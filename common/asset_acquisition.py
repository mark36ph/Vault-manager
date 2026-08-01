"""Provider-neutral asset search, ranking, download, and reuse."""
from __future__ import annotations

import hashlib
import shutil
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Protocol, Sequence


class AssetAcquisitionError(RuntimeError):
    """Raised when no provider can acquire a usable asset."""


@dataclass(frozen=True)
class AssetCandidate:
    provider: str
    id: str
    url: str
    kind: str = "image"
    title: str = ""
    width: int = 0
    height: int = 0
    duration: float = 0.0
    score: float = 0.0
    credit: str = ""
    license: str = ""
    metadata: Mapping[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class AcquiredAsset:
    candidate: AssetCandidate
    path: Path
    reused: bool = False


class AssetProvider(Protocol):
    name: str

    def search(self, query: str, *, kind: str, limit: int) -> Sequence[AssetCandidate]: ...


Downloader = Callable[[str, Path], None]
ProgressCallback = Callable[[str, int, int, str], None]


def _download_headers(url: str) -> dict[str, str]:
    """Return conservative browser-compatible headers for media CDNs."""
    headers = {
        "User-Agent": "FactVaultManager/1.0 (+desktop media downloader)",
        "Accept": "image/avif,image/webp,image/apng,image/svg+xml,image/*,video/*;q=0.9,*/*;q=0.8",
    }
    host = urllib.parse.urlparse(url).hostname or ""
    if host == "pixabay.com" or host.endswith(".pixabay.com") or host.endswith(".pixabay.com"):
        headers["Referer"] = "https://pixabay.com/"
    elif host == "pexels.com" or host.endswith(".pexels.com"):
        headers["Referer"] = "https://www.pexels.com/"
    return headers


def _default_downloader(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers=_download_headers(url))
    with urllib.request.urlopen(request, timeout=30) as response, destination.open("wb") as output:
        shutil.copyfileobj(response, output)


def _safe_filename(value: str, fallback: str = "asset") -> str:
    cleaned = "".join(ch if ch.isalnum() or ch in "._-" else "_" for ch in value).strip("._")
    return cleaned or fallback


def _candidate_key(candidate: AssetCandidate) -> str:
    return f"{candidate.provider}:{candidate.id or candidate.url}"


class AssetAcquisitionEngine:
    """Search providers, rank candidates, reuse cached files, and download safely."""

    def __init__(
        self,
        providers: Iterable[AssetProvider],
        *,
        downloader: Downloader = _default_downloader,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self.providers = tuple(providers)
        if not self.providers:
            raise ValueError("at least one asset provider is required")
        self.downloader = downloader
        self.progress_callback = progress_callback

    def _progress(self, stage: str, current: int, total: int, message: str) -> None:
        if self.progress_callback is not None:
            self.progress_callback(stage, current, total, message)

    @staticmethod
    def rank(candidates: Iterable[AssetCandidate], *, target_ratio: float | None = None) -> list[AssetCandidate]:
        unique: dict[str, AssetCandidate] = {}
        for candidate in candidates:
            key = candidate.url or _candidate_key(candidate)
            previous = unique.get(key)
            if previous is None or candidate.score > previous.score:
                unique[key] = candidate

        def ranking(candidate: AssetCandidate) -> tuple[float, int, float]:
            pixels = max(0, candidate.width) * max(0, candidate.height)
            ratio_bonus = 0.0
            if target_ratio and candidate.width > 0 and candidate.height > 0:
                ratio = candidate.width / candidate.height
                ratio_bonus = max(0.0, 1.0 - abs(ratio - target_ratio))
            return (float(candidate.score) + ratio_bonus, pixels, -float(candidate.duration))

        return sorted(unique.values(), key=ranking, reverse=True)

    def search(
        self,
        query: str,
        *,
        kind: str = "image",
        limit: int = 20,
        target_ratio: float | None = None,
    ) -> list[AssetCandidate]:
        if not str(query).strip():
            raise ValueError("query is required")
        collected: list[AssetCandidate] = []
        errors: list[str] = []
        for index, provider in enumerate(self.providers, start=1):
            self._progress("search", index, len(self.providers), f"Searching {provider.name}")
            try:
                results = provider.search(str(query), kind=kind, limit=limit)
            except Exception as error:
                errors.append(f"{provider.name}: {error}")
                continue
            for result in results:
                if result.kind == kind and result.url:
                    collected.append(result)
        ranked = self.rank(collected, target_ratio=target_ratio)
        if not ranked and errors:
            raise AssetAcquisitionError("; ".join(errors))
        return ranked[:limit]

    @staticmethod
    def _destination(candidate: AssetCandidate, folder: Path) -> Path:
        suffix = Path(candidate.url.split("?", 1)[0]).suffix or (".mp4" if candidate.kind == "video" else ".jpg")
        digest = hashlib.sha256(_candidate_key(candidate).encode("utf-8")).hexdigest()[:12]
        stem = _safe_filename(candidate.title or candidate.id, "asset")
        return folder / f"{stem}_{digest}{suffix}"

    def acquire(
        self,
        query: str,
        destination_folder: str | Path,
        *,
        kind: str = "image",
        limit: int = 20,
        target_ratio: float | None = None,
        attempts: int = 3,
    ) -> AcquiredAsset:
        if attempts < 1:
            raise ValueError("attempts must be at least 1")
        folder = Path(destination_folder)
        folder.mkdir(parents=True, exist_ok=True)
        candidates = self.search(query, kind=kind, limit=limit, target_ratio=target_ratio)
        if not candidates:
            raise AssetAcquisitionError(f"no {kind} assets found for: {query}")

        failures: list[str] = []
        for index, candidate in enumerate(candidates[:attempts], start=1):
            destination = self._destination(candidate, folder)
            if destination.is_file() and destination.stat().st_size > 0:
                self._progress("download", index, attempts, f"Reusing {destination.name}")
                return AcquiredAsset(candidate=candidate, path=destination, reused=True)
            temporary = destination.with_suffix(destination.suffix + ".part")
            temporary.unlink(missing_ok=True)
            self._progress("download", index, attempts, f"Downloading from {candidate.provider}")
            try:
                self.downloader(candidate.url, temporary)
                if not temporary.is_file() or temporary.stat().st_size == 0:
                    raise OSError("downloaded file is empty")
                temporary.replace(destination)
                return AcquiredAsset(candidate=candidate, path=destination, reused=False)
            except Exception as error:
                temporary.unlink(missing_ok=True)
                failures.append(f"{candidate.provider}/{candidate.id}: {error}")
        raise AssetAcquisitionError("all asset downloads failed: " + "; ".join(failures))

    def acquire_many(
        self,
        queries: Iterable[str],
        destination_folder: str | Path,
        **options: Any,
    ) -> list[AcquiredAsset]:
        items = [str(query).strip() for query in queries if str(query).strip()]
        results: list[AcquiredAsset] = []
        for index, query in enumerate(items, start=1):
            self._progress("acquire", index, len(items), query)
            results.append(self.acquire(query, destination_folder, **options))
        return results


def make_asset_acquisition_provider(
    engine: AssetAcquisitionEngine,
    destination_folder: str | Path,
    *,
    kind: str = "image",
):
    """Return a ContentProductionEngine-compatible image-prompts provider."""
    def run(context):
        prompts = context.image_prompts
        if prompts is None:
            prompts = [scene.narration or scene.title for scene in context.timeline.scenes] if context.timeline else []
        if isinstance(prompts, str):
            prompts = [prompts]
        return engine.acquire_many(prompts or [], destination_folder, kind=kind)

    return run


__all__ = [
    "AcquiredAsset",
    "AssetAcquisitionEngine",
    "AssetAcquisitionError",
    "AssetCandidate",
    "AssetProvider",
    "make_asset_acquisition_provider",
]
