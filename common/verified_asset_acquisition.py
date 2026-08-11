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

    QUALITY_RANK = {"weak": 1, "acceptable": 2, "preferred": 3}
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

    @staticmethod
    def _discard_rejected_asset(asset: AcquiredAsset) -> None:
        """Remove a rejected or superseded download so it is not reused."""
        try:
            Path(asset.path).unlink(missing_ok=True)
        except OSError:
            pass

    def _verifier_decision(self) -> str:
        detail = str(getattr(self.verifier, "last_decision", "") or "").strip()
        return detail or "visual relevance rejected"

    def _verifier_quality(self) -> str:
        quality = str(getattr(self.verifier, "last_quality", "preferred") or "preferred").strip().lower()
        if quality not in self.QUALITY_RANK:
            return "preferred"
        return quality

    def _try_candidates(
        self,
        query: str,
        candidates: list[AssetCandidate],
        folder: Path,
        *,
        attempts: int,
        excluded: set[str],
        failures: list[str],
    ) -> AcquiredAsset | None:
        distinct = [
            item
            for item in candidates
            if _candidate_key(item) not in excluded and item.url not in excluded
        ]
        pool = distinct or candidates
        scan_limit = min(max(attempts, self.MIN_QUALITY_SCAN), len(pool))
        total = scan_limit
        best_asset: AcquiredAsset | None = None
        best_quality = ""

        for index, candidate in enumerate(pool[:scan_limit], start=1):
            try:
                asset = self._download_candidate(candidate, folder, index, total)
            except Exception as error:
                failures.append(f"{candidate.provider}/{candidate.id}: {error}")
                continue

            if candidate.kind != "image":
                if best_asset is not None:
                    self._discard_rejected_asset(best_asset)
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

            quality = self._verifier_quality()
            if best_asset is None or self.QUALITY_RANK[quality] > self.QUALITY_RANK[best_quality]:
                if best_asset is not None:
                    self._discard_rejected_asset(best_asset)
                best_asset = asset
                best_quality = quality
                self._progress(
                    "verify",
                    index,
                    total,
                    f"Best verified visual so far: {quality}; checking remaining candidates",
                )
            else:
                self._discard_rejected_asset(asset)
                self._progress(
                    "verify",
                    index,
                    total,
                    f"Accepted {quality} visual, but existing {best_quality} candidate is stronger",
                )

        if best_asset is not None:
            self._progress(
                "verify",
                total,
                total,
                f"Selected best verified visual ({best_quality} quality) after comparing {total} candidate(s)",
            )
            return best_asset
        return None

    @staticmethod
    def _matches_original_subject(candidate: AssetCandidate, required_subject: str) -> bool:
        if not required_subject:
            return True
        return required_subject in set(_relevance_words(_candidate_search_text(candidate)))

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
        blocked = excluded or set()
        failures: list[str] = []
        required_subject = _required_subject(query)

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
        )
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
            )
            if result is not None:
                return result

        if required_subject:
            self._progress(
                "retry",
                1,
                1,
                "No verified subject match found; checking broader candidates",
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
            )
            if result is not None:
                return result

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
