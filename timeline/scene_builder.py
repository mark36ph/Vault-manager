"""Build editable project timelines from narration scripts."""

from __future__ import annotations

import re
from pathlib import Path

from .models import Scene, Timeline, Track, TrackKind
from .storage import ProjectTimelineStore

_PARAGRAPH_BREAK = re.compile(r"\n\s*\n+")
_WHITESPACE = re.compile(r"\s+")
_WORD = re.compile(r"\b[\w'-]+\b", re.UNICODE)


class SceneBuilder:
    """Convert script paragraphs into sequential timeline scenes."""

    def __init__(
        self,
        *,
        words_per_minute: float = 150.0,
        minimum_scene_duration: float = 1.0,
        timing_precision: int = 3,
    ) -> None:
        if words_per_minute <= 0:
            raise ValueError("words_per_minute must be greater than zero")
        if minimum_scene_duration <= 0:
            raise ValueError("minimum_scene_duration must be greater than zero")
        if timing_precision < 0:
            raise ValueError("timing_precision cannot be negative")
        self.words_per_minute = float(words_per_minute)
        self.minimum_scene_duration = float(minimum_scene_duration)
        self.timing_precision = timing_precision

    def split_script(self, script: str) -> list[str]:
        """Return normalized non-empty paragraphs from a script."""
        if not isinstance(script, str):
            raise TypeError("script must be a string")
        normalized = script.replace("\r\n", "\n").replace("\r", "\n").strip()
        if not normalized:
            return []
        return [
            _WHITESPACE.sub(" ", paragraph).strip()
            for paragraph in _PARAGRAPH_BREAK.split(normalized)
            if paragraph.strip()
        ]

    def estimate_duration(self, narration: str) -> float:
        """Estimate spoken duration using word count and configured pace."""
        if not isinstance(narration, str):
            raise TypeError("narration must be a string")
        word_count = len(_WORD.findall(narration))
        duration = word_count * 60.0 / self.words_per_minute
        return round(max(self.minimum_scene_duration, duration), self.timing_precision)

    def build(
        self,
        script: str,
        *,
        name: str = "Fact video",
        frame_rate: float = 30.0,
        width: int = 1920,
        height: int = 1080,
    ) -> Timeline:
        """Build a timeline containing empty production tracks and timed scenes."""
        timeline = Timeline(
            name=name,
            frame_rate=frame_rate,
            width=width,
            height=height,
            tracks=[
                Track(name="Video 1", kind=TrackKind.VIDEO),
                Track(name="Narration", kind=TrackKind.AUDIO),
                Track(name="Subtitles", kind=TrackKind.SUBTITLE),
                Track(name="Markers", kind=TrackKind.MARKER),
            ],
            metadata={
                "generated_from": "script",
                "words_per_minute": self.words_per_minute,
            },
        )

        cursor = 0.0
        for index, narration in enumerate(self.split_script(script), start=1):
            duration = self.estimate_duration(narration)
            timeline.add_scene(
                Scene(
                    title=f"Scene {index}",
                    start=round(cursor, self.timing_precision),
                    duration=duration,
                    narration=narration,
                    metadata={
                        "scene_number": index,
                        "word_count": len(_WORD.findall(narration)),
                        "visuals": [],
                        "keywords": [],
                        "subtitle_text": narration,
                        "transition": "cut",
                        "notes": "",
                    },
                )
            )
            cursor = round(cursor + duration, self.timing_precision)

        return timeline

    def build_and_save(
        self,
        project_folder: str | Path,
        script: str,
        *,
        name: str = "Fact video",
        frame_rate: float = 30.0,
        width: int = 1920,
        height: int = 1080,
    ) -> Timeline:
        """Build a timeline and persist it as the project's timeline.json."""
        timeline = self.build(
            script,
            name=name,
            frame_rate=frame_rate,
            width=width,
            height=height,
        )
        ProjectTimelineStore(project_folder).save(timeline)
        return timeline


def build_project_timeline(
    project_folder: str | Path,
    script: str,
    *,
    name: str = "Fact video",
    **builder_options,
) -> Timeline:
    """Convenience entry point for project workflows."""
    return SceneBuilder(**builder_options).build_and_save(
        project_folder,
        script,
        name=name,
    )


__all__ = ["SceneBuilder", "build_project_timeline"]
