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
    """Reject visually wrong downloaded images and continue searching."""

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
        """Remove a rejected download so it cannot be reused as a cached candidate."""
        try:
            Path(asset.path).unlink(missing_ok=True)
        except OSError:
            pass

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
        total = min(attempts, len(pool))

        for index, candidate in enumerate(pool[:attempts], start=1):
            try:
                asset = self._download_candidate(candidate, folder, index, total)
            except Exception as error:
                failures.append(f"{candidate.provider}/{candidate.id}: {error}")
                continue

            if candidate.kind != "image":
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
                # Visual verification is now a real quality gate. An unavailable,
                # malformed, or uncertain verifier result must not silently allow a
                # visibly wrong asset into the final video.
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

            if accepted:
                self._progress("verify", index, total, "Visual relevance accepted")
                return asset

            failures.append(f"{candidate.provider}/{candidate.id}: visual relevance rejected")
            self._discard_rejected_asset(asset)
            self._progress("verify", index, total, "Visual relevance rejected; trying another asset")

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
