from pathlib import Path

import common.resolve_integration as resolve_integration


def test_find_resolve_prefers_configured_path(tmp_path):
    configured = tmp_path / "Resolve.exe"
    configured.write_text("", encoding="utf-8")

    assert resolve_integration.find_resolve_application(configured) == configured


def test_find_resolve_returns_none_when_candidates_are_missing(monkeypatch):
    monkeypatch.setattr(
        resolve_integration,
        "candidate_application_paths",
        lambda system_name=None: [Path("/missing/resolve")],
    )

    assert resolve_integration.find_resolve_application() is None


def test_ensure_scripting_module_path_adds_existing_folder(monkeypatch, tmp_path):
    module_folder = tmp_path / "Modules"
    module_folder.mkdir()
    monkeypatch.setattr(resolve_integration.sys, "path", [])

    result = resolve_integration.ensure_scripting_module_path(module_folder)

    assert result == module_folder
    assert str(module_folder) in resolve_integration.sys.path


def test_inspect_resolve_reports_connected_instance(monkeypatch, tmp_path):
    application = tmp_path / "Resolve.exe"
    application.write_text("", encoding="utf-8")

    class FakeResolve:
        def GetProductName(self):
            return "DaVinci Resolve Studio"

        def GetVersionString(self):
            return "20.0"

    class FakeModule:
        @staticmethod
        def scriptapp(name):
            assert name == "Resolve"
            return FakeResolve()

    monkeypatch.setattr(
        resolve_integration,
        "load_scripting_module",
        lambda configured_module_path="": FakeModule(),
    )

    status = resolve_integration.inspect_resolve(application, "")

    assert status.installed is True
    assert status.scripting_module_available is True
    assert status.connected is True
    assert status.product_name == "DaVinci Resolve Studio"
    assert status.version == "20.0"


def test_inspect_resolve_handles_installed_but_missing_module(monkeypatch, tmp_path):
    application = tmp_path / "Resolve.exe"
    application.write_text("", encoding="utf-8")
    monkeypatch.setattr(
        resolve_integration,
        "load_scripting_module",
        lambda configured_module_path="": None,
    )

    status = resolve_integration.inspect_resolve(application, "")

    assert status.installed is True
    assert status.scripting_module_available is False
    assert status.connected is False
    assert "module was not found" in status.message
