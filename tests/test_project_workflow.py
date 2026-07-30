import pytest

from common.job_queue import JobQueue, JobStatus
from common.project_workflow import (
    ProjectWorkflow,
    STAGE_ORDER,
    WorkflowServices,
    create_project_workflow,
)
from common.pipeline import StageStatus


def test_workflow_builds_standard_stage_order():
    workflow = ProjectWorkflow(WorkflowServices())
    assert tuple(stage.name for stage in workflow.runner.stages) == STAGE_ORDER
    assert tuple(job.stage for job in workflow.queue.jobs) == STAGE_ORDER


def test_available_services_share_context_and_outputs():
    calls = []

    def research(context):
        calls.append("research")
        return {"fact": context["project"]["title"]}

    def script(context):
        calls.append("script")
        return f"Script: {context['research']['fact']}"

    result = ProjectWorkflow(
        WorkflowServices(research=research, script=script)
    ).run({"title": "Airplane windows"})

    assert calls == ["research", "script"]
    assert result.pipeline.context["script"] == "Script: Airplane windows"
    assert result.succeeded


def test_missing_services_are_skipped():
    result = ProjectWorkflow(WorkflowServices(research=lambda context: "facts")).run({})
    statuses = {stage.name: stage.status for stage in result.pipeline.stages}
    assert statuses["research"] == StageStatus.SUCCEEDED
    assert statuses["publish"] == StageStatus.SKIPPED
    assert result.queue.get_by_stage("publish").status == JobStatus.SKIPPED


def test_enabled_setting_can_disable_configured_service():
    called = []
    workflow = ProjectWorkflow(
        WorkflowServices(render=lambda context: called.append(True)),
        enabled={"render": False},
    )
    result = workflow.run({})
    assert called == []
    assert next(s for s in result.pipeline.stages if s.name == "render").status == StageStatus.SKIPPED


def test_failure_stops_later_services_and_updates_queue():
    workflow = ProjectWorkflow(
        WorkflowServices(
            timeline=lambda context: (_ for _ in ()).throw(RuntimeError("Resolve offline")),
            render=lambda context: "video.mp4",
        )
    )
    result = workflow.run({})
    assert result.pipeline.failed_stage.name == "timeline"
    assert result.queue.get_by_stage("timeline").status == JobStatus.FAILED
    assert result.queue.get_by_stage("render").status == JobStatus.SKIPPED


def test_continue_on_error_runs_later_service():
    workflow = ProjectWorkflow(
        WorkflowServices(
            research=lambda context: 1 / 0,
            script=lambda context: "continued",
        )
    )
    result = workflow.run({}, stop_on_error=False)
    assert result.pipeline.context["script"] == "continued"


def test_custom_queue_is_used():
    queue = JobQueue()
    workflow = create_project_workflow(
        WorkflowServices(research=lambda context: "ok"), queue=queue
    )
    result = workflow.run({})
    assert result.queue is queue
    assert queue.get_by_stage("research").status == JobStatus.COMPLETED


def test_unknown_enabled_stage_is_rejected():
    with pytest.raises(ValueError, match="unknown workflow stages"):
        ProjectWorkflow(WorkflowServices(), enabled={"unknown": True})


def test_project_must_be_mapping():
    workflow = ProjectWorkflow(WorkflowServices())
    with pytest.raises(TypeError, match="project must be a mapping"):
        workflow.run("not a project")
