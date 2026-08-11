"""Named-subject search and ranking policy for mixed production assets.

This module strengthens the mixed image/video selector without making the visual
verifier an impossible identity gate.  When a scene names a concrete entity, it
adds a focused search for that full entity and gives a decisive ranking bonus
only when provider-origin metadata independently supports the full name.

The result is a general hierarchy:

    provider-supported named match > plausible verified match > generic fallback

Visual verification still has final veto power, so metadata can never rescue a
visually contradictory asset.
"""
from __future__ import annotations

from dataclasses import replace
import re
from typing import Any

import common.mixed_asset_acquisition as mixed
from common.asset_acquisition import AcquiredAsset, AssetCandidate, _candidate_key
from common.named_subject_verification import named_subject_phrase


NAMED_IDENTITY_BONUS = 30


def _normalized_words(value: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", str(value or "").casefold()))


def named_identity_evidence(query: str, candidate: AssetCandidate) -> bool:
    """Return True only when provider-origin text independently contains the full entity.

    Some provider adapters use the search query itself as a fallback title.  That
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
        if key_text in {"query", "search_query", "requested_query"}:
            continue
        if isinstance(value, (str, int, float)):
            evidence_texts.append(_normalized_words(str(value)))

    return any(needle in text for text in evidence_texts if text)


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
        # scene query.  This lets exact-name stock results enter the same fixed
        # verification budget instead of increasing API verification cost.
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

            seen = {
                (_candidate_key(item), item.url)
                for item in by_kind[kind]
            }
            for candidate in focused:
                key = (_candidate_key(candidate), candidate.url)
                if key in seen or _candidate_key(candidate) in used or candidate.url in used:
                    continue
                by_kind[kind].append(candidate)
                seen.add(key)

            # Keep the existing verification budget.  Independent full-name
            # evidence outranks generic provider score, but visual verification
            # still decides whether each candidate is actually usable.
            by_kind[kind].sort(
                key=lambda candidate: (
                    int(named_identity_evidence(query, candidate)),
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

        if not named_identity_evidence(query, candidate):
            return asset, score, detail

        metadata = dict(asset.candidate.metadata)
        metadata["verified_named_identity"] = named_subject_phrase(query)
        boosted = AcquiredAsset(
            replace(asset.candidate, metadata=metadata),
            asset.path,
            asset.reused,
        )
        detail = dict(detail)
        detail["named_identity"] = metadata["verified_named_identity"]
        return boosted, score + NAMED_IDENTITY_BONUS, detail

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
    "install_named_asset_hierarchy",
    "named_identity_evidence",
]
