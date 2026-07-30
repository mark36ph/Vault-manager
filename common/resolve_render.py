import re
import time
from pathlib import Path


DEFAULT_RENDER_SETTINGS = {
    "format": "mp4",
    "codec": "H264",
    "width": 1080,
    "height": 1920,
    "fps": 30,
    "video_quality": "Best",
    "audio_codec": "aac",
    "audio_bit_depth": 16,
    "audio_sample_rate": 48000,
}


def safe_filename(value):
    """Return a filesystem-safe render name without changing readable words."""
    value = re.sub(r'[<>:"/\\|?*]+', "-", str(value or "render")).strip()
    value = re.sub(r"\s+", " ", value).rstrip(". ")
    return value or "render"


def build_render_plan(project_folder, project_name, settings=None):
    """Create normalized Resolve render settings and the expected output path."""
    settings = {**DEFAULT_RENDER_SETTINGS, **(settings or {})}
    output_dir = Path(settings.get("output_dir") or Path(project_folder) / "Renders").resolve()
    custom_name = safe_filename(settings.get("custom_name") or project_name)
    extension = str(settings.get("extension") or settings["format"]).lstrip(".")

    return {
        "output_dir": str(output_dir),
        "custom_name": custom_name,
        "output_path": str(output_dir / f"{custom_name}.{extension}"),
        "format": str(settings["format"]),
        "codec": str(settings["codec"]),
        "render_mode": int(settings.get("render_mode", 1)),
        "render_settings": {
            "SelectAllFrames": True,
            "TargetDir": str(output_dir),
            "CustomName": custom_name,
            "ExportVideo": bool(settings.get("export_video", True)),
            "ExportAudio": bool(settings.get("export_audio", True)),
            "FormatWidth": int(settings["width"]),
            "FormatHeight": int(settings["height"]),
            "FrameRate": float(settings["fps"]),
            "VideoQuality": settings["video_quality"],
            "AudioCodec": str(settings["audio_codec"]),
            "AudioBitDepth": int(settings["audio_bit_depth"]),
            "AudioSampleRate": int(settings["audio_sample_rate"]),
        },
    }


def render_resolve_project(
    resolve,
    project_folder,
    project_name,
    settings=None,
    wait=True,
    timeout=3600,
    poll_interval=1.0,
    sleeper=time.sleep,
):
    """Configure, queue, and optionally wait for a Resolve render job."""
    if resolve is None:
        raise RuntimeError("Could not connect to DaVinci Resolve.")

    manager = resolve.GetProjectManager()
    project = manager.GetCurrentProject()
    if project is None or project.GetName() != project_name:
        project = manager.LoadProject(project_name)
    if project is None:
        raise RuntimeError(f"Could not open Resolve project: {project_name}")
    if project.GetCurrentTimeline() is None:
        raise RuntimeError("Resolve project has no current timeline to render.")

    plan = build_render_plan(project_folder, project_name, settings)
    Path(plan["output_dir"]).mkdir(parents=True, exist_ok=True)

    if not project.SetCurrentRenderMode(plan["render_mode"]):
        raise RuntimeError("Resolve rejected the requested render mode.")
    if not project.SetCurrentRenderFormatAndCodec(plan["format"], plan["codec"]):
        raise RuntimeError(
            f"Resolve does not support render format/codec: {plan['format']}/{plan['codec']}"
        )
    if not project.SetRenderSettings(plan["render_settings"]):
        raise RuntimeError("Resolve rejected the render settings.")

    job_id = project.AddRenderJob()
    if not job_id:
        raise RuntimeError("Resolve could not add the render job.")
    if not project.StartRendering(job_id):
        raise RuntimeError("Resolve could not start the render job.")

    if not wait:
        return {
            "job_id": job_id,
            "status": "Rendering",
            "completion": 0,
            "output_path": plan["output_path"],
            "plan": plan,
        }

    started = time.monotonic()
    while project.IsRenderingInProgress():
        if timeout is not None and time.monotonic() - started > timeout:
            project.StopRendering()
            raise TimeoutError(f"Resolve render exceeded {timeout} seconds and was stopped.")
        sleeper(poll_interval)

    status = project.GetRenderJobStatus(job_id) or {}
    job_status = str(status.get("JobStatus") or status.get("status") or "Unknown")
    completion = status.get("CompletionPercentage", status.get("completion", 0))
    if job_status.lower() not in {"complete", "completed"}:
        error = status.get("Error") or status.get("error")
        detail = f": {error}" if error else ""
        raise RuntimeError(f"Resolve render did not complete successfully ({job_status}){detail}")

    return {
        "job_id": job_id,
        "status": job_status,
        "completion": completion,
        "output_path": plan["output_path"],
        "plan": plan,
    }


__all__ = ["build_render_plan", "render_resolve_project", "safe_filename"]
