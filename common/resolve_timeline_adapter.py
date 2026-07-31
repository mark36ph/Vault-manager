"""Translate the internal edit timeline into a Resolve-ready export plan."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from timeline import ClipKind, Timeline, TrackKind


class ResolveTimelineAdapterError(ValueError):
    """Raised when an internal timeline cannot be exported safely."""


class ResolveTimelineAdapter:
    """Convert editor-neutral timeline clips into Resolve export payloads."""

    SUPPORTED_CLIPS = {
        ClipKind.IMAGE,
        ClipKind.VIDEO,
        ClipKind.AUDIO,
        ClipKind.SUBTITLE,
        ClipKind.MARKER,
    }
    FILE_BACKED_CLIPS = {ClipKind.IMAGE, ClipKind.VIDEO, ClipKind.AUDIO}

    def __init__(self, timeline: Timeline, *, project_folder: str | Path | None = None) -> None:
        if not isinstance(timeline, Timeline):
            raise TypeError("timeline must be a Timeline")
        self.timeline = timeline
        self.project_folder = Path(project_folder) if project_folder is not None else None

    def _source_path(self, source: str | None) -> str:
        if not source:
            return ""
        path = Path(source)
        if path.is_absolute() or self.project_folder is None:
            return str(path)
        return str((self.project_folder / path).resolve())

    def validate(self) -> list[str]:
        issues: list[str] = []
        seen_ids: set[str] = set()
        for track in self.timeline.tracks:
            for clip in track.clips:
                if clip.id in seen_ids:
                    issues.append(f"duplicate clip id: {clip.id}")
                seen_ids.add(clip.id)
                if clip.kind not in self.SUPPORTED_CLIPS:
                    issues.append(f"unsupported clip kind: {clip.kind.value}")
                if clip.kind in self.FILE_BACKED_CLIPS and not clip.source:
                    issues.append(f"clip has no source: {clip.id}")
                if (
                    clip.kind in self.FILE_BACKED_CLIPS
                    and clip.source
                    and self.project_folder is not None
                ):
                    candidate = Path(clip.source)
                    if not candidate.is_absolute():
                        candidate = self.project_folder / candidate
                    if not candidate.exists():
                        issues.append(f"clip source does not exist: {clip.source}")
                if track.kind is TrackKind.VIDEO and clip.kind not in {ClipKind.IMAGE, ClipKind.VIDEO}:
                    issues.append(f"clip {clip.id} is incompatible with video track {track.name}")
                if track.kind is TrackKind.AUDIO and clip.kind is not ClipKind.AUDIO:
                    issues.append(f"clip {clip.id} is incompatible with audio track {track.name}")
                if track.kind is TrackKind.SUBTITLE and clip.kind is not ClipKind.SUBTITLE:
                    issues.append(f"clip {clip.id} is incompatible with subtitle track {track.name}")
                if track.kind is TrackKind.MARKER and clip.kind is not ClipKind.MARKER:
                    issues.append(f"clip {clip.id} is incompatible with marker track {track.name}")
        return issues

    def _clip_payload(self, clip) -> dict[str, Any]:
        return {
            "id": clip.id,
            "name": clip.name,
            "kind": clip.kind.value,
            "source": self._source_path(clip.source),
            "start": clip.start,
            "duration": clip.duration,
            "end": clip.end,
            "source_in": clip.source_in,
            "transition_in": (
                {"name": clip.transition_in.name, "duration": clip.transition_in.duration}
                if clip.transition_in
                else None
            ),
            "transition_out": (
                {"name": clip.transition_out.name, "duration": clip.transition_out.duration}
                if clip.transition_out
                else None
            ),
            "metadata": dict(clip.metadata),
        }

    def build_plan(self, *, strict: bool = True) -> dict[str, Any]:
        issues = self.validate()
        if strict and issues:
            raise ResolveTimelineAdapterError("; ".join(issues))

        tracks = []
        for index, track in enumerate(self.timeline.tracks, start=1):
            tracks.append(
                {
                    "id": track.id,
                    "index": index,
                    "name": track.name,
                    "kind": track.kind.value,
                    "clips": [self._clip_payload(clip) for clip in track.clips],
                }
            )

        scenes = [
            {
                "id": scene.id,
                "title": scene.title,
                "start": scene.start,
                "duration": scene.duration,
                "end": scene.end,
                "narration": scene.narration,
                "clip_ids": list(scene.clip_ids),
                "metadata": dict(scene.metadata),
            }
            for scene in self.timeline.scenes
        ]

        return {
            "timeline_id": self.timeline.id,
            "name": self.timeline.name,
            "frame_rate": self.timeline.frame_rate,
            "resolution": [self.timeline.width, self.timeline.height],
            "duration": self.timeline.duration,
            "tracks": tracks,
            "scenes": scenes,
            "warnings": issues,
            "metadata": dict(self.timeline.metadata),
        }


def build_resolve_timeline_plan(
    timeline: Timeline,
    *,
    project_folder: str | Path | None = None,
    strict: bool = True,
) -> dict[str, Any]:
    """Convenience entry point for Resolve package exporters."""
    return ResolveTimelineAdapter(timeline, project_folder=project_folder).build_plan(strict=strict)


__all__ = [
    "ResolveTimelineAdapter",
    "ResolveTimelineAdapterError",
    "build_resolve_timeline_plan",
]
