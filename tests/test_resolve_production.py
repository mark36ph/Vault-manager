from pathlib import Path

import pytest

from common.resolve_production import (
    ResolveProductionError,
    ResolveProductionService,
    build_resolve_production,
    make_resolve_workflow_service,
)
from timeline import Clip, ClipKind, Timeline, Track, TrackKind


def project_data():
    return {
        "title": "Ocean Fact",
        "description": "Description",
        "pinned_comment": "Comment",
        "script": "Ocean fact.",
        "sources": "Source",
        "subtitle_text": "Ocean fact.",
        "narration_duration": 2,
    }


def settings():
    return {
        "timeline_width": 1080,
        "timeline_height": 1920,
        "frame_rate": 30,
        "default_project_name": "Ocean Fact",
    }


def timeline_for(folder: Path):
    image = folder / "Assets" / "ocean.jpg"
    audio = folder / "Voice" / "voice.wav"
    image.parent.mkdir(parents=True, exist_ok=True)
    audio.parent.mkdir(parents=True, exist_ok=True)
    image.write_bytes(b"image")
    audio.write_bytes(b"audio")
    video = Track(kind=TrackKind.VIDEO, name="Video 1")
    video.add_clip(Clip(kind=ClipKind.IMAGE, start=0, duration=2, source=str(image)))
    narration = Track(kind=TrackKind.AUDIO, name="Narration")
    narration.add_clip(Clip(kind=ClipKind.AUDIO, start=0, duration=2, source=str(audio)))
    return Timeline(name="Ocean Fact", width=1080, height=1920, tracks=[video, narration])


def test_rejects_non_mapping_project(tmp_path):
    with pytest.raises(TypeError, match="project"):
        ResolveProductionService().run([], tmp_path, settings())


def test_rejects_non_mapping_settings(tmp_path):
    with pytest.raises(TypeError, match="settings"):
        ResolveProductionService().run(project_data(), tmp_path, [])


def test_rejects_missing_project_folder(tmp_path):
    with pytest.raises(FileNotFoundError):
        ResolveProductionService().run(project_data(), tmp_path / "missing", settings())


def test_builds_portable_package_and_saves_timeline(tmp_path):
    result = ResolveProductionService().run(
        project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path), materialize=False
    )
    assert result.timeline_path == tmp_path / "timeline.json"
    assert result.timeline_path.is_file()
    assert result.package.package_folder.is_dir()
    assert result.launched is False


def test_reports_package_warnings(tmp_path):
    result = ResolveProductionService().run(
        project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path), materialize=False
    )
    assert result.warnings == result.package.warnings


def test_launches_generated_runner(tmp_path):
    calls = []

    def runner(command, **kwargs):
        calls.append((command, kwargs))
        return object()

    result = ResolveProductionService(
        process_runner=runner, python_executable="python-test"
    ).run(
        project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path),
        materialize=False, launch=True,
    )
    assert result.launched is True
    assert result.command[0] == "python-test"
    assert result.command[1].endswith("build_resolve_timeline.py")
    assert calls[0][1]["cwd"] == result.package.package_folder


def test_wraps_launch_os_error(tmp_path):
    def runner(*args, **kwargs):
        raise OSError("blocked")

    with pytest.raises(ResolveProductionError, match="Could not launch"):
        ResolveProductionService(process_runner=runner).run(
            project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path),
            materialize=False, launch=True,
        )


def test_emits_progress_in_order(tmp_path):
    events = []
    service = ResolveProductionService(
        progress_callback=lambda stage, fraction, message: events.append((stage, fraction, message))
    )
    service.run(
        project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path), materialize=False
    )
    assert [event[0] for event in events] == ["timeline", "package", "complete"]
    assert events[-1][1] == 1.0


def test_materialize_progress_is_reported(tmp_path):
    events = []
    empty = Timeline(name="Empty")
    ResolveProductionService(
        progress_callback=lambda stage, fraction, message: events.append(message)
    ).run(project_data(), tmp_path, settings(), timeline=empty, materialize=True, strict=False)
    assert "Materializing assigned assets" in events


def test_loads_existing_timeline_when_not_supplied(tmp_path):
    original = timeline_for(tmp_path)
    from timeline import ProjectTimelineStore
    ProjectTimelineStore(tmp_path).save(original)
    result = ResolveProductionService().run(
        project_data(), tmp_path, settings(), materialize=False
    )
    assert result.timeline_path.is_file()


def test_convenience_function_builds_package(tmp_path):
    result = build_resolve_production(
        project_data(), tmp_path, settings(), timeline=timeline_for(tmp_path), materialize=False
    )
    assert result.package.package_folder.exists()


def test_workflow_service_reads_project_from_context(tmp_path):
    service = ResolveProductionService()
    workflow_stage = make_resolve_workflow_service(
        tmp_path, settings(), service=service, timeline=timeline_for(tmp_path), materialize=False
    )
    result = workflow_stage({"project": project_data()})
    assert result.package.package_folder.exists()


def test_workflow_service_rejects_missing_project(tmp_path):
    workflow_stage = make_resolve_workflow_service(tmp_path, settings())
    with pytest.raises(ResolveProductionError, match="project mapping"):
        workflow_stage({})
