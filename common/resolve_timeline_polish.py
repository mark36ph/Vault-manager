from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Iterable


_TIMESTAMP = re.compile(
    r"(?P<hours>\d{2}):(?P<minutes>\d{2}):(?P<seconds>\d{2})[,.](?P<millis>\d{3})"
)


@dataclass(frozen=True)
class SubtitleCue:
    start_frame: int
    duration_frames: int
    text: str


@dataclass(frozen=True)
class TimelineMarker:
    frame: int
    name: str
    note: str
    color: str = "Blue"


def seconds_to_frames(seconds: float, fps: int) -> int:
    if fps <= 0:
        raise ValueError("Frame rate must be greater than zero.")
    return max(0, round(float(seconds) * fps))


def _timestamp_seconds(value: str) -> float:
    match = _TIMESTAMP.fullmatch(value.strip())
    if not match:
        raise ValueError(f"Invalid subtitle timestamp: {value}")
    parts = {key: int(number) for key, number in match.groupdict().items()}
    return (
        parts["hours"] * 3600
        + parts["minutes"] * 60
        + parts["seconds"]
        + parts["millis"] / 1000
    )


def parse_srt_cues(subtitle_text: str, fps: int) -> list[SubtitleCue]:
    text = (subtitle_text or "").replace("\r\n", "\n").strip()
    if not text:
        return []

    cues: list[SubtitleCue] = []
    for block in re.split(r"\n\s*\n", text):
        lines = [line.strip() for line in block.splitlines() if line.strip()]
        if not lines:
            continue
        if lines[0].isdigit():
            lines = lines[1:]
        if len(lines) < 2 or "-->" not in lines[0]:
            continue

        start_text, end_text = [part.strip() for part in lines[0].split("-->", 1)]
        start_frame = seconds_to_frames(_timestamp_seconds(start_text), fps)
        end_frame = seconds_to_frames(_timestamp_seconds(end_text), fps)
        cue_text = "\n".join(lines[1:]).strip()
        if cue_text and end_frame > start_frame:
            cues.append(
                SubtitleCue(
                    start_frame=start_frame,
                    duration_frames=end_frame - start_frame,
                    text=cue_text,
                )
            )
    return cues


def build_scene_markers(scenes: Iterable[dict], fps: int) -> list[TimelineMarker]:
    markers: list[TimelineMarker] = []
    for position, scene in enumerate(scenes, start=1):
        index = int(scene.get("index") or position)
        start = seconds_to_frames(scene.get("start", 0), fps)
        caption = str(scene.get("caption") or "").strip()
        visual = str(scene.get("visual_plan") or "").strip()
        note = caption or visual or f"Scene {index}"
        markers.append(TimelineMarker(frame=start, name=f"Scene {index}", note=note))
    return markers


def build_motion_plan(scene: dict, media_path: str) -> dict:
    suffix = media_path.lower().rsplit(".", 1)[-1] if "." in media_path else ""
    is_still = suffix in {"jpg", "jpeg", "png", "webp", "bmp", "tif", "tiff"}
    requested = str(scene.get("motion") or "").strip().lower()

    if requested and requested != "none":
        style = requested
    elif is_still:
        style = "slow_zoom_in"
    else:
        style = "none"

    return {
        "style": style,
        "start_zoom": 1.0,
        "end_zoom": 1.08 if style == "slow_zoom_in" else 1.0,
        "ease": "smooth",
    }


def build_transition_plan(scenes: list[dict], fps: int, default_seconds: float = 0.25) -> list[dict]:
    duration = max(1, seconds_to_frames(default_seconds, fps))
    transitions: list[dict] = []
    for index in range(1, len(scenes)):
        scene = scenes[index]
        requested = str(scene.get("transition") or "").strip().lower()
        transition_type = requested if requested and requested != "none" else "cross_dissolve"
        transitions.append(
            {
                "before_scene": int(scene.get("index") or index + 1),
                "type": transition_type,
                "duration_frames": duration,
            }
        )
    return transitions


def build_polish_plan(scene_plan: dict, subtitle_text: str = "") -> dict:
    fps = int(scene_plan.get("fps") or 30)
    scenes = list(scene_plan.get("scenes") or [])

    polished_scenes = []
    for position, scene in enumerate(scenes):
        media_path = str(scene.get("media_path") or "")
        polished_scenes.append(
            {
                "index": int(scene.get("index") or position + 1),
                "motion": build_motion_plan(scene, media_path),
            }
        )

    return {
        "fps": fps,
        "markers": [marker.__dict__ for marker in build_scene_markers(scenes, fps)],
        "subtitles": [cue.__dict__ for cue in parse_srt_cues(subtitle_text, fps)],
        "transitions": build_transition_plan(scenes, fps),
        "scenes": polished_scenes,
        "track_layout": {
            "video": 1,
            "overlays": 2,
            "titles": 3,
            "narration": 1,
            "music": 2,
        },
    }


__all__ = [
    "SubtitleCue",
    "TimelineMarker",
    "build_motion_plan",
    "build_polish_plan",
    "build_scene_markers",
    "build_transition_plan",
    "parse_srt_cues",
    "seconds_to_frames",
]
