"""Named-subject search and ranking policy for mixed production assets.

This module strengthens the mixed image/video selector without making the visual
verifier an impossible identity gate. When a scene names a concrete entity, it
adds a focused search for that full entity and preserves four explicit tiers:

    provider-supported named match
        > focused named match with descriptive scene evidence
        > plausible named-search match
        > generic fallback

It also carries lightweight scene-order context into candidate scoring. A result
whose provider evidence strongly favors the previous scene's named subject over
the current scene receives a penalty instead of being allowed to win merely on
visual attractiveness. Visual verification still has final veto power.
"""
from __future__ import annotations

from dataclasses import replace
import re
from typing import Any

import common.mixed_asset_acquisition as mixed
from common.asset_acquisition import AcquiredAsset, AssetCandidate, _candidate_key
from common.named_subject_verification import named_subject_phrase


NAMED_IDENTITY_BONUS = 30
NAMED_DESCRIPTIVE_BONUS = 22
NAMED_SEARCH_BONUS = 15
CURRENT_SCENE_EVIDENCE_BONUS = 4
PREVIOUS_SUBJECT_PENALTY = 12
_NAMED_SEARCH_METADATA_KEY = "_named_subject_search"
_PREVIOUS_QUERY_METADATA_KEY = "_selection_previous_query"
_LAST_SCENE_QUERY = ""
_DESCRIPTIVE_STOP_WORDS = {
    "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from",
    "by", "at", "photo", "photography", "image", "video", "vertical", "portrait",
    "realistic", "documentary", "close", "up", "nature", "science", "history",
    "technology", "engineering", "health", "medicine", "animals", "animal",
    "ocean", "geography", "physics", "chemistry", "biology", "astronomy",
    "earth", "environment", "transport", "architecture",
}


def _normalized_words(value: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", str(value or "").casefold()))


def _provider_evidence_texts(query: str, candidate: AssetCandidate) -> list[str]:
    """Return provider-origin text while excluding search/query bookkeeping."""
    entity = named_subject_phrase(query)
    needle = _normalized_words(entity)
    query_text = _normalized_words(query)
    title_text = _normalized_words(candidate.title)
    evidence_texts: list[str] = []

    if title_text and title_text not in {query_text, needle}:
        evidence_texts.append(title_text)

    for key, value in candidate.metadata.items():
        key_text = str(key or "").casefold()
        if key_text in {
            "query",
            "search_query",
            "requested_query",
            _NAMED_SEARCH_METADATA_KEY,
            _PREVIOUS_QUERY_METADATA_KEY,
        }:
            continue
        if isinstance(value, (str, int, float)):
            normalized = _normalized_words(str(value))
            if normalized:
                evidence_texts.append(normalized)
    return evidence_texts


def named_identity_evidence(query: str, candidate: AssetCandidate) -> bool:
    """Return True only when provider-origin text independently contains the full entity.

    Some provider adapters use the search query itself as a fallback title. That
    echo is deliberately ignored, otherwise asking for ``Mauna Loa`` would count
    as evidence that every returned clip actually depicts Mauna Loa.
    """
    entity = named_subject_phrase(query)
    if not entity:
        return False

    needle = _normalized_words(entity)
    if not needle:
        return False

    return any(needle in text for text in _provider_evidence_texts(query, candidate))


def _is_focused_named_candidate(query: str, candidate: AssetCandidate) -> bool:
    entity = named_subject_phrase(query)
    if not entity:
        return False
    searched = candidate.metadata.get(_NAMED_SEARCH_METADATA_KEY)
    return _normalized_words(str(searched or "")) == _normalized_words(entity)


def named_descriptive_overlap(query: str, candidate: AssetCandidate) -> int:
    """Count distinct non-name scene terms independently supported by provider text."""
    entity_words = set(_normalized_words(named_subject_phrase(query)).split())
    query_words = [
        word
        for word in _normalized_words(query).split()
        if len(word) >= 3 and word not in entity_words and word not in _DESCRIPTIVE_STOP_WORDS
    ]
    if not query_words:
        return 0

    evidence_words: set[str] = set()
    for text in _provider_evidence_texts(query, candidate):
        evidence_words.update(text.split())
    return sum(1 for word in dict.fromkeys(query_words) if word in evidence_words)


def named_candidate_rank_tier(query: str, candidate: AssetCandidate) -> int:
    """Return 3 strong, 2 focused+descriptive, 1 focused, or 0 generic."""
    if named_identity_evidence(query, candidate):
        return 3
    if _is_focused_named_candidate(query, candidate):
        if named_descriptive_overlap(query, candidate) >= 2:
            return 2
        return 1
    return 0


def named_candidate_bonus(query: str, candidate: AssetCandidate) -> int:
    """Return a decisive post-verification score bonus for the named hierarchy."""
    tier = named_candidate_rank_tier(query, candidate)
    if tier == 3:
        return NAMED_IDENTITY_BONUS
    if tier == 2:
        return NAMED_DESCRIPTIVE_BONUS
    if tier == 1:
        return NAMED_SEARCH_BONUS
    return 0


def _provider_mentions_entity(query: str, entity: str, candidate: AssetCandidate) -> bool:
    needle = _normalized_words(entity)
    if not needle:
        return False
    return any(needle in text for text in _provider_evidence_texts(query, candidate))


def scene_context_adjustment(query: str, candidate: AssetCandidate) -> int:
    """Reward current-scene evidence and penalize clear previous-subject bleed.

    This is deliberately evidence-based rather than a hard transition gate. It
    only penalizes the previous subject when provider text supports that subject,
    the current scene does not still name it, and the candidate lacks meaningful
    evidence for the current scene. Neutral fallbacks therefore remain available.
    """
    adjustment = 0
    current_overlap = named_descriptive_overlap(query, candidate)
    if current_overlap >= 2:
        adjustment += CURRENT_SCENE_EVIDENCE_BONUS

    previous_query = str(candidate.metadata.get(_PREVIOUS_QUERY_METADATA_KEY) or "").strip()
    previous_entity = named_subject_phrase(previous_query)
    if not previous_entity:
        return adjustment

    current_entity = named_subject_phrase(query)
    if _normalized_words(previous_entity) == _normalized_words(current_entity):
        return adjustment

    previous_supported = _provider_mentions_entity(query, previous_entity, candidate)
    current_supported = bool(current_entity and named_identity_evidence(query, candidate))
    if previous_supported and not current_supported and current_overlap < 2:
        adjustment -= PREVIOUS_SUBJECT_PENALTY
    return adjustment


def _mark_focused_named_candidate(candidate: AssetCandidate, entity: str) -> AssetCandidate:
    metadata = dict(candidate.metadata)
    metadata[_NAMED_SEARCH_METADATA_KEY] = entity
    return replace(candidate, metadata=metadata)


def _mark_previous_query(candidate: AssetCandidate, previous_query: str) -> AssetCandidate:
    if not previous_query:
        return candidate
    metadata = dict(candidate.metadata)
    metadata[_PREVIOUS_QUERY_METADATA_KEY] = previous_query
    return replace(candidate, metadata=metadata)


def _install_acquisition_context_patch() -> None:
    """Reset scene-order state at the start of each multi-scene acquisition run."""
    original_acquire_many = mixed.acquire_mixed_many

    def acquire_mixed_many(*args, **kwargs):
        global _LAST_SCENE_QUERY
        _LAST_SCENE_QUERY = ""
        return original_acquire_many(*args, **kwargs)

    mixed.acquire_mixed_many = acquire_mixed_many  # type: ignore[method-assign]


def _install_candidate_pool_patch() -> None:
    original_pool = mixed._candidate_pool  # noqa: SLF001

    def candidate_pool(engine, query, *, limit, target_ratio, used):
        global _LAST_SCENE_QUERY
        previous_query = _LAST_SCENE_QUERY
        _LAST_SCENE_QUERY = str(query or "").strip()

        base = original_pool(
            engine,
            query,
            limit=limit,
            target_ratio=target_ratio,
            used=used,
        )
        base = [_mark_previous_query(candidate, previous_query) for candidate in base]
        entity = named_subject_phrase(query)
        if not entity:
            return base

        by_kind: dict[str, list[AssetCandidate]] = {"video": [], "image": []}
        for candidate in base:
            if candidate.kind in by_kind:
                by_kind[candidate.kind].append(candidate)

        # Search the complete named entity directly as well as the descriptive
        # scene query. A surviving focused-search candidate is a named fallback;
        # provider evidence for location/type/action raises it above a merely
        # plausible focused result without pretending that metadata proves pixels.
        for kind in ("video", "image"):
            try:
                focused = engine.search(
                    entity,
                    kind=kind,
                    limit=limit,
                    target_ratio=target_ratio,
                    require_subject=False,
                )
            except Exception:
                focused = []

            positions = {
                (_candidate_key(item), item.url): index
                for index, item in enumerate(by_kind[kind])
            }
            for candidate in focused:
                candidate_key = _candidate_key(candidate)
                key = (candidate_key, candidate.url)
                if candidate_key in used or candidate.url in used:
                    continue
                marked = _mark_focused_named_candidate(candidate, entity)
                marked = _mark_previous_query(marked, previous_query)
                existing_index = positions.get(key)
                if existing_index is not None:
                    by_kind[kind][existing_index] = marked
                    continue
                positions[key] = len(by_kind[kind])
                by_kind[kind].append(marked)

            # Keep the existing verification budget. Tier and current-scene
            # evidence are evaluated before provider score so stale previous-
            # subject media cannot crowd out better current-scene candidates.
            by_kind[kind].sort(
                key=lambda candidate: (
                    named_candidate_rank_tier(query, candidate),
                    scene_context_adjustment(query, candidate),
                    named_descriptive_overlap(query, candidate),
                    float(candidate.score),
                    max(0, candidate.width) * max(0, candidate.height),
                ),
                reverse=True,
            )
            by_kind[kind] = by_kind[kind][: mixed.VERIFY_PER_KIND]

        return [*by_kind["video"], *by_kind["image"]]

    mixed._candidate_pool = candidate_pool  # type: ignore[attr-defined]  # noqa: SLF001


def _install_verified_score_patch() -> None:
    original_verify = mixed._verify_candidate  # noqa: SLF001

    def verify_candidate(engine, verifier, query, candidate, folder, index, total):
        asset, score, detail = original_verify(
            engine,
            verifier,
            query,
            candidate,
            folder,
            index,
            total,
        )
        if asset is None:
            return asset, score, detail

        tier = named_candidate_rank_tier(query, candidate)
        bonus = named_candidate_bonus(query, candidate)
        context_adjustment = scene_context_adjustment(query, candidate)
        if not bonus and not context_adjustment:
            return asset, score, detail

        entity = named_subject_phrase(query)
        metadata = dict(asset.candidate.metadata)
        if bonus:
            metadata["verified_named_identity"] = entity
            metadata["verified_named_identity_tier"] = tier
            metadata["verified_named_descriptive_overlap"] = named_descriptive_overlap(query, candidate)
        metadata["verified_scene_context_adjustment"] = context_adjustment
        boosted = AcquiredAsset(
            replace(asset.candidate, metadata=metadata),
            asset.path,
            asset.reused,
        )
        detail = dict(detail)
        if bonus:
            detail["named_identity"] = entity
            detail["named_identity_tier"] = tier
            detail["named_descriptive_overlap"] = metadata["verified_named_descriptive_overlap"]
        detail["scene_context_adjustment"] = context_adjustment
        return boosted, score + bonus + context_adjustment, detail

    mixed._verify_candidate = verify_candidate  # type: ignore[attr-defined]  # noqa: SLF001


def install_named_asset_hierarchy() -> None:
    """Install the named-subject and scene-context hierarchy once."""
    if getattr(mixed, "_named_asset_hierarchy_installed", False):
        return
    _install_acquisition_context_patch()
    _install_candidate_pool_patch()
    _install_verified_score_patch()
    mixed._named_asset_hierarchy_installed = True


__all__ = [
    "CURRENT_SCENE_EVIDENCE_BONUS",
    "NAMED_DESCRIPTIVE_BONUS",
    "NAMED_IDENTITY_BONUS",
    "NAMED_SEARCH_BONUS",
    "PREVIOUS_SUBJECT_PENALTY",
    "install_named_asset_hierarchy",
    "named_candidate_bonus",
    "named_candidate_rank_tier",
    "named_descriptive_overlap",
    "named_identity_evidence",
    "scene_context_adjustment",
]
