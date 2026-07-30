"""Reusable workflow orchestration for Vault Manager.

The pipeline module intentionally contains no UI or service-specific code. Existing
research, scripting, media, Resolve, render, and publishing functions can be
registered as stages and coordinated through :class:`PipelineRunner`.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from time import monotonic
from typing import Any, Callable, Iterable, Mapping, MutableMapping


class StageStatus(str, Enum):
    """Lifecycle states reported for a pipeline stage."""

    PENDING = "pending"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    SKIPPED = "skipped"


@dataclass(frozen=True)
class PipelineEvent:
    """Progress event emitted while a pipeline runs."""

    stage: str
    status: StageStatus
    message: str = ""
    elapsed_seconds: float | None = None


@dataclass
class StageResult:
    """Result returned for one stage."""

    name: str
    status: StageStatus
    value: Any = None
    error: Exception | None = None
    elapsed_seconds: float = 0.0


@dataclass
class PipelineResult:
    """Complete result returned by :meth:`PipelineRunner.run`."""

    stages: list[StageResult] = field(default_factory=list)
    context: MutableMapping[str, Any] = field(default_factory=dict)

    @property
    def succeeded(self) -> bool:
        return all(
            stage.status in {StageStatus.SUCCEEDED, StageStatus.SKIPPED}
            for stage in self.stages
        )

    @property
    def failed_stage(self) -> StageResult | None:
        return next(
            (stage for stage in self.stages if stage.status == StageStatus.FAILED),
            None,
        )


StageCallable = Callable[[MutableMapping[str, Any]], Any]
ProgressCallback = Callable[[PipelineEvent], None]


@dataclass(frozen=True)
class PipelineStage:
    """A named unit of pipeline work.

    ``run`` receives the shared mutable context. Its return value is stored in the
    context under ``output_key`` (or the stage name when no key is supplied).
    ``enabled`` can be a boolean or a predicate evaluated against the context.
    """

    name: str
    run: StageCallable
    output_key: str | None = None
    enabled: bool | Callable[[Mapping[str, Any]], bool] = True

    def is_enabled(self, context: Mapping[str, Any]) -> bool:
        if callable(self.enabled):
            return bool(self.enabled(context))
        return bool(self.enabled)


class PipelineStageError(RuntimeError):
    """Raised when a stage fails and ``raise_on_error`` is enabled."""

    def __init__(self, stage: str, original_error: Exception):
        super().__init__(f"Pipeline stage '{stage}' failed: {original_error}")
        self.stage = stage
        self.original_error = original_error


class PipelineRunner:
    """Run ordered workflow stages with shared state and progress reporting."""

    def __init__(
        self,
        stages: Iterable[PipelineStage] | None = None,
        *,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self._stages: list[PipelineStage] = []
        self.progress_callback = progress_callback
        for stage in stages or ():
            self.add_stage(stage)

    @property
    def stages(self) -> tuple[PipelineStage, ...]:
        return tuple(self._stages)

    def add_stage(self, stage: PipelineStage) -> "PipelineRunner":
        if not isinstance(stage, PipelineStage):
            raise TypeError("stage must be a PipelineStage")
        if not stage.name or not stage.name.strip():
            raise ValueError("stage name must not be empty")
        if any(existing.name == stage.name for existing in self._stages):
            raise ValueError(f"duplicate pipeline stage: {stage.name}")
        self._stages.append(stage)
        return self

    def run(
        self,
        initial_context: Mapping[str, Any] | None = None,
        *,
        stop_on_error: bool = True,
        raise_on_error: bool = False,
    ) -> PipelineResult:
        context: MutableMapping[str, Any] = dict(initial_context or {})
        results: list[StageResult] = []
        failure_seen = False

        for stage in self._stages:
            if failure_seen and stop_on_error:
                result = StageResult(stage.name, StageStatus.SKIPPED)
                results.append(result)
                self._emit(stage.name, StageStatus.SKIPPED, "Skipped after failure")
                continue

            if not stage.is_enabled(context):
                result = StageResult(stage.name, StageStatus.SKIPPED)
                results.append(result)
                self._emit(stage.name, StageStatus.SKIPPED, "Stage disabled")
                continue

            self._emit(stage.name, StageStatus.RUNNING, "Stage started")
            started = monotonic()

            try:
                value = stage.run(context)
                elapsed = monotonic() - started
                context[stage.output_key or stage.name] = value
                result = StageResult(
                    name=stage.name,
                    status=StageStatus.SUCCEEDED,
                    value=value,
                    elapsed_seconds=elapsed,
                )
                results.append(result)
                self._emit(
                    stage.name,
                    StageStatus.SUCCEEDED,
                    "Stage completed",
                    elapsed,
                )
            except Exception as error:  # pipeline boundary intentionally catches services
                elapsed = monotonic() - started
                result = StageResult(
                    name=stage.name,
                    status=StageStatus.FAILED,
                    error=error,
                    elapsed_seconds=elapsed,
                )
                results.append(result)
                failure_seen = True
                self._emit(
                    stage.name,
                    StageStatus.FAILED,
                    str(error),
                    elapsed,
                )
                if raise_on_error:
                    raise PipelineStageError(stage.name, error) from error

        return PipelineResult(stages=results, context=context)

    def _emit(
        self,
        stage: str,
        status: StageStatus,
        message: str,
        elapsed_seconds: float | None = None,
    ) -> None:
        if self.progress_callback is None:
            return
        self.progress_callback(
            PipelineEvent(
                stage=stage,
                status=status,
                message=message,
                elapsed_seconds=elapsed_seconds,
            )
        )


__all__ = [
    "PipelineEvent",
    "PipelineResult",
    "PipelineRunner",
    "PipelineStage",
    "PipelineStageError",
    "StageResult",
    "StageStatus",
]
