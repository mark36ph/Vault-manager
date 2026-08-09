import json
import shutil
from pathlib import Path
from urllib.parse import unquote, urlparse
import xml.etree.ElementTree as ET

import pytest

from common.fcpxml_paths import rebase_fcpxml_media_paths
from common.resolve_export_v2 import (
    ResolveExportV2Error,
    export_resolve_free_v2,
    validate_fcpxml_media,
)
from common.resolve_portable_package import PortableResolvePackageResult
from timeline import Clip, ClipKind, Timeline, Track, TrackKind


def make_package(tmp_path, source: Path, copied: Path):
    package = tmp_path / "Portable" / "Project"
    copied.parent.mkdir(parents=True, exist_ok=True)
    copied.write_bytes(source.read_bytes())
    manifest = package / "package_manifest.json"
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps({
        "media": [{
            "source": str(source.resolve()),
            "package_path": copied.relative_to(package).as_posix(),
        }]
    }), encoding="utf-8")
    plan = package / "resolve_timeline_plan.json"
    plan.write_text("{}", encoding="utf-8")
    return PortableResolvePackageResult(
        package_folder=package,
        files=(copied, manifest, plan),
        copied_media=(copied,),
        warnings=(),
        timeline_plan=plan,
        manifest=manifest,
    )


def make_timeline(source: Path):
    track = Track(kind=TrackKind.VIDEO, name="Visuals")
    track.add_clip(Clip(kind=ClipKind.IMAGE, start=0, duration=4, source=str(source), name="Tower"))
    return Timeline(name="Tower", width=1080, height=1920, frame_rate=30, tracks=[track])


def file_url_path(value):
    parsed = urlparse(value)
    path = unquote(parsed.path)
    if len(path) >= 3 and path[0] == "/" and path[2] == ":":
        path = path[1:]
    return Path(path)


def test_export_references_only_copied_portable_media(tmp_path):
    source = tmp_path / "project" / "Assets" / "tower.jpg"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"image")
    package_root = tmp_path / "Portable" / "Project"
    copied = package_root / "Media" / "Images" / "tower.jpg"
    package = make_package(tmp_path, source, copied)

    output = package.package_folder / "Tower.fcpxml"
    result = export_resolve_free_v2(make_timeline(source), package, output)

    assert result.remapped_media == 1
    assets = ET.parse(output).getroot().findall("./resources/asset")
    assert len(assets) == 1
    referenced = file_url_path(assets[0].attrib["src"]).resolve()
    assert referenced == copied.resolve()
    assert referenced.is_file()
    assert str(source.resolve()) not in output.read_text(encoding="utf-8")


def test_fcpxml_rebases_after_project_folder_moves(tmp_path):
    old_project = tmp_path / "In Progress" / "Project"
    source = old_project / "Assets" / "tower.jpg"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"image")

    package_root = old_project / "Resolve" / "Portable" / "Project"
    copied = package_root / "Media" / "Images" / "tower.jpg"
    package = make_package(old_project / "Resolve", source, copied)
    output = package.package_folder / "Tower.fcpxml"
    export_resolve_free_v2(make_timeline(source), package, output)

    new_project = tmp_path / "Completed" / "Project"
    new_project.parent.mkdir(parents=True)
    shutil.move(str(old_project), str(new_project))

    rewritten = rebase_fcpxml_media_paths(new_project, old_project, new_project)
    assert rewritten == 1

    moved_xml = new_project / "Resolve" / "Portable" / "Project" / "Tower.fcpxml"
    moved_media = new_project / "Resolve" / "Portable" / "Project" / "Media" / "Images" / "tower.jpg"

    validated = validate_fcpxml_media(
        moved_xml,
        moved_xml.parent,
        expected_media=[moved_media],
    )
    assert validated == (moved_media.resolve(),)


def test_validation_rejects_asset_outside_package(tmp_path):
    outside = tmp_path / "outside.jpg"
    outside.write_bytes(b"image")
    xml = tmp_path / "Portable" / "Project" / "bad.fcpxml"
    xml.parent.mkdir(parents=True)
    xml.write_text(
        '<?xml version="1.0"?><fcpxml><resources><asset src="'
        + outside.resolve().as_uri()
        + '"/></resources></fcpxml>',
        encoding="utf-8",
    )
    with pytest.raises(ResolveExportV2Error, match="outside portable package Media folder"):
        validate_fcpxml_media(xml, xml.parent)


def test_validation_rejects_asset_inside_package_but_outside_media_folder(tmp_path):
    package = tmp_path / "Portable" / "Project"
    metadata = package / "Metadata" / "not-media.jpg"
    metadata.parent.mkdir(parents=True)
    metadata.write_bytes(b"image")
    xml = package / "bad.fcpxml"
    xml.write_text(
        '<?xml version="1.0"?><fcpxml><resources><asset src="'
        + metadata.resolve().as_uri()
        + '"/></resources></fcpxml>',
        encoding="utf-8",
    )

    with pytest.raises(ResolveExportV2Error, match="outside portable package Media folder"):
        validate_fcpxml_media(xml, package)


def test_validation_requires_expected_media_to_be_referenced(tmp_path):
    package = tmp_path / "Portable" / "Project"
    expected = package / "Media" / "Images" / "tower.jpg"
    expected.parent.mkdir(parents=True)
    expected.write_bytes(b"image")
    xml = package / "missing.fcpxml"
    xml.write_text(
        '<?xml version="1.0"?><fcpxml><resources></resources></fcpxml>',
        encoding="utf-8",
    )

    with pytest.raises(ResolveExportV2Error, match="Expected media is not referenced"):
        validate_fcpxml_media(xml, package, expected_media=[expected])


def test_export_fails_when_manifest_does_not_map_clip(tmp_path):
    source = tmp_path / "source.jpg"
    source.write_bytes(b"image")
    package_root = tmp_path / "Portable" / "Project"
    package_root.mkdir(parents=True)
    manifest = package_root / "package_manifest.json"
    manifest.write_text('{"media": []}', encoding="utf-8")
    plan = package_root / "resolve_timeline_plan.json"
    plan.write_text("{}", encoding="utf-8")
    package = PortableResolvePackageResult(package_root, (manifest, plan), (), (), plan, manifest)

    with pytest.raises(ResolveExportV2Error, match="mapping is incomplete"):
        export_resolve_free_v2(make_timeline(source), package, package_root / "bad.fcpxml")
