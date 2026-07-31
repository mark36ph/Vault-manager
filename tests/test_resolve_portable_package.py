import hashlib
import json
from pathlib import Path

import pytest

from common.resolve_portable_package import (
    PortableResolvePackageError,
    export_portable_resolve_package,
)
from timeline import Clip, ClipKind, Scene, Timeline, Track, TrackKind


def settings():
    return {
        "timeline_width": 1080,
        "timeline_height": 1920,
        "frame_rate": 30,
        "default_project_name": "Fact Vault Video",
    }


def project_data():
    return {
        "title": "Ocean Fact",
        "description": "A short ocean fact.",
        "pinned_comment": "Which fact surprised you?",
        "script": "The ocean is deep.",
        "sources": "Example source",
        "subtitle_text": "The ocean is deep.",
        "narration_duration": 2,
        "on_screen_text": "Deep ocean",
        "visual_plan": "Show the ocean.",
    }


def make_timeline(tmp_path: Path, *, include_subtitle=True):
    image = tmp_path / "Assets" / "ocean.jpg"
    audio = tmp_path / "Voice" / "narration.wav"
    image.parent.mkdir(parents=True, exist_ok=True)
    audio.parent.mkdir(parents=True, exist_ok=True)
    image.write_bytes(b"image data")
    audio.write_bytes(b"audio data")

    video_track = Track(kind=TrackKind.VIDEO, name="Video 1")
    video_track.add_clip(
        Clip(kind=ClipKind.IMAGE, start=0, duration=2, source=str(image), id="image-1")
    )
    audio_track = Track(kind=TrackKind.AUDIO, name="Narration")
    audio_track.add_clip(
        Clip(kind=ClipKind.AUDIO, start=0, duration=2, source=str(audio), id="audio-1")
    )
    tracks = [video_track, audio_track]
    clip_ids = ["image-1", "audio-1"]
    if include_subtitle:
        subtitle_track = Track(kind=TrackKind.SUBTITLE, name="Subtitles")
        subtitle_track.add_clip(
            Clip(
                kind=ClipKind.SUBTITLE,
                start=0,
                duration=2,
                source="captions.srt",
                name="Caption",
                id="subtitle-1",
                metadata={"subtitle_text": "The ocean is deep."},
            )
        )
        tracks.append(subtitle_track)
        clip_ids.append("subtitle-1")
    return Timeline(
        name="Ocean Fact",
        width=1080,
        height=1920,
        tracks=tracks,
        scenes=[Scene(title="Scene 1", start=0, duration=2, clip_ids=clip_ids)],
    )


def export(tmp_path, **kwargs):
    return export_portable_resolve_package(
        project_data(), tmp_path, settings(), make_timeline(tmp_path), **kwargs
    )


def test_export_creates_portable_package(tmp_path):
    result = export(tmp_path)
    assert result.package_folder.is_dir()
    assert result.package_folder.name == "Ocean Fact"


def test_export_copies_image_and_audio(tmp_path):
    result = export(tmp_path)
    assert len(result.copied_media) == 2
    assert (result.package_folder / "Media" / "Images" / "ocean.jpg").is_file()
    assert (result.package_folder / "Media" / "Audio" / "narration.wav").is_file()


def test_plan_uses_package_relative_media_paths(tmp_path):
    result = export(tmp_path)
    plan = json.loads(result.timeline_plan.read_text(encoding="utf-8"))
    sources = [
        clip["source"]
        for track in plan["tracks"]
        for clip in track["clips"]
        if clip["kind"] in {"image", "audio"}
    ]
    assert sources == ["Media/Images/ocean.jpg", "Media/Audio/narration.wav"]


def test_subtitle_file_is_generated(tmp_path):
    result = export(tmp_path)
    subtitles = (result.package_folder / "Subtitles" / "captions.srt").read_text(encoding="utf-8")
    assert "00:00:00,000 --> 00:00:02,000" in subtitles
    assert "The ocean is deep." in subtitles


def test_empty_subtitle_file_is_valid(tmp_path):
    timeline = make_timeline(tmp_path, include_subtitle=False)
    result = export_portable_resolve_package(project_data(), tmp_path, settings(), timeline)
    assert (result.package_folder / "Subtitles" / "captions.srt").read_text(encoding="utf-8") == ""


def test_metadata_json_contains_publish_fields(tmp_path):
    result = export(tmp_path)
    metadata = json.loads(
        (result.package_folder / "Metadata" / "project_metadata.json").read_text(encoding="utf-8")
    )
    assert metadata["title"] == "Ocean Fact"
    assert metadata["description"] == "A short ocean fact."
    assert metadata["pinned_comment"] == "Which fact surprised you?"


def test_metadata_text_files_are_written(tmp_path):
    result = export(tmp_path)
    folder = result.package_folder / "Metadata"
    assert (folder / "title.txt").read_text(encoding="utf-8") == "Ocean Fact"
    assert (folder / "script.txt").read_text(encoding="utf-8") == "The ocean is deep."


def test_manifest_contains_sizes_and_sha256(tmp_path):
    result = export(tmp_path)
    manifest = json.loads(result.manifest.read_text(encoding="utf-8"))
    image = next(item for item in manifest["media"] if item["package_path"].endswith("ocean.jpg"))
    assert image["size_bytes"] == len(b"image data")
    assert image["sha256"] == hashlib.sha256(b"image data").hexdigest()


def test_duplicate_source_is_copied_once(tmp_path):
    timeline = make_timeline(tmp_path)
    source = timeline.tracks[0].clips[0].source
    timeline.tracks[0].add_clip(
        Clip(kind=ClipKind.IMAGE, start=2, duration=1, source=source, id="image-2")
    )
    result = export_portable_resolve_package(project_data(), tmp_path, settings(), timeline)
    assert len([path for path in result.copied_media if path.suffix == ".jpg"]) == 1


def test_same_named_different_sources_get_unique_names(tmp_path):
    timeline = make_timeline(tmp_path)
    other = tmp_path / "Other" / "ocean.jpg"
    other.parent.mkdir()
    other.write_bytes(b"other")
    timeline.tracks[0].add_clip(
        Clip(kind=ClipKind.IMAGE, start=2, duration=1, source=str(other), id="second-image")
    )
    result = export_portable_resolve_package(project_data(), tmp_path, settings(), timeline)
    names = sorted(path.name for path in result.copied_media if path.suffix == ".jpg")
    assert names == ["ocean.jpg", "ocean_second-i.jpg"]


def test_strict_mode_rejects_missing_media(tmp_path):
    timeline = make_timeline(tmp_path)
    timeline.tracks[0].clips[0].source = str(tmp_path / "missing.jpg")
    with pytest.raises((PortableResolvePackageError, ValueError), match="missing|does not exist|Missing"):
        export_portable_resolve_package(project_data(), tmp_path, settings(), timeline)


def test_non_strict_mode_warns_about_missing_media(tmp_path):
    timeline = make_timeline(tmp_path)
    timeline.tracks[0].clips[0].source = str(tmp_path / "missing.jpg")
    result = export_portable_resolve_package(
        project_data(), tmp_path, settings(), timeline, strict=False
    )
    assert any("missing.jpg" in warning for warning in result.warnings)


def test_overwrite_rebuilds_existing_package(tmp_path):
    first = export(tmp_path)
    stale = first.package_folder / "stale.txt"
    stale.write_text("old", encoding="utf-8")
    second = export(tmp_path, overwrite=True)
    assert not (second.package_folder / "stale.txt").exists()


def test_no_overwrite_rejects_existing_package(tmp_path):
    export(tmp_path)
    with pytest.raises(FileExistsError, match="already exists"):
        export(tmp_path, overwrite=False)


def test_runner_and_readme_are_included(tmp_path):
    result = export(tmp_path)
    assert (result.package_folder / "build_resolve_timeline.py").is_file()
    readme = (result.package_folder / "README.txt").read_text(encoding="utf-8")
    assert "self-contained" in readme
    assert result.timeline_plan in result.files
