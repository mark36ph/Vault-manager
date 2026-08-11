"""Asset acquisition with post-download visual relevance verification."""
from __future__ import annotations

from pathlib import Path
from typing import Callable, Iterable

from common.asset_acquisition import (
    AcquiredAsset,
    AssetAcquisitionEngine,
    AssetAcquisitionError,
    AssetCandidate,
    AssetProvider,
    Downloader,
    ProgressCallback,
    _candidate_key,
    _candidate_search_text,
    _fallback_search_queries,
    _relevance_words,
    _required_subject,
)

AssetVerifier = Callable[[str, AcquiredAsset], bool]


class VerifiedAssetAcquisitionEngine(AssetAcquisitionEngine):
    """Reject visually wrong images and choose the strongest accepted visual."""

    QUALITY_SCORE = {"weak": 0, "acceptable": 3, "preferred": 6}
    STYLE_SCORE = {"decorative": -10, "representational": 1, "literal": 2}
    SUBJECT_UNCERTAIN_PENALTY = 4
    MIN_QUALITY_SCAN = 5

    def __init__(
        self,
        providers: Iterable[AssetProvider],
        *,
        verifier: AssetVerifier,
        downloader: Downloader,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        super().__init__(
            providers,
            downloader=downloader,
            progress_callback=progress_callback,
        )
        self.verifier = verifier
        self.last_selected_quality = "preferred"
        self.last_selected_style = "literal"
        self.last_selected_subject_uncertain = False

    @staticmethod
    def _discard_rejected_asset(asset: AcquiredAsset) -> None:
        """Remove a rejected or superseded download so it is not reused."""
        try:
            Path(asset.path).unlink(missing_ok=True)
        except OSError:
            pass

    @staticmethod
    def _protect_asset(asset: AcquiredAsset, protected: set[str]) -> None:
        """Prevent a retained fallback from being downloaded over or deleted by later retries."""
        protected.add(_candidate_key(asset.candidate))
        protected.add(asset.candidate.url)

    def _verifier_decision(self) -> str:
        detail = str(getattr(self.verifier, "last_decision", "") or "").strip()
        return detail or "visual relevance rejected"

    def _verifier_quality(self) -> str:
        quality = str(getattr(self.verifier, "last_quality", "preferred") or "preferred").strip().lower()
        if quality not in self.QUALITY_SCORE:
            return "preferred"
        return quality

    def _verifier_style(self) -> str:
        style = str(getattr(self.verifier, "last_style", "literal") or "literal").strip().lower()
        if style not in self.STYLE_SCORE:
            return "literal"
        return style

    def _verifier_subject_uncertain(self) -> bool:
        return bool(getattr(self.verifier, "last_subject_uncertain", False))

    def _visual_score(self) -> tuple[int, str, str, bool]:
        quality = self._verifier_quality()
        style = self._verifier_style()
        uncertain = self._verifier_subject_uncertain()
        score = self.QUALITY_SCORE[quality] + self.STYLE_SCORE[style]
        if uncertain:
            score -= self.SUBJECT_UNCERTAIN_PENALTY
        return score, quality, style, uncertain

    def _try_candidates(
        self,
        query: str,
        candidates: list[AssetCandidate],
        folder: Path,
        *,
        attempts: int,
        excluded: set[str],
        failures: list[str],
        allow_reuse: bool = False,
        protected: set[str] | None = None,
    ) -> AcquiredAsset | None:
        protected_items = protected or set()

        def is_protected(item: AssetCandidate) -> bool:
            return _candidate_key(item) in protected_items or item.url in protected_items

        distinct = [
            item
            for item in candidates
            if not is_protected(item)
            and _candidate_key(item) not in excluded
            and item.url not in excluded
        ]
        if distinct:
            pool = distinct
        elif allow_reuse:
            pool = [item for item in candidates if not is_protected(item)]
        else:
            pool = []
        if not pool:
            return None

        scan_limit = min(max(attempts, self.MIN_QUALITY_SCAN), len(pool))
        total = scan_limit
        best_asset: AcquiredAsset | None = None
        best_score = -100
        best_quality = ""
        best_style = ""
        best_uncertain = False

        for index, candidate in enumerate(pool[:scan_limit], start=1):
            try:
                asset = self._download_candidate(candidate, folder, index, total)
            except Exception as error:
                failures.append(f"{candidate.provider}/{candidate.id}: {error}")
                continue

            if candidate.kind != "image":
                if best_asset is not None:
                    self._discard_rejected_asset(best_asset)
                self.last_selected_quality = "preferred"
                self.last_selected_style = "literal"
                self.last_selected_subject_uncertain = False
                return asset

            self._progress(
                "verify",
                index,
                total,
                f"Checking visual relevance: {candidate.title or candidate.id}",
            )
            try:
                accepted = bool(self.verifier(query, asset))
            except Exception as error:
                failures.append(
                    f"{candidate.provider}/{candidate.id}: visual verification failed: {error}"
                )
                self._discard_rejected_asset(asset)
                self._progress(
                    "verify",
                    index,
                    total,
                    "Visual verification failed; rejecting candidate and trying another asset",
                )
                continue

            if not accepted:
                decision = self._verifier_decision()
                failures.append(f"{candidate.provider}/{candidate.id}: {decision}")
                self._discard_rejected_asset(asset)
                self._progress(
                    "verify",
                    index,
                    total,
                    f"Visual relevance rejected ({decision}); trying another asset",
                )
                continue

            score, quality, style, uncertain = self._visual_score()
            if best_asset is None or score > best_score:
                if best_asset is not None:
                    self._discard_rejected_asset(best_asset)
                best_asset = asset
                best_score = score
                best_quality = quality
                best_style = style
                best_uncertain = uncertain
                suffix = ", subject uncertain" if uncertain else ""
                self._progress(
                    "verify",
                    index,
                    total,
                    f"Best visual so far: {quality}/{style} ({score}{suffix}); checking remaining candidates",
                )
            else:
                self._discard_rejected_asset(asset)
                self._progress(
                    "verify",
                    index,
                    total,
                    f"Accepted {quality}/{style} ({score}), but current {best_quality}/{best_style} ({best_score}) is stronger",
                )

        if best_asset is not None:
            self.last_selected_quality = best_quality
            self.last_selected_style = best_style
            self.last_selected_subject_uncertain = best_uncertain
            suffix = ", subject uncertain" if best_uncertain else ""
            self._progress(
                "verify",
                total,
                total,
                f"Selected best verified visual ({best_quality}/{best_style}, score {best_score}{suffix}) after comparing {total} candidate(s)",
            )
            return best_asset
        return None

    @staticmethod
    def _matches_original_subject(candidate: AssetCandidate, required_subject: str) -> bool:
        if not required_subject:
            return True
        return required_subject in set(_relevance_words(_candidate_search_text(candidate)))

    def _defer_candidate(
        self,
        asset: AcquiredAsset,
        *,
        decorative_fallback: AcquiredAsset | None,
        uncertain_fallback: AcquiredAsset | None,
    ) -> tuple[AcquiredAsset | None, AcquiredAsset | None, AcquiredAsset | None]:
        """Keep weak classes only as fallbacks while factual searches continue."""
        if self.last_selected_style == "decorative":
            if decorative_fallback is None:
                decorative_fallback = asset
                self._progress(
                    "verify",
                    1,
                    1,
                    "Decorative visual retained only as last resort; searching for factual imagery",
                )
            else:
                self._discard_rejected_asset(asset)
            return None, decorative_fallback, uncertain_fallback

        if self.last_selected_subject_uncertain:
            if uncertain_fallback is None:
                uncertain_fallback = asset
                self._progress(
                    "verify",
                    1,
                    1,
                    "Subject-uncertain visual retained as fallback; searching for a safer factual match",
                )
            else:
                self._discard_rejected_asset(asset)
            return None, decorative_fallback, uncertain_fallback

        if uncertain_fallback is not None:
            self._discard_rejected_asset(uncertain_fallback)
        if decorative_fallback is not None:
            self._discard_rejected_asset(decorative_fallback)
        return asset, None, None

    def acquire(
        self,
        query: str,
        destination_folder: str | Path,
        *,
        kind: str = "image",
        limit: int = 20,
        target_ratio: float | None = None,
        attempts: int = 3,
        excluded: set[str] | None = None,
    ) -> AcquiredAsset:
        if attempts < 1:
            raise ValueError("attempts must be at least 1")

        folder = Path(destination_folder)
        folder.mkdir(parents=True, exist_ok=True)
        blocked = set(excluded or ())
        protected: set[str] = set()
        failures: list[str] = []
        required_subject = _required_subject(query)
        decorative_fallback: AcquiredAsset | None = None
        uncertain_fallback: AcquiredAsset | None = None

        def protect_fallbacks() -> None:
            for fallback_asset in (decorative_fallback, uncertain_fallback):
                if fallback_asset is not None:
                    self._protect_asset(fallback_asset, protected)

        candidates = self.search(
            query,
            kind=kind,
            limit=limit,
            target_ratio=target_ratio,
            require_subject=bool(required_subject),
        )
        result = self._try_candidates(
            query,
            candidates,
            folder,
            attempts=attempts,
            excluded=blocked,
            failures=failures,
            protected=protected,
        )
        if result is not None:
            result, decorative_fallback, uncertain_fallback = self._defer_candidate(
                result,
                decorative_fallback=decorative_fallback,
                uncertain_fallback=uncertain_fallback,
            )
            protect_fallbacks()
            if result is not None:
                return result

        fallbacks = _fallback_search_queries(query)
        for index, fallback in enumerate(fallbacks, start=1):
            self._progress(
                "retry",
                index,
                len(fallbacks),
                f"Trying visually verified fallback: {fallback}",
            )
            candidates = self.search(
                fallback,
                kind=kind,
                limit=limit,
                target_ratio=target_ratio,
                require_subject=False,
            )
            if required_subject:
                candidates = [
                    candidate
                    for candidate in candidates
                    if self._matches_original_subject(candidate, required_subject)
                ]
            result = self._try_candidates(
                query,
                candidates,
                folder,
                attempts=attempts,
                excluded=blocked,
                failures=failures,
                protected=protected,
            )
            if result is not None:
                result, decorative_fallback, uncertain_fallback = self._defer_candidate(
                    result,
                    decorative_fallback=decorative_fallback,
                    uncertain_fallback=uncertain_fallback,
                )
                protect_fallbacks()
                if result is not None:
                    return result

        if required_subject:
            self._progress(
                "retry",
                1,
                1,
                "No safe subject match found; checking broader unused candidates",
            )
            candidates = self.search(
                query,
                kind=kind,
                limit=limit,
                target_ratio=target_ratio,
                require_subject=False,
            )
            result = self._try_candidates(
                query,
                candidates,
                folder,
                attempts=attempts,
                excluded=blocked,
                failures=failures,
                protected=protected,
            )
            if result is not None:
                result, decorative_fallback, uncertain_fallback = self._defer_candidate(
                    result,
                    decorative_fallback=decorative_fallback,
                    uncertain_fallback=uncertain_fallback,
                )
                protect_fallbacks()
                if result is not None:
                    return result

        if blocked:
            self._progress(
                "retry",
                1,
                1,
                "No unused safe visual found; checking previously used assets before weak fallbacks",
            )
            candidates = self.search(
                query,
                kind=kind,
                limit=limit,
                target_ratio=target_ratio,
                require_subject=False,
            )
            result = self._try_candidates(
                query,
                candidates,
                folder,
                attempts=attempts,
                excluded=blocked,
                failures=failures,
                allow_reuse=True,
                protected=protected,
            )
            if result is not None:
                result, decorative_fallback, uncertain_fallback = self._defer_candidate(
                    result,
                    decorative_fallback=decorative_fallback,
                    uncertain_fallback=uncertain_fallback,
                )
                protect_fallbacks()
                if result is not None:
                    return result

        if uncertain_fallback is not None:
            if decorative_fallback is not None:
                self._discard_rejected_asset(decorative_fallback)
            self._progress(
                "verify",
                1,
                1,
                "No certain factual visual found; using subject-uncertain factual fallback",
            )
            return uncertain_fallback

        if decorative_fallback is not None:
            self._progress(
                "verify",
                1,
                1,
                "No literal or representational visual passed; using decorative fallback as last resort",
            )
            return decorative_fallback

        if failures:
            raise AssetAcquisitionError("no visually relevant asset passed verification: " + "; ".join(failures))
        raise AssetAcquisitionError(f"no {kind} assets found for: {query}")


def install_visual_verification(engine: AssetAcquisitionEngine, verifier: AssetVerifier) -> AssetAcquisitionEngine:
    """Route an existing engine's acquire calls through the verified retry engine."""
    verified = VerifiedAssetAcquisitionEngine(
        engine.providers,
        verifier=verifier,
        downloader=engine.downloader,
        progress_callback=engine.progress_callback,
    )
    engine.acquire = verified.acquire  # type: ignore[method-assign]
    return engine


__all__ = [
    "AssetVerifier",
    "VerifiedAssetAcquisitionEngine",
    "install_visual_verification",
]
