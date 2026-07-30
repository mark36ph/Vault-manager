import pytest

from common.pipeline import (
    PipelineRunner,
    PipelineStage,
    PipelineStageError,
    StageStatus,
)


def test_pipeline_runs_stages_in_order_and_shares_context():
    calls = []

    def research(context):
        calls.append("research")
        return {"topic": context["topic"], "facts": ["one", "two"]}

    def script(context):
        calls.append("script")
        return f"Script about {context['research']['topic']}"

    runner = PipelineRunner(
        [
            PipelineStage("research", research),
            PipelineStage("script", script),
        ]
    )

    result = runner.run({"topic": "space"})

    assert result.succeeded is True
    assert calls == ["research", "script"]
    assert result.context["script"] == "Script about space"
    assert [stage.status for stage in result.stages] == [
        StageStatus.SUCCEEDED,
        StageStatus.SUCCEEDED,
    ]


def test_pipeline_supports_custom_output_keys():
    runner = PipelineRunner(
        [PipelineStage("render", lambda context: "video.mp4", output_key="video")]
    )

    result = runner.run()

    assert result.context["video"] == "video.mp4"
    assert "render" not in result.context


def test_pipeline_skips_disabled_stage():
    runner = PipelineRunner(
        [
            PipelineStage("publish", lambda context: "uploaded", enabled=False),
            PipelineStage("archive", lambda context: "archived"),
        ]
    )

    result = runner.run()

    assert result.succeeded is True
    assert result.stages[0].status == StageStatus.SKIPPED
    assert result.context["archive"] == "archived"


def test_pipeline_stops_and_skips_remaining_stages_after_failure():
    def fail(context):
        raise ValueError("render failed")

    runner = PipelineRunner(
        [
            PipelineStage("render", fail),
            PipelineStage("publish", lambda context: "uploaded"),
        ]
    )

    result = runner.run()

    assert result.succeeded is False
    assert result.failed_stage.name == "render"
    assert isinstance(result.failed_stage.error, ValueError)
    assert result.stages[1].status == StageStatus.SKIPPED
    assert "publish" not in result.context


def test_pipeline_can_continue_after_failure():
    runner = PipelineRunner(
        [
            PipelineStage("broken", lambda context: 1 / 0),
            PipelineStage("cleanup", lambda context: "done"),
        ]
    )

    result = runner.run(stop_on_error=False)

    assert result.stages[0].status == StageStatus.FAILED
    assert result.stages[1].status == StageStatus.SUCCEEDED
    assert result.context["cleanup"] == "done"


def test_pipeline_can_raise_wrapped_stage_error():
    runner = PipelineRunner(
        [PipelineStage("upload", lambda context: (_ for _ in ()).throw(OSError("offline")))]
    )

    with pytest.raises(PipelineStageError) as error:
        runner.run(raise_on_error=True)

    assert error.value.stage == "upload"
    assert isinstance(error.value.original_error, OSError)


def test_pipeline_emits_progress_events():
    events = []
    runner = PipelineRunner(
        [PipelineStage("research", lambda context: "complete")],
        progress_callback=events.append,
    )

    runner.run()

    assert [event.status for event in events] == [
        StageStatus.RUNNING,
        StageStatus.SUCCEEDED,
    ]
    assert all(event.stage == "research" for event in events)
    assert events[-1].elapsed_seconds is not None


def test_pipeline_rejects_duplicate_stage_names():
    runner = PipelineRunner([PipelineStage("research", lambda context: None)])

    with pytest.raises(ValueError, match="duplicate pipeline stage"):
        runner.add_stage(PipelineStage("research", lambda context: None))
