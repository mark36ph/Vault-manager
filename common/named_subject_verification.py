"""Stricter visual verification for concrete named subjects.

This wrapper keeps the existing topic-neutral verifier while preserving named
entities as complete semantic subjects. It rejects clear identity contradictions,
but does not require stock footage to prove a landmark or place from pixels alone.
Ambiguous but plausible imagery remains marked uncertain so the acquisition layer
can keep searching and use it only as a fallback.
"""
from __future__ import annotations

import re
from typing import Any


_BROAD_ANCHORS = {
    "space", "science", "nature", "history", "technology", "engineering",
    "health", "medicine", "animals", "animal", "ocean", "geography",
    "physics", "chemistry", "biology", "astronomy", "earth", "environment",
    "transport", "architecture", "geology",
}

_GENERIC_CAPITALIZED = {
    "Aerial", "Close", "Documentary", "Realistic", "Scientific", "Vertical",
    "Wide", "Landscape", "Photo", "Photography", "Video", "Image",
}

_TWO_WORD_PREFIXES = {
    "Mount", "Mt", "Mauna", "Lake", "Cape", "Fort", "Saint", "St",
}

_ENTITY_TERMINALS = {
    "Bridge", "Reef", "Telescope", "Station", "Tower", "Building", "Palace",
    "Temple", "Cathedral", "Church", "Mosque", "River", "Sea", "Ocean",
    "Lake", "Island", "Falls", "Dam", "Park", "City", "University", "Museum",
    "Airport", "Volcano", "Mountain", "Peak", "Monument", "Castle", "Canal",
    "Desert", "Forest", "Bay", "Gulf", "Peninsula", "Spacecraft", "Rover",
    "Tomb", "Wall", "Capitol", "Center", "Centre",
}


def _trim_named_run(tokens: list[str]) -> list[str]:
    if not tokens:
        return []

    if tokens[0] in _TWO_WORD_PREFIXES and len(tokens) >= 2:
        return tokens[:2]

    for index, token in enumerate(tokens):
        if token in _ENTITY_TERMINALS:
            return tokens[: index + 1]

    # Consecutive proper nouns often include a following region/country in stock
    # queries (for example "Mauna Loa Hawaii"). Without an explicit terminal,
    # two words is the safest general-purpose entity boundary.
    return tokens[:2]


def named_subject_phrase(query: str) -> str:
    """Return the strongest proper-named phrase visible in a stock search query.

    Imported searches preserve proper-name capitalization, which lets us retain
    multiword entities such as ``Mauna Loa``, ``Mount Everest``, ``Great Barrier
    Reef``, and ``James Webb Space Telescope`` as one semantic subject instead of
    reducing them to a single keyword.
    """
    tokens = re.findall(r"[A-Za-z0-9][A-Za-z0-9'’-]*", str(query or ""))
    if not tokens:
        return ""

    if tokens[0].casefold() in _BROAD_ANCHORS:
        tokens = tokens[1:]

    runs: list[list[str]] = []
    current: list[str] = []
    for token in tokens:
        is_named = (
            len(token) > 1
            and (token[0].isupper() or token.isupper())
            and token not in _GENERIC_CAPITALIZED
        )
        if is_named:
            current.append(token)
        elif current:
            runs.append(current)
            current = []
    if current:
        runs.append(current)

    if not runs:
        return ""

    candidates = [_trim_named_run(run) for run in runs]
    best = max(candidates, key=lambda run: (len(run), len(" ".join(run))))
    return " ".join(best).strip()


class NamedSubjectVerifier:
    """Proxy an existing visual verifier with balanced named-identity semantics."""

    def __init__(self, base_verifier: Any) -> None:
        self.base_verifier = base_verifier

    def __getattr__(self, name: str) -> Any:
        return getattr(self.base_verifier, name)

    def __call__(self, query: str, asset: Any) -> bool:
        entity = named_subject_phrase(query)
        check_query = str(query or "").strip()

        if entity:
            check_query += (
                "\n\nNAMED-SUBJECT IDENTITY REQUIREMENT: "
                f"The requested named subject is '{entity}'. Judge it as that complete "
                "entity rather than as separate matching keywords. Reject clear evidence "
                "of a different named subject or a different semantic meaning. However, "
                "do not demand impossible pixel-only proof for visually similar places, "
                "landforms, buildings, species, machines, or celestial bodies. Stock "
                "metadata may corroborate a visually plausible match, but it must never "
                "override a visible contradiction. If the image is plausible for the "
                "requested entity but cannot be uniquely identified from pixels, keep it "
                "and mark subject uncertainty rather than calling it wrong_named_subject. "
                "The acquisition system will prefer a more certain candidate when one is "
                "available. Apply this principle to all named people, places, landmarks, "
                "species, machines, vehicles, products, celestial bodies, organizations, "
                "and other concrete named entities."
            )

        return bool(self.base_verifier(check_query, asset))


__all__ = ["NamedSubjectVerifier", "named_subject_phrase"]
