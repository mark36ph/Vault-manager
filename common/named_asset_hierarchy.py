"""Named-subject search and ranking policy for mixed production assets.

This module strengthens the mixed image/video selector without making the visual
verifier an impossible identity gate. When a scene names a concrete entity, it
adds a focused search for that full entity and preserves three explicit tiers:

    provider-supported named match > plausible named-search match > generic fallback

Visual verification still has final veto power, so metadata or search origin can
never rescue a visually contradictory asset.
"""
from __future__ import annotations

from dataclasses import replace
import re
from typing import Any

import common.mixed_asset_acquisition as mixed
from common.asset_acquisition import AcquiredAsset, AssetCandidate, _candidate_key
from common.named_subject_verification import named_subject_phrase


NAMED_IDENTITY_BONUS = 30
NAMED_SEARCH_BONUS = 15
_NAMED_SEARCH_METADATA_KEY = "_named_subject_search"


def _normalized_words(value: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", str(value or "").casefold()))


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

    query_text = _normalized_words(query)
    title_text = _normalized_words(candidate.title)
    evidence_texts: list[str] = []

    # A title that is just either the submitted scene query or the focused
    # entity-only query is not provider evidence. Pexels video results can echo
    # either form when the API does not supply a real descriptive title.
    if title_text and title_text not in {query_text, needle}:
        evidence_texts.append(title_text)

    for key, value in candidate.metadata.items():
        key_text = str(key or "").casefold()
        if key_text in {
            "query",
            "search_query",
            "requested_query",
            _NAMED_SEARCH_METADATA_KEY,
        }:
            continue
        if isinstance(value, (str, int, float)):
            evidence_texts.append(_normalized_words(str(value)))

    return any(needle in text for text in evidence_texts if text)


def _is_focused_named_candidate(query: str, candidate: AssetCandidate) -> bool:
    entity = named_subject_phrase(query)
    if not entity:
        return False
    searched = candidate.metadata.get(_NAMED_SEARCH_METADATA_KEY)
    return _normalized_words(str(searched or "")) == _normalized_words(entity)


def named_candidate_rank_tier(query: str, candidate: AssetCandidate) -> int:
    """Return 2 for strong named evidence, 1 for focused named search, else 0."""
    if named_identity_evidence(query, candidate):
        return 2
    if _is_focused_named_candidate(query, candidate):
        return 1
    return 0


def named_candidate_bonus(query: str, candidate: AssetCandidate) -> int:
    """Return a decisive post-verification score bonus for the named hierarchy."""
    tier = named_candidate_rank_tier(query, candidate)
    if tier == 2:
        return NAMED_IDENTITY_BONUS
    if tier == 1:
        return NAMED_SEARCH_BONUS
    return 0


def _mark_focused_named_candidate(candidate: AssetCandidate, entity: str) -> AssetCandidate:
    metadata = dict(candidate.metadata)
    metadata[_NAMED_SEARCH_METADATA_KEY] = entity
    return replace(candidate, metadata=metadata)


def _install_candidate_pool_patch() -> None:
    original_pool = mixed._candidate_pool  # noqa: SLF001

    def candidate_pool(engine, query, *, limit, target_ratio, used):
        base = original_pool(
            engine,
            query,
            limit=limit,
            target_ratio=target_ratio,
            used=used,
        )
        entity = named_subject_phrase(query)
        if not entity:
            return base

        by_kind: dict[str, list[AssetCandidate]] = {"video": [], "image": []}
        for candidate in base:
            if candidate.kind in by_kind:
                by_kind[candidate.kind].append(candidate)

        # Search the complete named entity directly as well as the descriptive
        # scene query. A surviving focused-search candidate is the middle tier:
        # plausible for the named subject even when pixels/metadata cannot prove
        # unique identity. That tier must outrank generic same-class footage.
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
                existing_index = positions.get(key)
                if existing_index is not None:
                    # The same provider asset can appear in both searches. Keep
                    # its named-search provenance instead of treating it generic.
                    by_kind[kind][existing_index] = marked
                    continue
                positions[key] = len(by_kind[kind])
                by_kind[kind].append(marked)

            # Keep the existing verification budget. Tier is evaluated before
            # provider score so a generic visually attractive result cannot push
            # all named-subject candidates out of the verification pool.
            by_kind[kind].sort(
                key=lambda candidate: (
                    named_candidate_rank_tier(query, candidate),
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
        if not bonus:
            return asset, score, detail

        entity = named_subject_phrase(query)
        metadata = dict(asset.candidate.metadata)
        metadata["verified_named_identity"] = entity
        metadata["verified_named_identity_tier"] = tier
        boosted = AcquiredAsset(
            replace(asset.candidate, metadata=metadata),
            asset.path,
            asset.reused,
        )
        detail = dict(detail)
        detail["named_identity"] = entity
        detail["named_identity_tier"] = tier
        return boosted, score + bonus, detail

    mixed._verify_candidate = verify_candidate  # type: ignore[attr-defined]  # noqa: SLF001


def install_named_asset_hierarchy() -> None:
    """Install the named-subject hierarchy once for the mixed selector module."""
    if getattr(mixed, "_named_asset_hierarchy_installed", False):
        return
    _install_candidate_pool_patch()
    _install_verified_score_patch()
    mixed._named_asset_hierarchy_installed = True


__all__ = [
    "NAMED_IDENTITY_BONUS",
    "NAMED_SEARCH_BONUS",
    "install_named_asset_hierarchy",
    "named_candidate_bonus",
    "named_candidate_rank_tier",
    "named_identity_evidence",
]
