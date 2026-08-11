"""Prepare one useful visual search query for every script scene."""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Iterable

from timeline import SceneBuilder


@dataclass(frozen=True)
class VisualQueryPlan:
    scene_count: int
    queries: tuple[str, ...]
    generated_fallbacks: int


_PREFIX = re.compile(r"^\s*(?:[-*•]+|\d+[.)\-:]|scene\s+\d+\s*[:.)-]?)\s*", re.IGNORECASE)
_HEADINGS = {"visuals", "images", "image prompts", "search queries", "queries", "scenes"}
_TRANSITION_CUE = re.compile(
    r"(?:^|[.!?]\s+|\b)(?:but|however|instead|rather|yet|actually|unlike|whereas|although|though|compared\s+with|compared\s+to)\b",
    re.IGNORECASE,
)
_QUERY_STOP_WORDS = {
    "a", "an", "and", "the", "of", "for", "to", "in", "on", "with", "from",
    "by", "at", "photo", "photography", "image", "video", "vertical", "portrait",
    "realistic", "documentary", "close", "up", "wide", "shot", "scene", "stock",
    "nature", "science", "history", "technology", "engineering", "geography",
    "architecture", "environment",
}
_NARRATION_STOP_WORDS = _QUERY_STOP_WORDS | {
    "but", "however", "instead", "rather", "yet", "actually", "unlike", "whereas",
    "although", "though", "this", "that", "these", "those", "its", "their", "there",
    "here", "into", "than", "then", "also", "very", "much", "many", "more", "most",
    "less", "least", "not", "isn", "isnt", "is", "are", "was", "were", "be", "been",
    "being", "has", "have", "had", "does", "do", "did", "can", "could", "would",
    "should", "will", "just", "really", "comparison", "compared",
}


def clean_visual_query(value: str) -> str:
    query = _PREFIX.sub("", str(value or "")).strip(" \t\"'")
    query = " ".join(query.split())
    if not query or query.casefold().rstrip(":") in _HEADINGS:
        return ""
    return query


def _scene_fallback(topic: str, scene, index: int) -> str:
    detail = str(getattr(scene, "narration", "") or getattr(scene, "title", "") or "").strip()
    words = re.findall(r"[\w'-]+", detail)[:10]
    core = " ".join(words)
    base = " ".join(part for part in (str(topic or "").strip(), core) if part).strip()
    return base or f"factual video scene {index}"


def _query_words(value: str) -> set[str]:
    return {
        word
        for word in re.findall(r"[a-z0-9]+", str(value or "").casefold())
        if len(word) >= 3 and word not in _QUERY_STOP_WORDS
    }


def _query_similarity(left: str, right: str) -> float:
    left_words = _query_words(left)
    right_words = _query_words(right)
    if not left_words or not right_words:
        return 0.0
    return len(left_words & right_words) / len(left_words | right_words)


def _is_transition_scene(scene) -> bool:
    narration = str(getattr(scene, "narration", "") or "").strip()
    return bool(narration and _TRANSITION_CUE.search(narration))


def _transition_intent_words(scene, destination_query: str, *, limit: int = 4) -> list[str]:
    """Return a few current-scene words that sharpen a destination search."""
    narration = str(getattr(scene, "narration", "") or "").strip()
    destination_words = _query_words(destination_query)
    result: list[str] = []
    seen: set[str] = set()
    for token in re.findall(r"[A-Za-z0-9][A-Za-z0-9'’-]*", narration):
        key = token.casefold()
        if len(key) < 3 or key in _NARRATION_STOP_WORDS or key in destination_words or key in seen:
            continue
        result.append(token)
        seen.add(key)
        if len(result) >= limit:
            break
    return result


def _destination_transition_query(scene, destination_query: str) -> str:
    """Aim a pivot at its destination while retaining useful current-scene intent."""
    destination = clean_visual_query(destination_query)
    if not destination:
        return ""
    intent = _transition_intent_words(scene, destination)
    return " ".join([destination, *intent]).strip()


def _repair_stale_transition_queries(queries: list[str], scenes) -> list[str]:
    """Move a stale repeated search toward the destination of a narration pivot.

    Imported projects sometimes repeat the previous scene's stock search on a
    contrast line such as "but..." even though the narration is deliberately
    moving to a new subject. Only repair that narrow case: the current query must
    be substantially closer to the previous query than to the next one. The
    repaired query starts with the next scene's visual subject and adds a few
    concrete words from the current narration, making the transition intent
    explicit before stock search and visual verification begin.
    """
    repaired = list(queries)
    last_scene_index = min(len(scenes), len(repaired)) - 1
    for index in range(1, last_scene_index):
        scene = scenes[index]
        if not _is_transition_scene(scene):
            continue

        previous_query = repaired[index - 1]
        current_query = repaired[index]
        next_query = repaired[index + 1]
        previous_similarity = _query_similarity(current_query, previous_query)
        next_similarity = _query_similarity(current_query, next_query)

        # Exact repeats are the clearest stale-scene signal. Near-duplicates are
        # also repaired when they retain at least half of the meaningful terms
        # from the previous search and are less aligned with the destination.
        exact_repeat = current_query.casefold() == previous_query.casefold()
        stale_repeat = previous_similarity >= 0.5 and previous_similarity > next_similarity
        if (exact_repeat or stale_repeat) and next_query:
            repaired[index] = _destination_transition_query(scene, next_query) or next_query
    return repaired


def plan_visual_queries(script: str, raw_queries: str | Iterable[str], *, topic: str = "") -> VisualQueryPlan:
    """Return one positionally aligned query per scene, adding fallbacks as needed.

    Query positions are significant. Repeated searches are intentionally kept:
    removing a duplicate would shift every later search onto the wrong narration
    scene. Contrast scenes may repair a stale repeat toward the following scene's
    visual direction, but otherwise supplied searches remain untouched.
    """
    scenes = SceneBuilder().build(str(script or ""), name=str(topic or "Visual Plan")).scenes
    lines = str(raw_queries or "").splitlines() if isinstance(raw_queries, str) else list(raw_queries or ())
    cleaned = [clean_visual_query(str(line)) for line in lines]

    scene_count = max(1, len(scenes))
    target = max(scene_count, len(cleaned))
    queries: list[str] = []
    fallbacks = 0

    for index in range(target):
        query = cleaned[index] if index < len(cleaned) else ""
        if not query:
            scene = scenes[index] if index < len(scenes) else None
            query = clean_visual_query(_scene_fallback(topic, scene, index + 1))
            if not query:
                query = f"{topic} scene {index + 1}".strip() or f"factual video scene {index + 1}"
            fallbacks += 1
        queries.append(query)

    queries = _repair_stale_transition_queries(queries, scenes)
    return VisualQueryPlan(scene_count=scene_count, queries=tuple(queries), generated_fallbacks=fallbacks)


__all__ = ["VisualQueryPlan", "clean_visual_query", "plan_visual_queries"]
