"""UI-facing controller and view state for content production.

The module is toolkit-neutral so Tk/customtkinter pages can bind buttons, labels,
progress bars, and stage selectors without running production work on the UI thread.
"""
from __future__ import annotations

from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass, field, replace
from pathlib import Path
from threading import Lock
from typing import Any, Callable, Mapping

from common.content_production import (
    ContentProductionEngine,
    ContentProductionError,
    ContentProductionResult,
    Provider,
    ProviderRegistry,
    STAGES,
)

StateCallback = Callable[["ProductionViewState"], None]
ExecutorSubmit = Callable[..., Future]


@dataclass(frozen=True)
class StageViewState:
    name: str
    label: str
    status: str = "pending"
    message: str = ""


@dataclass(frozen=True)
class ProductionViewState:
    running: bool = False
    progress: float = 0.0
    current_stage: str | None = None
    message: str = "Ready"
    error: str | None = None
    can_start: bool = True
    can_resume: bool = False
    can_cancel: bool = False
    completed: tuple[str, ...] = ()
    stages: tuple[StageViewState, ...] = field(default_factory=tuple)
    result: ContentProductionResult | None = None


class ProductionUIController:
    """Coordinate production runs and publish immutable UI state snapshots."""

    LABELS = {
        "research": "Research",
        "facts": "Select Facts",
        "script": "Write Script",
        "image_prompts": "Create Image Prompts",
        "voice": "Generate Voice",
        "timeline": "Build Timeline",
        "resolve": "Build Resolve Package",
    }

    def __init__(
        self,
        providers: ProviderRegistry | Mapping[str, Provider],
        *,
        engine_factory: Callable[..., ContentProductionEngine] = ContentProductionEngine,
        submit: ExecutorSubmit | None = None,
        state_callback: StateCallback | None = None,
    ) -> None:
        self.providers = providers if isinstance(providers, ProviderRegistry) else ProviderRegistry(providers)
        self.engine_factory = engine_factory
        self._executor = None if submit is not None else ThreadPoolExecutor(max_workers=1, thread_name_prefix="production")
        self._submit = submit or self._executor.submit
        self.state_callback = state_callback
        self._lock = Lock()
        self._cancel_requested = False
        self._future: Future | None = None
        self.state = ProductionViewState(stages=self._initial_stages())

    def _initial_stages(self) -> tuple[StageViewState, ...]:
        return tuple(StageViewState(name=name, label=self.LABELS[name]) for name in STAGES)

    def _publish(self, state: ProductionViewState) -> None:
        with self._lock:
            self.state = state
        if self.state_callback is not None:
            self.state_callback(state)

    def _set_stage(self, stages, name: str, status: str, message: str = ""):
        return tuple(
            replace(stage, status=status, message=message) if stage.name == name else stage
            for stage in stages
        )

    def refresh_checkpoint(self, project_folder: str | Path) -> ProductionViewState:
        resumable = (Path(project_folder) / "production_checkpoint.json").is_file()
        state = replace(self.state, can_resume=resumable)
        self._publish(state)
        return state

    def start(
        self,
        project: Mapping[str, Any],
        project_folder: str | Path,
        settings: Mapping[str, Any],
        *,
        topic: str | None = None,
        resume: bool = False,
        start_at: str | None = None,
        stop_after: str | None = None,
        launch_resolve: bool = False,
    ) -> Future:
        if self.state.running:
            raise RuntimeError("production is already running")
        if start_at is not None and start_at not in STAGES:
            raise ValueError(f"unknown start stage: {start_at}")
        self._cancel_requested = False
        state = ProductionViewState(
            running=True,
            message="Starting production",
            can_start=False,
            can_resume=False,
            can_cancel=True,
            stages=self._initial_stages(),
        )
        self._publish(state)

        def progress(stage: str, index: int, total: int, message: str) -> None:
            if self._cancel_requested:
                raise ContentProductionError("production cancelled")
            current = self.state
            stages = current.stages
            for completed_name in STAGES[: max(0, index - 1)]:
                stages = self._set_stage(stages, completed_name, "complete")
            stages = self._set_stage(stages, stage, "running", message)
            self._publish(replace(
                current,
                progress=max(0.0, min(1.0, (index - 1) / total)),
                current_stage=stage,
                message=message,
                stages=stages,
            ))

        engine = self.engine_factory(self.providers, progress_callback=progress)

        def work():
            return engine.run(
                project,
                project_folder,
                settings,
                topic=topic,
                resume=resume,
                start_at=start_at,
                stop_after=stop_after,
                launch_resolve=launch_resolve,
            )

        future = self._submit(work)
        self._future = future
        future.add_done_callback(self._finished)
        return future

    def resume(self, project, project_folder, settings, **options) -> Future:
        return self.start(project, project_folder, settings, resume=True, **options)

    def restart_from(self, stage: str, project, project_folder, settings, **options) -> Future:
        return self.start(project, project_folder, settings, resume=True, start_at=stage, **options)

    def cancel(self) -> bool:
        if not self.state.running:
            return False
        self._cancel_requested = True
        self._publish(replace(self.state, message="Cancelling production", can_cancel=False))
        return True

    def _finished(self, future: Future) -> None:
        current = self.state
        try:
            result = future.result()
        except Exception as error:
            cancelled = self._cancel_requested or "cancelled" in str(error).lower()
            stages = current.stages
            if current.current_stage:
                stages = self._set_stage(stages, current.current_stage, "cancelled" if cancelled else "failed", str(error))
            self._publish(replace(
                current,
                running=False,
                message="Production cancelled" if cancelled else "Production failed",
                error=None if cancelled else str(error),
                can_start=True,
                can_resume=True,
                can_cancel=False,
                stages=stages,
            ))
            return

        completed = tuple(result.completed)
        stages = current.stages
        for name in completed:
            stages = self._set_stage(stages, name, "complete")
        succeeded = result.succeeded
        self._publish(replace(
            current,
            running=False,
            progress=1.0 if succeeded else len(completed) / len(STAGES),
            current_stage=None,
            message="Production complete" if succeeded else "Partial production complete",
            error=None,
            can_start=True,
            can_resume=not succeeded,
            can_cancel=False,
            completed=completed,
            stages=stages,
            result=result,
        ))

    def close(self) -> None:
        if self._executor is not None:
            self._executor.shutdown(wait=False, cancel_futures=True)


__all__ = ["ProductionUIController", "ProductionViewState", "StageViewState"]
