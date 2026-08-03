"""Helpers that turn acquired production outputs into an editable timeline."""
from __future__ import annotations

from dataclasses import asdict, is_dataclass
from enum import Enum
from pathlib import Path
from typing import Any, Iterable, Mapping

from timeline import Clip, ClipKind, Timeline, Track, TrackKind


def json_safe(value: Any) -> Any:
    """Convert checkpoint values into JSON-safe data without losing file paths."""
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    if isinstance(value, Path):
        return str(value)
    if isinstance(value, Enum):
        return value.value
    if is_dataclass(value):
        return json_safe(asdict(value))
    if isinstance(value, Mapping):
        return {str(key): json_safe(item) for key, item in value.items()}
    if isinstance(value, (list, tuple, set)):
        return [json_safe(item) for item in value]
    if hasattr(value, "to_dict") and callable(value.to_dict):
        return json_safe(value.to_dict())
    return str(value)


def _asset_value(asset: Any, name: str, default: Any = None) -> Any:
    if isinstance(asset, Mapping):
        if name in asset:
            return asset[name]
        candidate = asset.get("candidate")
        if isinstance(candidate, Mapping):
            return candidate.get(name, default)
        return default
    if hasattr(asset, name):
        return getattr(asset, name)
    candidate = getattr(asset, "candidate", None)
    return getattr(candidate, name, default) if candidate is not None else default


def _asset_path(asset: Any) -> str:
    value = _asset_value(asset, "path", "")
    return str(value or "")


def _absolute_media_path(value: Any, project_folder: str | Path | None = None) -> str:
    """Return an absolute media path suitable for Resolve validation/import."""
    text = str(value or "").strip()
    if not text:
        return ""
    path = Path(text).expanduser()
    if path.is_absolute():
        return str(path.resolve())

    # Acquired assets currently contain a project-root-relative path such as
    # ``In Progress/Project/Assets/...``. Resolve validation is performed from
    # another working directory, so preserving that relative path makes a real
    # file appear missing. Resolve it against the current application folder
    # first; support paths relative to the project folder as a fallback.
    cwd_candidate = path.resolve()
    if cwd_candidate.exists() or project_folder is None:
        return str(cwd_candidate)

    folder_candidate = (Path(project_folder).expanduser().resolve() / path).resolve()
    return str(folder_candidate)


def assemble_timeline(
    timeline: Timeline,
    assets: Iterable[Any] | None,
    voice: Any = None,
    *,
    project_folder: str | Path | None = None,
) -> Timeline:
    """Attach one acquired visual per scene and one narration clip to a timeline."""
    items = [asset for asset in (assets or []) if _asset_path(asset)]
    if items and timeline.scenes:
        visual_track = timeline.get_track("Visuals")
        if visual_track is None:
            visual_track = timeline.add_track(Track(kind=TrackKind.VIDEO, name="Visuals"))
        for index, scene in enumerate(timeline.scenes):
            asset = items[index % len(items)]
            kind = str(_asset_value(asset, "kind", "image"))
            source = _absolute_media_path(_asset_path(asset), project_folder)
            clip = visual_track.add_clip(
                Clip(
                    kind=ClipKind.VIDEO if kind == "video" else ClipKind.IMAGE,
                    start=scene.start,
                    duration=scene.duration,
                    source=source,
                    name=str(_asset_value(asset, "title", "") or scene.title),
                    metadata={
                        "provider": str(_asset_value(asset, "provider", "")),
                        "credit": str(_asset_value(asset, "credit", "")),
                        "license": str(_asset_value(asset, "license", "")),
                    },
                )
            )
            if clip.id not in scene.clip_ids:
                scene.clip_ids.append(clip.id)

    voice_path = _absolute_media_path(voice, project_folder)
    if voice_path and timeline.duration > 0:
        narration_track = timeline.get_track("Narration")
        if narration_track is None:
            narration_track = timeline.add_track(Track(kind=TrackKind.AUDIO, name="Narration"))
        if not any(clip.source == voice_path for clip in narration_track.clips):
            narration_track.add_clip(
                Clip(
                    kind=ClipKind.AUDIO,
                    start=0.0,
                    duration=timeline.duration,
                    source=voice_path,
                    name="Narration",
                )
            )

    timeline.metadata["production_assets"] = len(items)
    timeline.metadata["narration_attached"] = bool(voice_path)
    return timeline


__all__ = ["assemble_timeline", "json_safe"]
