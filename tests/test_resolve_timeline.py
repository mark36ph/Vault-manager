from common.resolve_timeline import build_timeline_plan, choose_scene_media, seconds_to_frames
from common.resolve_timeline_runner import build_resolve_timeline


def test_seconds_to_frames_rounds_to_nearest_frame():
    assert seconds_to_frames(4.2, 30) == 126
    assert seconds_to_frames(-1, 30) == 0


def test_choose_scene_media_prefers_explicit_path_then_rotates_fallbacks():
    manifest = {
        "videos": [{"path": "Assets/Videos/ocean.mp4"}],
        "images": [{"path": "Assets/Images/ship.jpg"}],
    }
    assert choose_scene_media({"media_path": "custom.png"}, manifest, 0) == "custom.png"
    assert choose_scene_media({}, manifest, 0) == "Assets/Videos/ocean.mp4"
    assert choose_scene_media({}, manifest, 1) == "Assets/Images/ship.jpg"


def test_build_timeline_plan_creates_frame_accurate_placements():
    scene_plan = {
        "project": "Titanic Facts",
        "fps": 30,
        "scenes": [
            {"index": 1, "start": 0, "duration": 2.0, "caption": "One"},
            {"index": 2, "start": 2.0, "duration": 2.2, "caption": "Two"},
        ],
    }
    manifest = {
        "videos": [],
        "images": [{"path": "Assets/Images/ship.jpg"}],
        "audio": [{"path": "Voice/narration.wav"}],
    }
    settings = {"project_name": "Fact Vault Video", "width": 1080, "height": 1920, "fps": 30}

    plan = build_timeline_plan(scene_plan, manifest, settings)

    assert plan["duration_frames"] == 126
    assert plan["placements"][0]["start_frame"] == 0
    assert plan["placements"][0]["duration_frames"] == 60
    assert plan["placements"][1]["start_frame"] == 60
    assert plan["placements"][1]["end_frame"] == 126
    assert plan["narration_path"] == "Voice/narration.wav"


class FakeMediaItem:
    def __init__(self, path):
        self.path = path

    def GetClipProperty(self, name):
        return self.path if name == "File Path" else ""


class FakeMediaPool:
    def __init__(self):
        self.appended = []
        self.timeline = object()

    def ImportMedia(self, paths):
        return [FakeMediaItem(path) for path in paths]

    def CreateEmptyTimeline(self, name):
        return self.timeline

    def AppendToTimeline(self, clips):
        self.appended.extend(clips)
        return True


class FakeProject:
    def __init__(self):
        self.media_pool = FakeMediaPool()
        self.settings = {}

    def GetName(self):
        return "Fact Vault Video"

    def SetSetting(self, key, value):
        self.settings[key] = value
        return True

    def GetMediaPool(self):
        return self.media_pool

    def GetCurrentTimeline(self):
        return None


class FakeProjectManager:
    def __init__(self):
        self.project = FakeProject()

    def GetCurrentProject(self):
        return self.project

    def LoadProject(self, name):
        return self.project

    def CreateProject(self, name):
        return self.project


class FakeResolve:
    def __init__(self):
        self.manager = FakeProjectManager()

    def GetProjectManager(self):
        return self.manager


def test_build_resolve_timeline_imports_and_places_visual_and_narration(tmp_path):
    image = tmp_path / "Assets" / "Images" / "ship.jpg"
    voice = tmp_path / "Voice" / "narration.wav"
    image.parent.mkdir(parents=True)
    voice.parent.mkdir(parents=True)
    image.write_bytes(b"image")
    voice.write_bytes(b"audio")

    scene_plan = {
        "project": "Fact Vault Video",
        "fps": 30,
        "scenes": [{"index": 1, "start": 0, "duration": 4.2, "media_path": "Assets/Images/ship.jpg"}],
    }
    manifest = {
        "images": [{"path": "Assets/Images/ship.jpg"}],
        "videos": [],
        "audio": [{"path": "Voice/narration.wav"}],
    }
    settings = {"project_name": "Fact Vault Video", "width": 1080, "height": 1920, "fps": 30}
    resolve = FakeResolve()

    result = build_resolve_timeline(resolve, tmp_path, scene_plan, manifest, settings)

    assert result["visual_clips_added"] == 1
    assert result["narration_added"] is True
    assert result["warnings"] == ()
    assert len(resolve.manager.project.media_pool.appended) == 2
    assert resolve.manager.project.settings["timelineResolutionWidth"] == "1080"
    assert resolve.manager.project.settings["timelineResolutionHeight"] == "1920"
