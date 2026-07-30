from pathlib import Path

import pytest

from common.resolve_render import build_render_plan, render_resolve_project, safe_filename


def test_safe_filename_removes_windows_reserved_characters():
    assert safe_filename('Titanic: "Facts" / Part 1?') == "Titanic- -Facts- - Part 1-"
    assert safe_filename("   ") == "render"


def test_build_render_plan_defaults_to_vertical_h264_mp4(tmp_path):
    plan = build_render_plan(tmp_path, "Titanic Facts")

    assert plan["format"] == "mp4"
    assert plan["codec"] == "H264"
    assert plan["render_mode"] == 1
    assert plan["output_dir"] == str((tmp_path / "Renders").resolve())
    assert plan["output_path"].endswith(str(Path("Renders") / "Titanic Facts.mp4"))
    assert plan["render_settings"]["FormatWidth"] == 1080
    assert plan["render_settings"]["FormatHeight"] == 1920
    assert plan["render_settings"]["FrameRate"] == 30.0
    assert plan["render_settings"]["ExportAudio"] is True


class FakeProject:
    def __init__(self, status=None, timeline=True):
        self.status = status or {"JobStatus": "Complete", "CompletionPercentage": 100}
        self.timeline = object() if timeline else None
        self.calls = []
        self.polls = [True, False]

    def GetName(self):
        return "Titanic Facts"

    def GetCurrentTimeline(self):
        return self.timeline

    def SetCurrentRenderMode(self, mode):
        self.calls.append(("mode", mode))
        return True

    def SetCurrentRenderFormatAndCodec(self, render_format, codec):
        self.calls.append(("format", render_format, codec))
        return True

    def SetRenderSettings(self, settings):
        self.calls.append(("settings", settings))
        return True

    def AddRenderJob(self):
        self.calls.append(("add",))
        return "job-1"

    def StartRendering(self, job_id):
        self.calls.append(("start", job_id))
        return True

    def IsRenderingInProgress(self):
        return self.polls.pop(0) if self.polls else False

    def GetRenderJobStatus(self, job_id):
        self.calls.append(("status", job_id))
        return self.status

    def StopRendering(self):
        self.calls.append(("stop",))


class FakeManager:
    def __init__(self, project):
        self.project = project

    def GetCurrentProject(self):
        return self.project

    def LoadProject(self, name):
        return self.project


class FakeResolve:
    def __init__(self, project):
        self.manager = FakeManager(project)

    def GetProjectManager(self):
        return self.manager


def test_render_resolve_project_queues_waits_and_reports_output(tmp_path):
    project = FakeProject()
    result = render_resolve_project(
        FakeResolve(project),
        tmp_path,
        "Titanic Facts",
        poll_interval=0,
        sleeper=lambda _: None,
    )

    assert result["job_id"] == "job-1"
    assert result["status"] == "Complete"
    assert result["completion"] == 100
    assert result["output_path"].endswith("Titanic Facts.mp4")
    assert ("mode", 1) in project.calls
    assert ("format", "mp4", "H264") in project.calls
    assert ("start", "job-1") in project.calls


def test_render_resolve_project_rejects_failed_job(tmp_path):
    project = FakeProject({"JobStatus": "Failed", "Error": "Disk full"})

    with pytest.raises(RuntimeError, match="Disk full"):
        render_resolve_project(
            FakeResolve(project),
            tmp_path,
            "Titanic Facts",
            poll_interval=0,
            sleeper=lambda _: None,
        )


def test_render_resolve_project_requires_a_timeline(tmp_path):
    project = FakeProject(timeline=False)

    with pytest.raises(RuntimeError, match="no current timeline"):
        render_resolve_project(FakeResolve(project), tmp_path, "Titanic Facts")
