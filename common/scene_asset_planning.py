"""Prepare one useful, distinct visual search query for every script scene."""
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


def plan_visual_queries(script: str, raw_queries: str | Iterable[str], *, topic: str = "") -> VisualQueryPlan:
    """Return exactly one non-empty query per scene, using scene text as fallback."""
    scenes = SceneBuilder().build(str(script or ""), name=str(topic or "Visual Plan")).scenes
    target = max(1, len(scenes))
    lines = str(raw_queries or "").splitlines() if isinstance(raw_queries, str) else list(raw_queries or ())

    queries: list[str] = []
    seen: set[str] = set()
    for line in lines:
        query = clean_visual_query(str(line))
        key = query.casefold()
        if query and key not in seen:
            queries.append(query)
            seen.add(key)
        if len(queries) >= target:
            break

    fallbacks = 0
    while len(queries) < target:
        index = len(queries)
        scene = scenes[index] if index < len(scenes) else None
        query = clean_visual_query(_scene_fallback(topic, scene, index + 1))
        base = query or f"{topic} scene {index + 1}".strip()
        candidate = base
        suffix = 2
        while candidate.casefold() in seen:
            candidate = f"{base} scene {suffix}"
            suffix += 1
        queries.append(candidate)
        seen.add(candidate.casefold())
        fallbacks += 1

    return VisualQueryPlan(scene_count=target, queries=tuple(queries), generated_fallbacks=fallbacks)


__all__ = ["VisualQueryPlan", "clean_visual_query", "plan_visual_queries"]
