import pytest

from common.resolve_timeline_builder import (
    ResolveTimelineBuildError,
    ResolveTimelineBuilder,
    build_resolve_timeline,
)


class FakeTimeline:
    def __init__(self):
        self.markers = []

    def AddMarker(self, *args):
        self.markers.append(args)
        return True


class FakeMediaPool:
    def __init__(self, *, import_count=None, append_ok=True):
        self.import_count = import_count
        self.append_ok = append_ok
        self.imported = []
        self.appended = []
        self.timeline = FakeTimeline()

    def ImportMedia(self, paths):
        self.imported = list(paths)
        count = len(paths) if self.import_count is None else self.import_count
        return [object() for _ in range(count)]

    def CreateEmptyTimeline(self, name):
        self.timeline.name = name
        return self.timeline

    def AppendToTimeline(self, payload):
        self.appended.extend(payload)
        return [object()] if self.append_ok else []


class FakeProject:
    def __init__(self, name="Demo", media_pool=None):
        self.name = name
        self.media_pool = media_pool or FakeMediaPool()
        self.settings = {}
        self.current_timeline = None

    def GetName(self):
        return self.name

    def SetSetting(self, key, value):
        self.settings[key] = value
        return True

    def GetMediaPool(self):
        return self.media_pool

    def SetCurrentTimeline(self, timeline):
        self.current_timeline = timeline
        return True

    def GetCurrentTimeline(self):
        return self.current_timeline


class FakeManager:
    def __init__(self, project=None, create_ok=True):
        self.project = project
        self.create_ok = create_ok

    def GetCurrentProject(self):
        return self.project

    def CreateProject(self, name):
        if not self.create_ok:
            return None
        self.project = FakeProject(name)
        return self.project


class FakeResolve:
    def __init__(self, manager=None):
        self.manager = manager or FakeManager()

    def GetProjectManager(self):
        return self.manager


def plan():
    return {
        "name": "Fact Timeline",
        "frame_rate": 30,
        "resolution": [1080, 1920],
        "tracks": [
            {
                "index": 1,
                "name": "Video 1",
                "kind": "video",
                "clips": [
                    {
                        "id": "image-1",
                        "kind": "image",
                        "source": "C:/media/image.jpg",
                        "start": 1,
                        "duration": 2,
                        "source_in": 0,
                        "transition_in": None,
                        "transition_out": None,
                        "metadata": {},
                    }
                ],
            },
            {
                "index": 1,
                "name": "Narration",
                "kind": "audio",
                "clips": [
                    {
                        "id": "audio-1",
                        "kind": "audio",
                        "source": "C:/media/voice.wav",
                        "start": 0,
                        "duration": 3,
                        "source_in": 0.5,
                        "transition_in": None,
                        "transition_out": None,
                        "metadata": {},
                    }
                ],
            },
            {
                "index": 1,
                "name": "Subtitles",
                "kind": "subtitle",
                "clips": [
                    {
                        "id": "sub-1",
                        "name": "Caption",
                        "kind": "subtitle",
                        "source": "caption.srt",
                        "start": 0,
                        "duration": 3,
                        "metadata": {"subtitle_text": "Hello"},
                    }
                ],
            },
        ],
    }


def test_frames_round_to_nearest_frame():
    assert ResolveTimelineBuilder._frames(1.5, 30) == 45


def test_builder_rejects_missing_resolve():
    with pytest.raises(TypeError, match="resolve"):
        ResolveTimelineBuilder(None)


def test_builder_rejects_non_dictionary_plan():
    with pytest.raises(TypeError, match="plan"):
        ResolveTimelineBuilder(FakeResolve()).build(None)


def test_builder_rejects_invalid_frame_rate():
    payload = plan()
    payload["frame_rate"] = 0
    with pytest.raises(ResolveTimelineBuildError, match="frame_rate"):
        ResolveTimelineBuilder(FakeResolve()).build(payload)


def test_builder_creates_project_and_applies_vertical_settings():
    resolve = FakeResolve(FakeManager(project=None))
    result = ResolveTimelineBuilder(resolve).build(plan())
    project = resolve.manager.project
    assert result.project_name == "Fact Timeline"
    assert project.settings["timelineResolutionWidth"] == "1080"
    assert project.settings["timelineResolutionHeight"] == "1920"
    assert float(project.settings["timelineFrameRate"]) == 30.0


def test_builder_reuses_matching_current_project():
    project = FakeProject("Fact Timeline")
    manager = FakeManager(project=project)
    ResolveTimelineBuilder(FakeResolve(manager)).build(plan())
    assert manager.project is project


def test_builder_fails_when_project_cannot_be_created():
    manager = FakeManager(project=None, create_ok=False)
    with pytest.raises(ResolveTimelineBuildError, match="could not create"):
        ResolveTimelineBuilder(FakeResolve(manager)).build(plan())


def test_builder_imports_each_unique_media_source_once():
    project = FakeProject("Fact Timeline")
    ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(plan())
    assert project.media_pool.imported == ["C:/media/image.jpg", "C:/media/voice.wav"]


def test_builder_places_video_and_audio_at_expected_frames():
    project = FakeProject("Fact Timeline")
    result = ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(plan())
    video, audio = project.media_pool.appended
    assert video["recordFrame"] == 30
    assert video["startFrame"] == 0
    assert video["endFrame"] == 59
    assert video["mediaType"] == 1
    assert audio["recordFrame"] == 0
    assert audio["startFrame"] == 15
    assert audio["mediaType"] == 2
    assert result.placed_clips == 2


def test_builder_creates_subtitle_marker():
    project = FakeProject("Fact Timeline")
    result = ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(plan())
    assert result.markers == 1
    assert project.media_pool.timeline.markers[0][2] == "Caption"
    assert project.media_pool.timeline.markers[0][3] == "Hello"


def test_builder_warns_when_media_import_is_partial():
    media_pool = FakeMediaPool(import_count=1)
    project = FakeProject("Fact Timeline", media_pool)
    result = ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(plan())
    assert any("imported 1 of 2" in warning for warning in result.warnings)
    assert any("media was not imported" in warning for warning in result.warnings)


def test_builder_warns_when_append_fails():
    project = FakeProject("Fact Timeline", FakeMediaPool(append_ok=False))
    result = ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(plan())
    assert result.placed_clips == 0
    assert any("could not place clip" in warning for warning in result.warnings)


def test_builder_reports_transitions_as_finishing_work():
    payload = plan()
    payload["tracks"][0]["clips"][0]["transition_in"] = {"name": "Cross Dissolve"}
    project = FakeProject("Fact Timeline")
    result = ResolveTimelineBuilder(FakeResolve(FakeManager(project))).build(payload)
    assert any("Cross Dissolve" in warning for warning in result.warnings)


def test_convenience_function_returns_result():
    project = FakeProject("Fact Timeline")
    result = build_resolve_timeline(FakeResolve(FakeManager(project)), plan())
    assert result.timeline_name == "Fact Timeline"
    assert project.current_timeline is project.media_pool.timeline
