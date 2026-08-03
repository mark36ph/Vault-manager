import json
from types import SimpleNamespace
from pathlib import Path

import pytest

import common.resolve_live as live
from common.resolve_live import LiveResolveError, LiveResolveService, _absolute_plan
from common.resolve_timeline_builder import ResolveTimelineBuildResult


def test_absolute_plan_resolves_portable_media(tmp_path):
    plan = {"tracks": [{"clips": [{"kind": "image", "source": "Media/Images/a.jpg"}]}]}
    result = _absolute_plan(plan, tmp_path)
    assert Path(result["tracks"][0]["clips"][0]["source"]).is_absolute()


def test_missing_plan_is_reported(tmp_path):
    with pytest.raises(LiveResolveError, match="timeline plan"):
        LiveResolveService().build_package(tmp_path, {})


def test_launches_resolve_waits_connects_builds_and_saves(tmp_path, monkeypatch):
    (tmp_path / "resolve_timeline_plan.json").write_text(
        json.dumps({"name": "Tower", "tracks": []}), encoding="utf-8"
    )
    executable = tmp_path / "Resolve.exe"
    executable.write_bytes(b"exe")
    project = SimpleNamespace(Save=lambda: True)
    manager = SimpleNamespace(GetCurrentProject=lambda: project)
    resolve = SimpleNamespace(GetProjectManager=lambda: manager)

    class Module:
        calls = 0
        @classmethod
        def scriptapp(cls, name):
            cls.calls += 1
            return None if cls.calls == 1 else resolve

    monkeypatch.setattr(live, "_module", lambda settings: Module)
    monkeypatch.setattr(
        live,
        "build_resolve_timeline",
        lambda *args, **kwargs: ResolveTimelineBuildResult(
            "Tower", "Tower", 2, 3, 1, ("warning",)
        ),
    )
    launches = []
    service = LiveResolveService(process_runner=lambda command: launches.append(command), sleeper=lambda _: None)
    result = service.build_package(tmp_path, {"application_path": str(executable)})
    assert launches == [[str(executable)]]
    assert result.launched_application is True
    assert result.placed_clips == 3
    assert result.warnings == ("warning",)


def test_running_resolve_does_not_launch_again(tmp_path, monkeypatch):
    (tmp_path / "resolve_timeline_plan.json").write_text(
        json.dumps({"name": "Fact", "tracks": []}), encoding="utf-8"
    )
    project = SimpleNamespace(Save=lambda: True)
    manager = SimpleNamespace(GetCurrentProject=lambda: project)
    resolve = SimpleNamespace(GetProjectManager=lambda: manager)
    module = SimpleNamespace(scriptapp=lambda name: resolve)
    monkeypatch.setattr(live, "_module", lambda settings: module)
    monkeypatch.setattr(
        live, "build_resolve_timeline",
        lambda *args, **kwargs: ResolveTimelineBuildResult("Fact", "Fact", 0, 0, 0, ()),
    )
    result = LiveResolveService(process_runner=lambda *_: pytest.fail("launched")).build_package(tmp_path, {})
    assert result.launched_application is False


def test_missing_executable_has_clear_error(tmp_path, monkeypatch):
    (tmp_path / "resolve_timeline_plan.json").write_text(
        json.dumps({"name": "Fact", "tracks": []}), encoding="utf-8"
    )
    monkeypatch.setattr(live, "_module", lambda settings: SimpleNamespace(scriptapp=lambda name: None))
    with pytest.raises(LiveResolveError, match="executable"):
        LiveResolveService().build_package(tmp_path, {"application_path": str(tmp_path / "missing.exe")})
