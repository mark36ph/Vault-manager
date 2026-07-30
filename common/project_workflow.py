"""Project-level workflow wiring for Vault Manager.

This module connects service callables to the reusable PipelineRunner and JobQueue.
The UI can supply the concrete research, script, asset, Resolve, render, and publish
functions without coupling those services to the orchestration layer.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable, Mapping, MutableMapping

from common.job_queue import Job, JobQueue
from common.pipeline import PipelineResult, PipelineRunner, PipelineStage

WorkflowCallable = Callable[[MutableMapping[str, Any]], Any]

STAGE_ORDER = (
    "research",
    "script",
    "description",
    "images",
    "timeline",
    "polish",
    "render",
    "publish",
)

STAGE_LABELS = {
    "research": "Research",
    "script": "Generate Script",
    "description": "Create Description",
    "images": "Download Images",
    "timeline": "Generate Timeline",
    "polish": "Polish Timeline",
    "render": "Render Video",
    "publish": "Upload to YouTube",
}


@dataclass
class WorkflowServices:
    """Concrete functions used by the project workflow.

    Any missing service is represented as a disabled pipeline stage, so partial
    workflows can be run while features are configured incrementally.
    """

    research: WorkflowCallable | None = None
    script: WorkflowCallable | None = None
    description: WorkflowCallable | None = None
    images: WorkflowCallable | None = None
    timeline: WorkflowCallable | None = None
    polish: WorkflowCallable | None = None
    render: WorkflowCallable | None = None
    publish: WorkflowCallable | None = None

    def get(self, name: str) -> WorkflowCallable | None:
        return getattr(self, name)


@dataclass
class ProjectWorkflowResult:
    pipeline: PipelineResult
    queue: JobQueue

    @property
    def succeeded(self) -> bool:
        return self.pipeline.succeeded


class ProjectWorkflow:
    """Build and execute the standard Vault Manager project workflow."""

    def __init__(
        self,
        services: WorkflowServices,
        *,
        queue: JobQueue | None = None,
        enabled: Mapping[str, bool] | None = None,
    ) -> None:
        if not isinstance(services, WorkflowServices):
            raise TypeError("services must be WorkflowServices")
        self.services = services
        self.enabled = dict(enabled or {})
        unknown = set(self.enabled) - set(STAGE_ORDER)
        if unknown:
            raise ValueError(f"unknown workflow stages: {', '.join(sorted(unknown))}")
        self.queue = queue or self._build_queue()
        self.runner = PipelineRunner(
            self._build_stages(), progress_callback=self.queue.handle_pipeline_event
        )

    def _build_queue(self) -> JobQueue:
        return JobQueue(Job(STAGE_LABELS[name], stage=name) for name in STAGE_ORDER)

    def _build_stages(self) -> list[PipelineStage]:
        stages = []
        for name in STAGE_ORDER:
            service = self.services.get(name)
            enabled = self.enabled.get(name, True) and service is not None
            stages.append(
                PipelineStage(
                    name=name,
                    run=service or _missing_service,
                    output_key=name,
                    enabled=enabled,
                )
            )
        return stages

    def run(
        self,
        project: Mapping[str, Any],
        *,
        context: Mapping[str, Any] | None = None,
        stop_on_error: bool = True,
        raise_on_error: bool = False,
    ) -> ProjectWorkflowResult:
        if not isinstance(project, Mapping):
            raise TypeError("project must be a mapping")
        initial = dict(context or {})
        initial["project"] = dict(project)
        pipeline_result = self.runner.run(
            initial,
            stop_on_error=stop_on_error,
            raise_on_error=raise_on_error,
        )
        return ProjectWorkflowResult(pipeline=pipeline_result, queue=self.queue)


def create_project_workflow(
    services: WorkflowServices,
    *,
    queue: JobQueue | None = None,
    enabled: Mapping[str, bool] | None = None,
) -> ProjectWorkflow:
    return ProjectWorkflow(services, queue=queue, enabled=enabled)


def _missing_service(context: MutableMapping[str, Any]) -> None:
    raise RuntimeError("workflow service is not configured")


__all__ = [
    "ProjectWorkflow",
    "ProjectWorkflowResult",
    "STAGE_LABELS",
    "STAGE_ORDER",
    "WorkflowCallable",
    "WorkflowServices",
    "create_project_workflow",
]
