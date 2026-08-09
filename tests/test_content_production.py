import json
from pathlib import Path

import pytest

from common.content_production import (
    ContentProductionEngine,
    ContentProductionError,
    ProductionCheckpointStore,
    ProductionContext,
    ProviderRegistry,
    STAGES,
    build_content_production,
)
from timeline import Timeline


def project():
    return {"title": "Ocean Fact", "topic": "deep ocean"}


def settings():
    return {"timeline_width": 1080, "timeline_height": 1920, "frame_rate": 30}


def providers(calls=None):
    calls = calls if calls is not None else []

    def stage(name, value):
        def run(context):
            calls.append(name)
            return value(context) if callable(value) else value
        return run

    return {
        "research": stage("research", {"summary": "Ocean research"}),
        "facts": stage("facts", ["The ocean is deep."]),
        "script": stage("script", "The ocean is deep."),
        "image_prompts": stage("image_prompts", ["deep blue ocean"]),
        "voice": stage("voice", "Voice/narration.wav"),
        "resolve": stage("resolve", {"package": "ready"}),
    }


def test_registry_rejects_unknown_provider():
    with pytest.raises(ValueError, match="unknown"):
        ProviderRegistry({"unknown": lambda context: None})


def test_registry_rejects_non_callable_provider():
    with pytest.raises(TypeError, match="callable"):
        ProviderRegistry({"research": object()})


def test_registry_can_replace_provider():
    registry = ProviderRegistry()
    first = lambda context: "first"
    second = lambda context: "second"
    registry.register("script", first)
    registry.register("script", second)
    assert registry.require("script") is second


def test_registry_require_reports_missing_provider():
    with pytest.raises(ContentProductionError, match="not configured"):
        ProviderRegistry().require("research")


def test_engine_runs_stages_in_order(tmp_path):
    calls = []
    result = ContentProductionEngine(providers(calls)).run(
        project(), tmp_path, settings(), resume=False
    )
    assert calls == ["research", "facts", "script", "image_prompts", "voice", "resolve"]
    assert result.completed == STAGES
    assert result.succeeded


def test_engine_builds_timeline_from_script_by_default(tmp_path):
    result = ContentProductionEngine(providers()).run(
        project(), tmp_path, settings(), resume=False, stop_after="timeline"
    )
    assert isinstance(result.context.timeline, Timeline)
    assert result.context.timeline.scenes[0].narration == "The ocean is deep."


def test_stage_values_are_available_to_later_providers(tmp_path):
    configured = providers()
    configured["facts"] = lambda context: [context.research["summary"]]
    configured["script"] = lambda context: context.facts[0]
    result = ContentProductionEngine(configured).run(
        project(), tmp_path, settings(), resume=False, stop_after="script"
    )
    assert result.context.script == "Ocean research"


def test_progress_reports_stage_position(tmp_path):
    events = []
    ContentProductionEngine(
        providers(), progress_callback=lambda stage, current, total, message: events.append((stage, current, total))
    ).run(project(), tmp_path, settings(), resume=False, stop_after="facts")
    assert events == [("research", 1, 7), ("facts", 2, 7)]


def test_checkpoint_is_saved_after_partial_run(tmp_path):
    ContentProductionEngine(providers()).run(
        project(), tmp_path, settings(), resume=False, stop_after="script"
    )
    checkpoint = tmp_path / "production_checkpoint.json"
    payload = json.loads(checkpoint.read_text(encoding="utf-8"))
    assert payload["completed_stages"] == ["research", "facts", "script"]
    assert payload["script"] == "The ocean is deep."


def test_resume_skips_completed_stages(tmp_path):
    calls = []
    engine = ContentProductionEngine(providers(calls))
    engine.run(project(), tmp_path, settings(), resume=False, stop_after="script")
    calls.clear()
    engine.run(project(), tmp_path, settings(), resume=True, stop_after="voice")
    assert calls == ["image_prompts", "voice"]


def test_resume_reruns_resolve_when_checkpoint_marked_it_complete(tmp_path):
    calls = []
    configured = providers(calls)
    engine = ContentProductionEngine(configured)

    engine.run(project(), tmp_path, settings(), resume=False, stop_after="timeline")

    checkpoint = tmp_path / "production_checkpoint.json"
    payload = json.loads(checkpoint.read_text(encoding="utf-8"))
    payload["completed_stages"].append("resolve")
    checkpoint.write_text(json.dumps(payload), encoding="utf-8")

    calls.clear()
    result = engine.run(project(), tmp_path, settings(), resume=True)

    assert calls == ["resolve"]
    assert result.succeeded
    assert result.context.resolve == {"package": "ready"}
    assert not checkpoint.exists()


def test_start_at_reruns_requested_stage(tmp_path):
    calls = []
    engine = ContentProductionEngine(providers(calls))
    engine.run(project(), tmp_path, settings(), resume=False, stop_after="script")
    calls.clear()
    engine.run(project(), tmp_path, settings(), resume=True, start_at="facts", stop_after="script")
    assert calls == ["facts", "script"]


def test_failed_stage_preserves_checkpoint(tmp_path):
    configured = providers()

    def fail(context):
        raise RuntimeError("provider offline")

    configured["voice"] = fail
    with pytest.raises(ContentProductionError, match="voice"):
        ContentProductionEngine(configured).run(project(), tmp_path, settings(), resume=False)
    payload = json.loads((tmp_path / "production_checkpoint.json").read_text(encoding="utf-8"))
    assert payload["completed_stages"] == ["research", "facts", "script", "image_prompts"]


def test_completed_run_removes_checkpoint(tmp_path):
    ContentProductionEngine(providers()).run(project(), tmp_path, settings(), resume=False)
    assert not (tmp_path / "production_checkpoint.json").exists()


def test_checkpoint_load_restores_serializable_values(tmp_path):
    context = ProductionContext(project=project(), project_folder=tmp_path, settings=settings())
    context.topic = "ocean"
    context.script = "A script"
    context.completed_stages = ["research", "script"]
    store = ProductionCheckpointStore(tmp_path)
    store.save(context)
    restored = store.load_into(
        ProductionContext(project=project(), project_folder=tmp_path, settings=settings())
    )
    assert restored.topic == "ocean"
    assert restored.script == "A script"
    assert restored.completed_stages == ["research", "script"]


def test_bad_checkpoint_reports_clear_error(tmp_path):
    (tmp_path / "production_checkpoint.json").write_text("not json", encoding="utf-8")
    context = ProductionContext(project=project(), project_folder=tmp_path, settings=settings())
    with pytest.raises(ContentProductionError, match="checkpoint"):
        ProductionCheckpointStore(tmp_path).load_into(context)


def test_convenience_function_runs_pipeline(tmp_path):
    result = build_content_production(
        project(), tmp_path, settings(), providers(), resume=False, stop_after="facts"
    )
    assert result.completed == ("research", "facts")


def test_engine_rejects_unknown_stage_options(tmp_path):
    engine = ContentProductionEngine(providers())
    with pytest.raises(ValueError, match="start stage"):
        engine.run(project(), tmp_path, settings(), start_at="missing")
    with pytest.raises(ValueError, match="stop stage"):
        engine.run(project(), tmp_path, settings(), stop_after="missing")


def test_engine_requires_project_folder(tmp_path):
    missing = tmp_path / "missing"
    with pytest.raises(FileNotFoundError):
        ContentProductionEngine(providers()).run(project(), missing, settings())
