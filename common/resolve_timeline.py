from pathlib import Path


def seconds_to_frames(seconds, fps):
    """Convert seconds to a non-negative whole-frame count."""
    try:
        seconds_value = float(seconds or 0)
        fps_value = int(fps)
    except (TypeError, ValueError) as error:
        raise ValueError("Seconds and fps must be numeric.") from error

    if fps_value <= 0:
        raise ValueError("FPS must be greater than zero.")

    return max(0, round(seconds_value * fps_value))


def choose_scene_media(scene, manifest, scene_index=0):
    """Return an explicit scene asset or a deterministic visual fallback."""
    explicit = str(scene.get("media_path", "") or "").strip()
    if explicit:
        return explicit

    visuals = [
        item["path"]
        for group in ("videos", "images")
        for item in manifest.get(group, [])
        if item.get("path")
    ]
    if not visuals:
        return ""

    return visuals[scene_index % len(visuals)]


def build_timeline_plan(scene_plan, manifest, timeline_settings):
    """Create an editor-neutral, frame-accurate Resolve placement plan."""
    fps = int(timeline_settings.get("fps", scene_plan.get("fps", 30)))
    if fps <= 0:
        raise ValueError("FPS must be greater than zero.")

    placements = []
    cursor = 0
    for index, scene in enumerate(scene_plan.get("scenes", [])):
        start_value = scene.get("start")
        start_frame = (
            seconds_to_frames(start_value, fps)
            if start_value not in (None, "")
            else cursor
        )
        duration_frames = seconds_to_frames(scene.get("duration", 0), fps)
        if duration_frames <= 0:
            continue

        media_path = choose_scene_media(scene, manifest, index)
        placements.append(
            {
                "index": int(scene.get("index", index + 1)),
                "media_path": media_path,
                "start_frame": start_frame,
                "duration_frames": duration_frames,
                "end_frame": start_frame + duration_frames,
                "caption": str(scene.get("caption", "") or ""),
                "transition": str(scene.get("transition", "none") or "none"),
                "motion": str(scene.get("motion", "none") or "none"),
            }
        )
        cursor = max(cursor, start_frame + duration_frames)

    audio = manifest.get("audio", [])
    narration_path = ""
    if audio:
        preferred = next(
            (
                item for item in audio
                if "narration" in Path(item.get("path", "")).stem.lower()
                or "voice" in Path(item.get("path", "")).parts
            ),
            audio[0],
        )
        narration_path = preferred.get("path", "")

    return {
        "project_name": timeline_settings.get("project_name", scene_plan.get("project", "Fact Vault Video")),
        "width": int(timeline_settings.get("width", 1080)),
        "height": int(timeline_settings.get("height", 1920)),
        "fps": fps,
        "duration_frames": max((item["end_frame"] for item in placements), default=0),
        "placements": placements,
        "narration_path": narration_path,
    }


__all__ = ["build_timeline_plan", "choose_scene_media", "seconds_to_frames"]
