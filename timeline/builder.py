"""Convenience builder for assembling timelines without UI dependencies."""

from __future__ import annotations

from .models import Clip, ClipKind, Scene, Timeline, Track, TrackKind


class TimelineBuilder:
    """Build a timeline incrementally while keeping common tracks reusable."""

    def __init__(
        self,
        name: str,
        *,
        frame_rate: float = 30.0,
        width: int = 1920,
        height: int = 1080,
    ) -> None:
        self.timeline = Timeline(
            name=name,
            frame_rate=frame_rate,
            width=width,
            height=height,
        )

    def track(self, name: str, kind: TrackKind) -> Track:
        existing = self.timeline.get_track(name)
        if existing is not None:
            if existing.kind != TrackKind(kind):
                raise ValueError(f"track {name!r} already exists with kind {existing.kind.value!r}")
            return existing
        return self.timeline.add_track(Track(name=name, kind=kind))

    def add_clip(
        self,
        *,
        track_name: str,
        track_kind: TrackKind,
        clip_kind: ClipKind,
        start: float,
        duration: float,
        source: str | None = None,
        name: str = "",
        source_in: float = 0.0,
        metadata: dict | None = None,
    ) -> Clip:
        clip = Clip(
            kind=clip_kind,
            start=start,
            duration=duration,
            source=source,
            name=name,
            source_in=source_in,
            metadata=dict(metadata or {}),
        )
        self.track(track_name, track_kind).add_clip(clip)
        return clip

    def add_scene(
        self,
        *,
        title: str,
        start: float,
        duration: float,
        narration: str = "",
        clip_ids: list[str] | None = None,
        metadata: dict | None = None,
    ) -> Scene:
        return self.timeline.add_scene(
            Scene(
                title=title,
                start=start,
                duration=duration,
                narration=narration,
                clip_ids=list(clip_ids or []),
                metadata=dict(metadata or {}),
            )
        )

    def build(self) -> Timeline:
        return self.timeline
