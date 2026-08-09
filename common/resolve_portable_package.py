"""Create a self-contained, portable DaVinci Resolve export package."""

from __future__ import annotations

import hashlib
import json
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from common.resolve_complete_export import export_complete_resolve_package
from timeline import Timeline


class PortableResolvePackageError(RuntimeError):
    """Raised when a portable Resolve package cannot be created safely."""


@dataclass(frozen=True)
class PortableResolvePackageResult:
    package_folder: Path
    files: tuple[Path, ...]
    copied_media: tuple[Path, ...]
    warnings: tuple[str, ...]
    timeline_plan: Path
    manifest: Path


def _project_value(project: Any, key: str, default: str = "") -> str:
    try:
        value = project[key]
    except (KeyError, IndexError, TypeError):
        value = default
    return str(default if value is None else value)


def _safe_name(value: str, fallback: str) -> str:
    cleaned = "".join(character if character.isalnum() or character in "._- " else "_" for character in value)
    cleaned = " ".join(cleaned.split()).strip(" .")
    return cleaned or fallback


def _media_folder(kind: str) -> str:
    return {"image": "Images", "video": "Video", "audio": "Audio"}.get(kind, "Other")


def _unique_destination(folder: Path, source: Path, clip_id: str) -> Path:
    candidate = folder / source.name
    if not candidate.exists():
        return candidate
    suffix = source.suffix
    stem = _safe_name(source.stem, "media")
    return folder / f"{stem}_{clip_id[:8]}{suffix}"


def _checksum(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _srt_timestamp(seconds: float) -> str:
    milliseconds = max(0, int(round(float(seconds) * 1000)))
    hours, remainder = divmod(milliseconds, 3_600_000)
    minutes, remainder = divmod(remainder, 60_000)
    secs, millis = divmod(remainder, 1000)
    return f"{hours:02}:{minutes:02}:{secs:02},{millis:03}"


def _subtitle_text(clip: dict[str, Any]) -> str:
    metadata = clip.get("metadata") or {}
    return str(metadata.get("subtitle_text") or metadata.get("text") or clip.get("name") or "").strip()


def _write_subtitles(plan: dict[str, Any], path: Path) -> int:
    entries = []
    for track in plan.get("tracks", []):
        for clip in track.get("clips", []):
            if clip.get("kind") != "subtitle":
                continue
            text = _subtitle_text(clip)
            if text:
                entries.append((float(clip.get("start", 0)), float(clip.get("end", 0)), text))
    entries.sort(key=lambda item: item[0])
    blocks = []
    for index, (start, end, text) in enumerate(entries, start=1):
        blocks.append(f"{index}\n{_srt_timestamp(start)} --> {_srt_timestamp(end)}\n{text}\n")
    path.write_text("\n".join(blocks), encoding="utf-8")
    return len(entries)


def export_portable_resolve_package(
    project: Any,
    project_folder: str | Path,
    settings: dict[str, Any],
    timeline: Timeline,
    *,
    strict: bool = True,
    overwrite: bool = True,
) -> PortableResolvePackageResult:
    """Export a complete Resolve package with copied media and relative paths."""
    project_folder = Path(project_folder)
    if not project_folder.is_dir():
        raise FileNotFoundError(f"Project folder could not be found: {project_folder}")
    if not isinstance(timeline, Timeline):
        raise TypeError("timeline must be a Timeline")

    complete = export_complete_resolve_package(project, project_folder, settings, timeline, strict=strict)
    original_plan = json.loads(complete.timeline_plan.read_text(encoding="utf-8"))
    package_name = _safe_name(_project_value(project, "title", timeline.name), "Resolve Package")
    package_folder = complete.export_folder / "Portable" / package_name
    if package_folder.exists() and overwrite:
        shutil.rmtree(package_folder)
    elif package_folder.exists():
        raise FileExistsError(f"Portable package already exists: {package_folder}")

    media_root = package_folder / "Media"
    metadata_root = package_folder / "Metadata"
    subtitles_root = package_folder / "Subtitles"
    for folder in (media_root, metadata_root, subtitles_root):
        folder.mkdir(parents=True, exist_ok=True)

    warnings = list(complete.warnings)
    copied: list[Path] = []
    copied_by_source: dict[str, Path] = {}
    manifest_items: list[dict[str, Any]] = []

    for track in original_plan.get("tracks", []):
        for clip in track.get("clips", []):
            if clip.get("kind") not in {"image", "video", "audio"}:
                continue
            source_text = str(clip.get("source") or "")
            source = Path(source_text)
            if not source.is_absolute():
                source = project_folder / source
            source = source.resolve()
            if not source.is_file():
                message = f"Missing media for clip {clip.get('id', '')}: {source}"
                if strict:
                    raise PortableResolvePackageError(message)
                warnings.append(message)
                continue

            key = str(source)
            destination = copied_by_source.get(key)
            if destination is None:
                folder = media_root / _media_folder(str(clip.get("kind")))
                folder.mkdir(parents=True, exist_ok=True)
                destination = _unique_destination(folder, source, str(clip.get("id", "media")))
                shutil.copy2(source, destination)
                copied_by_source[key] = destination
                copied.append(destination)
                manifest_items.append(
                    {
                        "source": key,
                        "package_path": destination.relative_to(package_folder).as_posix(),
                        "size_bytes": destination.stat().st_size,
                        "sha256": _checksum(destination),
                    }
                )
            clip["source"] = destination.relative_to(package_folder).as_posix()

    plan_path = package_folder / "resolve_timeline_plan.json"
    plan_path.write_text(json.dumps(original_plan, indent=2, ensure_ascii=False), encoding="utf-8")

    subtitle_path = subtitles_root / "captions.srt"
    subtitle_count = _write_subtitles(original_plan, subtitle_path)

    metadata = {
        "title": _project_value(project, "title"),
        "description": _project_value(project, "description"),
        "pinned_comment": _project_value(project, "pinned_comment"),
        "script": _project_value(project, "script"),
        "sources": _project_value(project, "sources"),
        "subtitle_count": subtitle_count,
    }
    metadata_path = metadata_root / "project_metadata.json"
    metadata_path.write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding="utf-8")
    for key in ("title", "description", "pinned_comment", "script", "sources"):
        (metadata_root / f"{key}.txt").write_text(metadata[key], encoding="utf-8")

    manifest_payload = {
        "format": "factvault-resolve-package",
        "version": 1,
        "project": package_name,
        "media": manifest_items,
        "warnings": warnings,
    }
    manifest_path = package_folder / "package_manifest.json"
    manifest_path.write_text(json.dumps(manifest_payload, indent=2, ensure_ascii=False), encoding="utf-8")

    runner_source = complete.export_folder / "build_complete_resolve_timeline.py"
    runner_path = package_folder / "build_resolve_timeline.py"
    shutil.copy2(runner_source, runner_path)

    readme_path = package_folder / "README.txt"
    readme_path.write_text(
        "Portable DaVinci Resolve Package\n"
        "=================================\n\n"
        "This folder is self-contained. Keep its folder structure unchanged.\n\n"
        "Recommended workflow for DaVinci Resolve Free:\n"
        "1. Open DaVinci Resolve.\n"
        "2. Choose File > Import > Timeline.\n"
        "3. Select the .fcpxml file in this folder.\n\n"
        "No external scripting connection is required for the normal import workflow.\n"
        "Media referenced by the FCPXML is stored inside this portable package.\n\n"
        "The bundled build_resolve_timeline.py file is only for optional advanced "
        "Resolve scripting workflows.\n",
        encoding="utf-8",
    )

    files = tuple(path for path in package_folder.rglob("*") if path.is_file())
    return PortableResolvePackageResult(
        package_folder=package_folder,
        files=files,
        copied_media=tuple(copied),
        warnings=tuple(warnings),
        timeline_plan=plan_path,
        manifest=manifest_path,
    )


__all__ = [
    "PortableResolvePackageError",
    "PortableResolvePackageResult",
    "export_portable_resolve_package",
]
