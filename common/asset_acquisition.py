"""Provider-neutral asset search, ranking, download, and reuse."""
from __future__ import annotations

import hashlib
import re
import shutil
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Protocol, Sequence

from common.scene_asset_planning import plan_visual_queries


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

_RELEVANCE_STOP_WORDS = {
    "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from",
    "by", "at", "photo", "photography", "image", "video", "vertical", "portrait",
    "realistic", "documentary", "close", "up",
}

_BROAD_QUERY_ANCHORS = {
    "space", "science", "nature", "history", "technology", "engineering", "health",
    "medicine", "animals", "animal", "ocean", "geography", "physics", "chemistry",
    "biology", "astronomy", "earth", "environment", "transport", "architecture",
}

_TOPIC_STOP_WORDS = _RELEVANCE_STOP_WORDS | _BROAD_QUERY_ANCHORS | {
    "fact", "facts", "takes", "take", "longer", "shorter", "more", "less", "than",
    "is", "are", "was", "were", "has", "have", "had", "can", "could", "does", "did",
    "why", "how", "what", "when", "where", "first", "last", "great", "biggest",
    "largest", "smallest", "oldest", "newest", "fastest", "slowest",
}


def _download_headers(url: str) -> dict[str, str]:
    headers = {
        "User-Agent": "FactVaultManager/1.0 (+desktop media downloader)",
        "Accept": "image/avif,image/webp,image/apng,image/svg+xml,image/*,video/*;q=0.9,*/*;q=0.8",
    }
    host = urllib.parse.urlparse(url).hostname or ""
    if host == "pixabay.com" or host.endswith(".pixabay.com"):
        headers["Referer"] = "https://pixabay.com/"
    elif host == "pexels.com" or host.endswith(".pexels.com"):
        headers["Referer"] = "https://www.pexels.com/"
    return headers


def _default_downloader(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers=_download_headers(url))
    with urllib.request.urlopen(request, timeout=30) as response, destination.open("wb") as output:
        shutil.copyfileobj(response, output)


def _safe_filename(
    value: str,
    fallback: str = "asset",
    max_length: int = 60,
) -> str:
    cleaned = "".join(
        ch if ch.isalnum() or ch in "._-" else "_"
        for ch in str(value or "")
    ).strip("._")

    while "__" in cleaned:
        cleaned = cleaned.replace("__", "_")

    cleaned = cleaned[:max_length].rstrip("._-")
    return cleaned or fallback


def _candidate_key(candidate: AssetCandidate) -> str:
    return f"{candidate.provider}:{candidate.id or candidate.url}"


def _relevance_words(value: str) -> list[str]:
    """Return distinct meaningful words for query/candidate relevance checks."""
    words = re.findall(r"[A-Za-z0-9]+", str(value or "").casefold())
    result: list[str] = []
    for word in words:
        if len(word) < 3 or word in _RELEVANCE_STOP_WORDS or word in result:
            continue
        result.append(word)
    return result


def _topic_subject(value: str, category: str = "") -> str:
    """Extract a stable concrete subject word from a project title/topic."""
    category_words = set(_relevance_words(category))
    for word in re.findall(r"[A-Za-z0-9]+", str(value or "")):
        key = word.casefold()
        if len(key) < 3 or key in _TOPIC_STOP_WORDS or key in category_words:
            continue
        return word
    return ""


def _required_subject(query: str) -> str:
    """Return the explicit subject from a category-anchored production query."""
    words = _relevance_words(query)
    if len(words) >= 2 and words[0] in _BROAD_QUERY_ANCHORS:
        return words[1]
    return ""


def _candidate_search_text(candidate: AssetCandidate) -> str:
    """Combine provider-visible candidate text without relying on provider-specific fields."""
    metadata_text = " ".join(
        str(value)
        for value in candidate.metadata.values()
        if isinstance(value, (str, int, float))
    )
    return " ".join((candidate.title, metadata_text))


def _candidate_relevance(candidate: AssetCandidate, query: str) -> tuple[int, int, int, float]:
    """Score lexical relevance, strongly weighting the query's concrete subject."""
    query_words = _relevance_words(query)
    if not query_words:
        return (0, 0, 0, 0.0)

    candidate_words = set(_relevance_words(_candidate_search_text(candidate)))
    overlap = [word for word in query_words if word in candidate_words]
    required_subject = _required_subject(query)
    primary_subject = required_subject or query_words[0]
    subject_match = int(primary_subject in candidate_words)
    leading_match = int(query_words[0] in candidate_words)
    early_overlap = sum(1 for word in query_words[:4] if word in candidate_words)
    coverage = len(overlap) / len(query_words)
    return (subject_match, early_overlap, leading_match, coverage)


def _prefer_subject_matches(
    candidates: list[AssetCandidate],
    query: str,
    *,
    require_subject: bool = False,
) -> list[AssetCandidate]:
    """Prefer subject matches, but retain generic results when no match exists."""
    if not candidates:
        return candidates

    subject = _required_subject(query)
    if not subject:
        return candidates

    subject_matches = [
        candidate
        for candidate in candidates
        if subject in set(_relevance_words(_candidate_search_text(candidate)))
    ]
    if subject_matches:
        return subject_matches
    return [] if require_subject else candidates


def _fallback_search_queries(query: str) -> tuple[str, ...]:
    """Build subject-preserving broader stock-media searches."""
    original = str(query or "").strip()
    words = re.findall(r"[A-Za-z0-9][A-Za-z0-9'’-]*", original)
    variants: list[str] = []
    seen = {original.casefold()}

    def add(value: str) -> None:
        candidate = " ".join(str(value or "").split()).strip()
        key = candidate.casefold()
        if candidate and key not in seen:
            variants.append(candidate)
            seen.add(key)

    cleaned = " ".join(words).strip()
    add(cleaned)

    meaningful = _relevance_words(original)
    required_subject = _required_subject(original)
    if required_subject:
        subject_text = next(
            (word for word in words if word.casefold() == required_subject),
            required_subject,
        )
        anchor_text = words[0] if words else meaningful[0]
        after_subject = [word for word in meaningful[2:] if word != required_subject]
        add(" ".join([anchor_text, subject_text, *after_subject[:2]]))

        # The final scene-query word is often the concrete visual class
        # (planet, bridge, animal, engine). Try those simple subject+noun
        # searches before a fully generic fallback.
        for word in reversed(after_subject):
            add(f"{subject_text} {word}")

        add(" ".join([subject_text, *after_subject[:2]]))
        add(subject_text)

    for length in (12, 9, 6, 4):
        if len(words) <= length:
            continue
        add(" ".join(words[:length]))

    return tuple(variants)


def _ensure_subject_in_queries(queries: Iterable[str], context: Any) -> list[str]:
    """Keep every scene query tied to the project's concrete subject when available."""
    project = getattr(context, "project", None)
    category = ""
    if isinstance(project, Mapping):
        category = str(project.get("category") or "").strip()
    topic = str(getattr(context, "topic", "") or "").strip()
    subject = _topic_subject(topic, category)

    result: list[str] = []
    for raw in queries:
        query = " ".join(str(raw or "").split()).strip()
        if not query:
            continue
        words = _relevance_words(query)
        query_subject = _required_subject(query)
        if subject and subject.casefold() not in words:
            if query_subject and words:
                parts = query.split()
                query = " ".join([parts[0], subject, *parts[1:]])
            else:
                query = f"{subject} {query}".strip()
        result.append(query)
    return result


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
    def rank(
        candidates: Iterable[AssetCandidate],
        *,
        target_ratio: float | None = None,
        query: str = "",
    ) -> list[AssetCandidate]:
        unique: dict[str, AssetCandidate] = {}
        for candidate in candidates:
            key = candidate.url or _candidate_key(candidate)
            previous = unique.get(key)
            if previous is None or candidate.score > previous.score:
                unique[key] = candidate

        def ranking(candidate: AssetCandidate) -> tuple[Any, ...]:
            pixels = max(0, candidate.width) * max(0, candidate.height)
            ratio_bonus = 0.0
            if target_ratio and candidate.width > 0 and candidate.height > 0:
                ratio = candidate.width / candidate.height
                ratio_bonus = max(0.0, 1.0 - abs(ratio - target_ratio))

            if query:
                return (
                    *_candidate_relevance(candidate, query),
                    float(candidate.score) + ratio_bonus,
                    pixels,
                    -float(candidate.duration),
                )

            return (float(candidate.score) + ratio_bonus, pixels, -float(candidate.duration))

        return sorted(unique.values(), key=ranking, reverse=True)

    def search(
        self,
        query: str,
        *,
        kind: str = "image",
        limit: int = 20,
        target_ratio: float | None = None,
        require_subject: bool = False,
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
            collected.extend(result for result in results if result.kind == kind and result.url)
        ranked = self.rank(collected, target_ratio=target_ratio, query=str(query))
        ranked = _prefer_subject_matches(ranked, str(query), require_subject=require_subject)
        if not ranked and errors:
            raise AssetAcquisitionError("; ".join(errors))
        return ranked[:limit]

    @staticmethod
    def _destination(candidate: AssetCandidate, folder: Path) -> Path:
        suffix = Path(candidate.url.split("?", 1)[0]).suffix or (".mp4" if candidate.kind == "video" else ".jpg")
        digest = hashlib.sha256(_candidate_key(candidate).encode("utf-8")).hexdigest()[:12]
        stem = _safe_filename(candidate.title or candidate.id, "asset")
        return folder / f"{stem}_{digest}{suffix}"

    def _download_candidate(self, candidate: AssetCandidate, folder: Path, index: int, total: int) -> AcquiredAsset:
        destination = self._destination(candidate, folder)
        if destination.is_file() and destination.stat().st_size > 0:
            self._progress("download", index, total, f"Reusing {destination.name}")
            return AcquiredAsset(candidate=candidate, path=destination, reused=True)
        temporary = destination.with_suffix(destination.suffix + ".part")
        temporary.unlink(missing_ok=True)
        self._progress("download", index, total, f"Downloading from {candidate.provider}")
        try:
            self.downloader(candidate.url, temporary)
            if not temporary.is_file() or temporary.stat().st_size == 0:
                raise OSError("downloaded file is empty")
            temporary.replace(destination)
            return AcquiredAsset(candidate=candidate, path=destination, reused=False)
        except Exception:
            temporary.unlink(missing_ok=True)
            raise

    def acquire(self, query: str, destination_folder: str | Path, *, kind: str = "image", limit: int = 20, target_ratio: float | None = None, attempts: int = 3, excluded: set[str] | None = None) -> AcquiredAsset:
        if attempts < 1:
            raise ValueError("attempts must be at least 1")
        folder = Path(destination_folder)
        folder.mkdir(parents=True, exist_ok=True)
        required_subject = _required_subject(query)
        candidates = self.search(
            query,
            kind=kind,
            limit=limit,
            target_ratio=target_ratio,
            require_subject=bool(required_subject),
        )
        if not candidates:
            fallbacks = _fallback_search_queries(query)
            for index, fallback in enumerate(fallbacks, start=1):
                self._progress(
                    "retry",
                    index,
                    len(fallbacks),
                    f"No subject match; trying: {fallback}",
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
                        if required_subject
                        in set(_relevance_words(_candidate_search_text(candidate)))
                    ]
                if candidates:
                    break
        if not candidates and required_subject:
            self._progress(
                "retry",
                1,
                1,
                "No direct subject match found; using best broader result",
            )
            candidates = self.search(
                query,
                kind=kind,
                limit=limit,
                target_ratio=target_ratio,
                require_subject=False,
            )
        if not candidates:
            raise AssetAcquisitionError(f"no {kind} assets found for: {query}")
        blocked = excluded or set()
        distinct = [item for item in candidates if _candidate_key(item) not in blocked and item.url not in blocked]
        pool = distinct or candidates
        failures: list[str] = []
        for index, candidate in enumerate(pool[:attempts], start=1):
            try:
                return self._download_candidate(candidate, folder, index, min(attempts, len(pool)))
            except Exception as error:
                failures.append(f"{candidate.provider}/{candidate.id}: {error}")
        raise AssetAcquisitionError("all asset downloads failed: " + "; ".join(failures))

    def acquire_many(self, queries: Iterable[str], destination_folder: str | Path, *, unique: bool = False, **options: Any) -> list[AcquiredAsset]:
        items = [str(query).strip() for query in queries if str(query).strip()]
        results: list[AcquiredAsset] = []
        used: set[str] = set()
        for index, query in enumerate(items, start=1):
            self._progress("acquire", index, len(items), query)
            result = self.acquire(query, destination_folder, excluded=used if unique else None, **options)
            results.append(result)
            if unique:
                used.add(_candidate_key(result.candidate))
                used.add(result.candidate.url)
        return results


def make_asset_acquisition_provider(engine: AssetAcquisitionEngine, destination_folder: str | Path, *, kind: str = "image"):
    """Return a production provider that prepares and acquires one visual per scene."""
    def run(context):
        prompts = context.image_prompts
        if isinstance(prompts, str):
            prompts = prompts.splitlines()
        plan = plan_visual_queries(
            str(getattr(context, "script", "") or ""),
            prompts or (),
            topic=str(getattr(context, "topic", "") or ""),
        )
        context.image_prompts = _ensure_subject_in_queries(plan.queries, context)
        if hasattr(context, "warnings") and plan.generated_fallbacks:
            context.warnings.append(
                f"Generated {plan.generated_fallbacks} fallback visual searches for {plan.scene_count} scenes"
            )
        return engine.acquire_many(context.image_prompts, destination_folder, kind=kind, unique=True)

    return run


__all__ = [
    "AcquiredAsset", "AssetAcquisitionEngine", "AssetAcquisitionError", "AssetCandidate",
    "AssetProvider", "make_asset_acquisition_provider",
]
