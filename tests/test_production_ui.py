from concurrent.futures import Future
from pathlib import Path

import pytest

from common.content_production import ContentProductionResult, ProductionContext, STAGES
from common.production_ui import ProductionUIController


class ImmediateSubmit:
    def __call__(self, function):
        future = Future()
        try:
            future.set_result(function())
        except Exception as error:
            future.set_exception(error)
        return future


class DeferredSubmit:
    def __init__(self):
        self.function = None
        self.future = Future()

    def __call__(self, function):
        self.function = function
        return self.future


class FakeEngine:
    result = None
    error = None
    calls = []

    def __init__(self, providers, progress_callback=None):
        self.progress_callback = progress_callback

    def run(self, project, project_folder, settings, **options):
        self.__class__.calls.append(options)
        if self.progress_callback:
            self.progress_callback("research", 1, len(STAGES), "Running research")
            self.progress_callback("facts", 2, len(STAGES), "Running facts")
        if self.__class__.error:
            raise self.__class__.error
        return self.__class__.result


def make_result(tmp_path: Path, completed=STAGES):
    context = ProductionContext({}, tmp_path, {})
    context.completed_stages = list(completed)
    return ContentProductionResult(context, completed[0] if completed else "", tuple(completed))


@pytest.fixture(autouse=True)
def reset_engine():
    FakeEngine.result = None
    FakeEngine.error = None
    FakeEngine.calls = []


def controller(callback=None, submit=None):
    return ProductionUIController(
        {}, engine_factory=FakeEngine, submit=submit or ImmediateSubmit(), state_callback=callback
    )


def test_initial_state_has_all_stages():
    state = controller().state
    assert [stage.name for stage in state.stages] == list(STAGES)
    assert state.can_start


def test_refresh_checkpoint_enables_resume(tmp_path):
    (tmp_path / "production_checkpoint.json").write_text("{}", encoding="utf-8")
    state = controller().refresh_checkpoint(tmp_path)
    assert state.can_resume


def test_refresh_checkpoint_disables_resume_when_missing(tmp_path):
    assert not controller().refresh_checkpoint(tmp_path).can_resume


def test_start_returns_future_and_completes(tmp_path):
    FakeEngine.result = make_result(tmp_path)
    ui = controller()
    future = ui.start({}, tmp_path, {})
    assert future.result().succeeded
    assert ui.state.message == "Production complete"
    assert ui.state.progress == 1.0


def test_progress_updates_stage_states(tmp_path):
    states = []
    FakeEngine.result = make_result(tmp_path)
    controller(states.append).start({}, tmp_path, {})
    assert any(state.current_stage == "research" for state in states)
    assert any(next(s for s in state.stages if s.name == "facts").status == "running" for state in states)


def test_partial_result_remains_resumable(tmp_path):
    FakeEngine.result = make_result(tmp_path, STAGES[:3])
    ui = controller()
    ui.start({}, tmp_path, {})
    assert ui.state.message == "Partial production complete"
    assert ui.state.can_resume


def test_failure_is_exposed_to_ui(tmp_path):
    FakeEngine.error = RuntimeError("provider offline")
    ui = controller()
    ui.start({}, tmp_path, {})
    assert ui.state.message == "Production failed"
    assert "provider offline" in ui.state.error
    assert ui.state.can_resume


def test_resume_passes_resume_option(tmp_path):
    FakeEngine.result = make_result(tmp_path)
    controller().resume({}, tmp_path, {})
    assert FakeEngine.calls[-1]["resume"] is True


def test_restart_from_passes_selected_stage(tmp_path):
    FakeEngine.result = make_result(tmp_path)
    controller().restart_from("script", {}, tmp_path, {})
    assert FakeEngine.calls[-1]["start_at"] == "script"


def test_invalid_restart_stage_is_rejected(tmp_path):
    with pytest.raises(ValueError, match="unknown start stage"):
        controller().restart_from("invalid", {}, tmp_path, {})


def test_second_run_is_rejected_while_running(tmp_path):
    submit = DeferredSubmit()
    ui = controller(submit=submit)
    ui.start({}, tmp_path, {})
    with pytest.raises(RuntimeError, match="already running"):
        ui.start({}, tmp_path, {})


def test_cancel_changes_state(tmp_path):
    submit = DeferredSubmit()
    ui = controller(submit=submit)
    ui.start({}, tmp_path, {})
    assert ui.cancel()
    assert ui.state.message == "Cancelling production"
    assert not ui.state.can_cancel


def test_cancel_when_idle_returns_false():
    assert controller().cancel() is False


def test_cancelled_worker_is_not_reported_as_error(tmp_path):
    submit = DeferredSubmit()
    ui = controller(submit=submit)
    ui.start({}, tmp_path, {})
    ui.cancel()
    submit.future.set_exception(RuntimeError("production cancelled"))
    assert ui.state.message == "Production cancelled"
    assert ui.state.error is None


def test_start_forwards_run_options(tmp_path):
    FakeEngine.result = make_result(tmp_path)
    controller().start(
        {"title": "Demo"}, tmp_path, {"frame_rate": 30}, topic="Ocean", stop_after="timeline", launch_resolve=True
    )
    options = FakeEngine.calls[-1]
    assert options["topic"] == "Ocean"
    assert options["stop_after"] == "timeline"
    assert options["launch_resolve"] is True
