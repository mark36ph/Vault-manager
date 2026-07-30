from pathlib import Path

from common.resolve_timeline import build_timeline_plan


def _absolute(project_folder, relative_path):
    return str((Path(project_folder) / Path(relative_path)).resolve())


def build_resolve_timeline(resolve, project_folder, scene_plan, manifest, timeline_settings):
    """Create a Resolve project/timeline, import media, and place planned clips."""
    if resolve is None:
        raise RuntimeError("Could not connect to DaVinci Resolve.")

    plan = build_timeline_plan(scene_plan, manifest, timeline_settings)
    project_manager = resolve.GetProjectManager()
    project = project_manager.GetCurrentProject()
    if project is None or project.GetName() != plan["project_name"]:
        project = project_manager.LoadProject(plan["project_name"])
        if project is None:
            project = project_manager.CreateProject(plan["project_name"])
    if project is None:
        raise RuntimeError(f"Could not create or open Resolve project: {plan['project_name']}")

    project.SetSetting("timelineResolutionWidth", str(plan["width"]))
    project.SetSetting("timelineResolutionHeight", str(plan["height"]))
    project.SetSetting("timelineFrameRate", str(plan["fps"]))

    media_pool = project.GetMediaPool()
    media_paths = []
    for group in ("images", "videos", "audio"):
        for item in manifest.get(group, []):
            if item.get("path"):
                media_paths.append(_absolute(project_folder, item["path"]))

    imported = media_pool.ImportMedia(media_paths) if media_paths else []
    imported = imported or []
    by_path = {}
    for item in imported:
        try:
            clip_path = item.GetClipProperty("File Path")
        except Exception:
            clip_path = ""
        if clip_path:
            by_path[str(Path(clip_path).resolve()).lower()] = item

    timeline = media_pool.CreateEmptyTimeline(plan["project_name"])
    if timeline is None:
        timeline = project.GetCurrentTimeline()
    if timeline is None:
        raise RuntimeError("Resolve could not create a timeline.")

    appended = 0
    missing = []
    for placement in plan["placements"]:
        if not placement["media_path"]:
            missing.append(f"Scene {placement['index']} has no visual media.")
            continue

        absolute = _absolute(project_folder, placement["media_path"])
        media_item = by_path.get(str(Path(absolute).resolve()).lower())
        if media_item is None:
            missing.append(f"Media was not imported: {placement['media_path']}")
            continue

        clip_info = {
            "mediaPoolItem": media_item,
            "startFrame": 0,
            "endFrame": max(0, placement["duration_frames"] - 1),
            "recordFrame": placement["start_frame"],
            "trackIndex": 1,
            "mediaType": 1,
        }
        if media_pool.AppendToTimeline([clip_info]):
            appended += 1

    narration_added = False
    if plan["narration_path"]:
        absolute = _absolute(project_folder, plan["narration_path"])
        narration = by_path.get(str(Path(absolute).resolve()).lower())
        if narration is not None:
            narration_added = bool(
                media_pool.AppendToTimeline(
                    [{
                        "mediaPoolItem": narration,
                        "startFrame": 0,
                        "endFrame": max(0, plan["duration_frames"] - 1),
                        "recordFrame": 0,
                        "trackIndex": 1,
                        "mediaType": 2,
                    }]
                )
            )
        else:
            missing.append(f"Narration was not imported: {plan['narration_path']}")

    return {
        "project_name": plan["project_name"],
        "timeline": timeline,
        "visual_clips_added": appended,
        "narration_added": narration_added,
        "warnings": tuple(missing),
        "plan": plan,
    }


__all__ = ["build_resolve_timeline"]
