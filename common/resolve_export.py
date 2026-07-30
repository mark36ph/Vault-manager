import json
from dataclasses import dataclass
from pathlib import Path


IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"}
VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".avi", ".m4v", ".webm"}
AUDIO_EXTENSIONS = {".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg"}


@dataclass(frozen=True)
class ResolveExportResult:
    export_folder: Path
    files: tuple[Path, ...]
    warnings: tuple[str, ...]


def _project_value(project, key, default=""):
    try:
        value = project[key]
    except (KeyError, IndexError, TypeError):
        value = default
    return default if value is None else value


def _relative_path(path, project_folder):
    return path.resolve().relative_to(project_folder.resolve()).as_posix()


def collect_media_manifest(project_folder):
    project_folder = Path(project_folder)
    manifest = {"images": [], "videos": [], "audio": []}

    if not project_folder.exists():
        return manifest

    for path in sorted(project_folder.rglob("*"), key=lambda item: str(item).lower()):
        if not path.is_file() or "Resolve" in path.parts:
            continue

        suffix = path.suffix.lower()
        item = {
            "path": _relative_path(path, project_folder),
            "name": path.name,
            "size_bytes": path.stat().st_size,
        }

        if suffix in IMAGE_EXTENSIONS:
            manifest["images"].append(item)
        elif suffix in VIDEO_EXTENSIONS:
            manifest["videos"].append(item)
        elif suffix in AUDIO_EXTENSIONS:
            manifest["audio"].append(item)

    return manifest


def build_scene_plan(project, settings):
    duration = _project_value(project, "narration_duration", 0)
    try:
        duration = float(duration or 0)
    except (TypeError, ValueError):
        duration = 0.0

    caption = str(_project_value(project, "on_screen_text", "")).strip()
    visual_plan = str(_project_value(project, "visual_plan", "")).strip()

    scenes = []
    if duration > 0 or caption or visual_plan:
        scenes.append(
            {
                "index": 1,
                "start": 0.0,
                "duration": duration,
                "caption": caption,
                "visual_plan": visual_plan,
                "media_path": "",
                "transition": "none",
                "motion": "none",
            }
        )

    return {
        "project": str(_project_value(project, "title", "Untitled Project")),
        "resolution": [
            int(settings.get("timeline_width", 1080)),
            int(settings.get("timeline_height", 1920)),
        ],
        "fps": int(settings.get("frame_rate", 30)),
        "scenes": scenes,
    }


def validate_resolve_export(project, manifest, scene_plan):
    warnings = []

    if not manifest["images"] and not manifest["videos"]:
        warnings.append("No image or video media was found in the project.")
    if not manifest["audio"]:
        warnings.append("No narration or other audio file was found in the project.")
    if not scene_plan["scenes"]:
        warnings.append("No scene timing or visual plan is available yet.")
    if not str(_project_value(project, "subtitle_text", "")).strip():
        warnings.append("No subtitle text is available.")

    return warnings


def _builder_script():
    return '''from pathlib import Path
import json
import sys

PACKAGE_FOLDER = Path(__file__).resolve().parent
PROJECT_FOLDER = PACKAGE_FOLDER.parent


def load_json(name):
    with (PACKAGE_FOLDER / name).open("r", encoding="utf-8") as handle:
        return json.load(handle)


def main():
    modules = PACKAGE_FOLDER / "Modules"
    if modules.exists() and str(modules) not in sys.path:
        sys.path.insert(0, str(modules))

    try:
        import DaVinciResolveScript as dvr_script
    except ImportError:
        raise SystemExit(
            "DaVinciResolveScript could not be imported. Configure the Resolve "
            "Scripting Modules path in Fact Vault Manager or run this script from Resolve."
        )

    resolve = dvr_script.scriptapp("Resolve")
    if resolve is None:
        raise SystemExit("Could not connect to DaVinci Resolve. Start Resolve and try again.")

    project_manager = resolve.GetProjectManager()
    timeline_settings = load_json("timeline_settings.json")
    manifest = load_json("media_manifest.json")

    project_name = timeline_settings["project_name"]
    resolve_project = project_manager.GetCurrentProject()
    if resolve_project is None or resolve_project.GetName() != project_name:
        resolve_project = project_manager.CreateProject(project_name)
    if resolve_project is None:
        raise SystemExit(f"Could not create or open Resolve project: {project_name}")

    resolve_project.SetSetting("timelineResolutionWidth", str(timeline_settings["width"]))
    resolve_project.SetSetting("timelineResolutionHeight", str(timeline_settings["height"]))
    resolve_project.SetSetting("timelineFrameRate", str(timeline_settings["fps"]))

    media_pool = resolve_project.GetMediaPool()
    media_paths = []
    for group in ("images", "videos", "audio"):
        for item in manifest[group]:
            media_paths.append(str(PROJECT_FOLDER / Path(item["path"])))

    if media_paths:
        media_pool.ImportMedia(media_paths)

    print(f"Resolve package imported for {project_name}.")
    print("Timeline clip placement will be added in PR #14.")


if __name__ == "__main__":
    main()
'''


def export_resolve_package(project, project_folder, settings):
    project_folder = Path(project_folder)
    if not project_folder.exists():
        raise FileNotFoundError(f"Project folder could not be found: {project_folder}")

    export_folder = project_folder / "Resolve"
    export_folder.mkdir(parents=True, exist_ok=True)

    manifest = collect_media_manifest(project_folder)
    scene_plan = build_scene_plan(project, settings)
    warnings = validate_resolve_export(project, manifest, scene_plan)

    timeline_settings = {
        "project_name": str(
            settings.get("default_project_name")
            or _project_value(project, "title", "Fact Vault Video")
        ),
        "width": int(settings.get("timeline_width", 1080)),
        "height": int(settings.get("timeline_height", 1920)),
        "fps": int(settings.get("frame_rate", 30)),
    }

    source_notes = {
        "title": str(_project_value(project, "title", "")),
        "script": str(_project_value(project, "script", "")),
        "captions": str(_project_value(project, "subtitle_text", "")),
        "sources": str(_project_value(project, "sources", "")),
    }

    payloads = {
        "scene_plan.json": scene_plan,
        "media_manifest.json": manifest,
        "timeline_settings.json": timeline_settings,
        "source_notes.json": source_notes,
    }

    created_files = []
    for filename, payload in payloads.items():
        path = export_folder / filename
        path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        created_files.append(path)

    builder_path = export_folder / "build_resolve_timeline.py"
    builder_path.write_text(_builder_script(), encoding="utf-8")
    created_files.append(builder_path)

    readme_path = export_folder / "README.txt"
    readme_path.write_text(
        "DaVinci Resolve Export Package\n"
        "==============================\n\n"
        "1. Start DaVinci Resolve.\n"
        "2. Ensure external scripting is enabled when required by your Resolve edition.\n"
        "3. Run build_resolve_timeline.py with the Python environment that can import "
        "DaVinciResolveScript.\n"
        "4. The script creates or opens the configured Resolve project, applies vertical "
        "timeline settings, and imports the package media.\n\n"
        "Automatic scene placement, captions, and voiceover timing will be added in PR #14.\n",
        encoding="utf-8",
    )
    created_files.append(readme_path)

    return ResolveExportResult(
        export_folder=export_folder,
        files=tuple(created_files),
        warnings=tuple(warnings),
    )


__all__ = [
    "ResolveExportResult",
    "build_scene_plan",
    "collect_media_manifest",
    "export_resolve_package",
    "validate_resolve_export",
]
