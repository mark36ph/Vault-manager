"""Build a complete Resolve package from a materialized internal timeline."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from common.resolve_export import ResolveExportResult, export_resolve_package
from common.resolve_timeline_adapter import build_resolve_timeline_plan
from timeline import Timeline


@dataclass(frozen=True)
class CompleteResolveExportResult:
    export_folder: Path
    files: tuple[Path, ...]
    warnings: tuple[str, ...]
    timeline_plan: Path


def _runner_script() -> str:
    return '''from pathlib import Path
import json
import sys

PACKAGE_FOLDER = Path(__file__).resolve().parent
PROJECT_FOLDER = PACKAGE_FOLDER.parent
APP_FOLDER = PROJECT_FOLDER.parent
if str(APP_FOLDER) not in sys.path:
    sys.path.insert(0, str(APP_FOLDER))


def main():
    modules = PACKAGE_FOLDER / "Modules"
    if modules.exists() and str(modules) not in sys.path:
        sys.path.insert(0, str(modules))
    try:
        import DaVinciResolveScript as dvr_script
    except ImportError as error:
        raise SystemExit("DaVinciResolveScript could not be imported. Configure Resolve scripting and try again.") from error

    from common.resolve_timeline_builder import build_resolve_timeline

    with (PACKAGE_FOLDER / "resolve_timeline_plan.json").open("r", encoding="utf-8") as handle:
        plan = json.load(handle)
    resolve = dvr_script.scriptapp("Resolve")
    if resolve is None:
        raise SystemExit("Could not connect to DaVinci Resolve. Start Resolve and try again.")
    result = build_resolve_timeline(resolve, plan)
    print(f"Built {result.timeline_name}: {result.placed_clips} clips, {result.markers} markers")
    for warning in result.warnings:
        print(f"WARNING: {warning}")


if __name__ == "__main__":
    main()
'''


def export_complete_resolve_package(
    project,
    project_folder: str | Path,
    settings: dict,
    timeline: Timeline,
    *,
    strict: bool = True,
) -> CompleteResolveExportResult:
    """Export legacy package files plus the executable internal timeline plan."""
    if not isinstance(timeline, Timeline):
        raise TypeError("timeline must be a Timeline")
    base: ResolveExportResult = export_resolve_package(project, project_folder, settings)
    export_folder = base.export_folder
    plan = build_resolve_timeline_plan(
        timeline,
        project_folder=project_folder,
        strict=strict,
    )
    plan_path = export_folder / "resolve_timeline_plan.json"
    plan_path.write_text(json.dumps(plan, indent=2, ensure_ascii=False), encoding="utf-8")

    runner_path = export_folder / "build_complete_resolve_timeline.py"
    runner_path.write_text(_runner_script(), encoding="utf-8")

    readme_path = export_folder / "README_COMPLETE.txt"
    readme_path.write_text(
        "Complete DaVinci Resolve Timeline Package\n"
        "========================================\n\n"
        "1. Start DaVinci Resolve.\n"
        "2. Ensure Resolve external scripting is available.\n"
        "3. Run build_complete_resolve_timeline.py from the FactVault project environment.\n"
        "4. The script creates/opens the project, imports referenced media, creates the timeline, "
        "places image/video/audio clips, and creates subtitle/marker annotations.\n\n"
        "Transitions that Resolve cannot apply safely through the generic scripting API are "
        "reported as finishing warnings.\n",
        encoding="utf-8",
    )

    warnings = tuple(base.warnings) + tuple(plan.get("warnings", []))
    files = tuple(base.files) + (plan_path, runner_path, readme_path)
    return CompleteResolveExportResult(
        export_folder=export_folder,
        files=files,
        warnings=warnings,
        timeline_plan=plan_path,
    )


__all__ = ["CompleteResolveExportResult", "export_complete_resolve_package"]
