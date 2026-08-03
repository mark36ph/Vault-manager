"""Build Resolve Free FCPXML using only media copied into the portable package."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping
from urllib.parse import unquote, urlparse
import xml.etree.ElementTree as ET

from common.fcpxml_export import FCPXMLExportResult, export_fcpxml
from common.resolve_portable_package import PortableResolvePackageResult
from timeline import ClipKind, Timeline


class ResolveExportV2Error(RuntimeError):
    """Raised when a portable Resolve export is not truly self-contained."""


@dataclass(frozen=True)
class ResolveExportV2Result:
    fcpxml: FCPXMLExportResult
    remapped_media: int
    validated_media: tuple[Path, ...]


def _manifest_mapping(package: PortableResolvePackageResult) -> dict[str, Path]:
    try:
        payload = json.loads(package.manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ResolveExportV2Error(f"Could not read portable package manifest: {package.manifest}") from error
    mapping: dict[str, Path] = {}
    for item in payload.get("media", []):
        if not isinstance(item, Mapping):
            continue
        source = str(item.get("source") or "").strip()
        package_path = str(item.get("package_path") or "").strip()
        if source and package_path:
            mapping[str(Path(source).resolve())] = (package.package_folder / package_path).resolve()
    return mapping


def _portable_timeline(timeline: Timeline, package: PortableResolvePackageResult) -> tuple[Timeline, int]:
    portable = Timeline.from_dict(timeline.to_dict())
    mapping = _manifest_mapping(package)
    remapped = 0
    missing: list[str] = []
    for track in portable.tracks:
        for clip in track.clips:
            if clip.kind not in {ClipKind.IMAGE, ClipKind.VIDEO, ClipKind.AUDIO} or not clip.source:
                continue
            original = str(Path(clip.source).resolve())
            copied = mapping.get(original)
            if copied is None:
                missing.append(f"{clip.name or clip.id}: {original}")
                continue
            if not copied.is_file():
                missing.append(f"{clip.name or clip.id}: copied file missing: {copied}")
                continue
            clip.source = str(copied)
            remapped += 1
    if missing:
        raise ResolveExportV2Error(
            "Portable media mapping is incomplete:\n" + "\n".join(missing)
        )
    return portable, remapped


def _path_from_file_url(value: str) -> Path:
    parsed = urlparse(value)
    path = unquote(parsed.path)
    if len(path) >= 3 and path[0] == "/" and path[2] == ":":
        path = path[1:]
    return Path(path)


def validate_fcpxml_media(fcpxml_path: str | Path, package_folder: str | Path) -> tuple[Path, ...]:
    """Verify every FCPXML asset resolves to an existing file inside the package."""
    package_root = Path(package_folder).resolve()
    try:
        root = ET.parse(fcpxml_path).getroot()
    except (OSError, ET.ParseError) as error:
        raise ResolveExportV2Error(f"Could not validate FCPXML: {fcpxml_path}") from error
    validated: list[Path] = []
    failures: list[str] = []
    for asset in root.findall("./resources/asset"):
        src = str(asset.attrib.get("src") or "")
        path = _path_from_file_url(src).resolve()
        try:
            path.relative_to(package_root)
        except ValueError:
            failures.append(f"Asset is outside portable package: {path}")
            continue
        if not path.is_file():
            failures.append(f"Asset does not exist: {path}")
            continue
        validated.append(path)
    if failures:
        raise ResolveExportV2Error("FCPXML media validation failed:\n" + "\n".join(failures))
    return tuple(validated)


def export_resolve_free_v2(
    timeline: Timeline,
    package: PortableResolvePackageResult,
    destination: str | Path,
) -> ResolveExportV2Result:
    portable, remapped = _portable_timeline(timeline, package)
    fcpxml = export_fcpxml(portable, destination)
    validated = validate_fcpxml_media(fcpxml.path, package.package_folder)
    return ResolveExportV2Result(fcpxml, remapped, validated)


__all__ = [
    "ResolveExportV2Error",
    "ResolveExportV2Result",
    "export_resolve_free_v2",
    "validate_fcpxml_media",
]
