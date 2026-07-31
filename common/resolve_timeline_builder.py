"""Execute Resolve export plans against the DaVinci Resolve scripting API."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any


class ResolveTimelineBuildError(RuntimeError):
    """Raised when a Resolve project or timeline cannot be built safely."""


@dataclass(frozen=True)
class ResolveTimelineBuildResult:
    project_name: str
    timeline_name: str
    imported_media: int
    placed_clips: int
    markers: int
    warnings: tuple[str, ...]


class ResolveTimelineBuilder:
    """Create a Resolve project, import media, and place an export plan."""

    MEDIA_KINDS = {"image", "video", "audio"}

    def __init__(self, resolve: Any) -> None:
        if resolve is None:
            raise TypeError("resolve must not be None")
        self.resolve = resolve
        self.warnings: list[str] = []

    @staticmethod
    def _frames(seconds: float, fps: float) -> int:
        return max(0, int(round(float(seconds) * float(fps))))

    def _project(self, project_name: str):
        manager = self.resolve.GetProjectManager()
        if manager is None:
            raise ResolveTimelineBuildError("Resolve project manager is unavailable")
        project = manager.GetCurrentProject()
        if project is None or project.GetName() != project_name:
            project = manager.CreateProject(project_name)
        if project is None:
            raise ResolveTimelineBuildError(f"could not create or open Resolve project: {project_name}")
        return project

    def _apply_settings(self, project, plan: dict[str, Any]) -> None:
        width, height = plan.get("resolution", [1080, 1920])
        fps = plan.get("frame_rate", 30)
        settings = {
            "timelineResolutionWidth": str(int(width)),
            "timelineResolutionHeight": str(int(height)),
            "timelineFrameRate": str(fps),
        }
        for key, value in settings.items():
            if project.SetSetting(key, value) is False:
                self.warnings.append(f"Resolve rejected project setting {key}={value}")

    def _all_clips(self, plan: dict[str, Any]):
        for track in plan.get("tracks", []):
            for clip in track.get("clips", []):
                yield track, clip

    def _import_media(self, media_pool, plan: dict[str, Any]) -> dict[str, Any]:
        sources = []
        seen = set()
        for _track, clip in self._all_clips(plan):
            if clip.get("kind") not in self.MEDIA_KINDS:
                continue
            source = str(clip.get("source") or "")
            if source and source not in seen:
                seen.add(source)
                sources.append(source)
        if not sources:
            return {}
        imported = media_pool.ImportMedia(sources)
        if imported is None:
            imported = []
        lookup = {}
        for source, item in zip(sources, imported):
            lookup[str(Path(source).resolve())] = item
            lookup[source] = item
        if len(imported) < len(sources):
            self.warnings.append(
                f"Resolve imported {len(imported)} of {len(sources)} referenced media files"
            )
        return lookup

    def _timeline(self, media_pool, name: str):
        timeline = media_pool.CreateEmptyTimeline(name)
        if timeline is None:
            project = self.resolve.GetProjectManager().GetCurrentProject()
            timeline = project.GetCurrentTimeline() if project is not None else None
        if timeline is None:
            raise ResolveTimelineBuildError(f"could not create Resolve timeline: {name}")
        return timeline

    def _place_media_clip(self, media_pool, item, track, clip, fps: float) -> bool:
        start = self._frames(clip.get("start", 0), fps)
        duration = max(1, self._frames(clip.get("duration", 0), fps))
        source_in = self._frames(clip.get("source_in", 0), fps)
        media_type = 2 if track.get("kind") == "audio" else 1
        payload = {
            "mediaPoolItem": item,
            "startFrame": source_in,
            "endFrame": source_in + duration - 1,
            "recordFrame": start,
            "mediaType": media_type,
            "trackIndex": int(track.get("index", 1)),
        }
        result = media_pool.AppendToTimeline([payload])
        return bool(result)

    def _add_marker(self, timeline, clip, fps: float) -> bool:
        frame = self._frames(clip.get("start", 0), fps)
        duration = max(1, self._frames(clip.get("duration", 0), fps))
        metadata = clip.get("metadata") or {}
        name = str(clip.get("name") or metadata.get("subtitle_text") or clip.get("kind", "Marker"))
        note = str(metadata.get("text") or metadata.get("subtitle_text") or clip.get("source") or "")
        return bool(timeline.AddMarker(frame, "Blue", name, note, duration, str(clip.get("id", ""))))

    def build(self, plan: dict[str, Any], *, project_name: str | None = None) -> ResolveTimelineBuildResult:
        if not isinstance(plan, dict):
            raise TypeError("plan must be a dictionary")
        name = str(project_name or plan.get("name") or "Fact Vault Video")
        fps = float(plan.get("frame_rate", 30))
        if fps <= 0:
            raise ResolveTimelineBuildError("frame_rate must be greater than zero")

        project = self._project(name)
        self._apply_settings(project, plan)
        media_pool = project.GetMediaPool()
        if media_pool is None:
            raise ResolveTimelineBuildError("Resolve media pool is unavailable")
        imported = self._import_media(media_pool, plan)
        timeline_name = str(plan.get("name") or name)
        timeline = self._timeline(media_pool, timeline_name)

        placed = 0
        markers = 0
        for track, clip in self._all_clips(plan):
            kind = clip.get("kind")
            if kind in {"marker", "subtitle"}:
                if self._add_marker(timeline, clip, fps):
                    markers += 1
                else:
                    self.warnings.append(f"could not add {kind} marker for clip {clip.get('id', '')}")
                continue
            source = str(clip.get("source") or "")
            item = imported.get(source) or imported.get(str(Path(source).resolve()))
            if item is None:
                self.warnings.append(f"media was not imported for clip {clip.get('id', '')}: {source}")
                continue
            if self._place_media_clip(media_pool, item, track, clip, fps):
                placed += 1
            else:
                self.warnings.append(f"Resolve could not place clip {clip.get('id', '')}")
            transition = clip.get("transition_in") or clip.get("transition_out")
            if transition:
                self.warnings.append(
                    f"transition {transition.get('name', 'unknown')} for clip {clip.get('id', '')} requires Resolve-side finishing"
                )

        project.SetCurrentTimeline(timeline)
        return ResolveTimelineBuildResult(
            project_name=name,
            timeline_name=timeline_name,
            imported_media=len(imported) // 2 if imported else 0,
            placed_clips=placed,
            markers=markers,
            warnings=tuple(self.warnings),
        )


def build_resolve_timeline(resolve: Any, plan: dict[str, Any], *, project_name: str | None = None):
    """Convenience entry point used by generated Resolve packages."""
    return ResolveTimelineBuilder(resolve).build(plan, project_name=project_name)


__all__ = [
    "ResolveTimelineBuildError",
    "ResolveTimelineBuildResult",
    "ResolveTimelineBuilder",
    "build_resolve_timeline",
]
