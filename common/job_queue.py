"""In-memory job queue and progress tracking for Vault Manager workflows."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Callable, Iterable
from uuid import uuid4

from common.pipeline import PipelineEvent, StageStatus


class JobStatus(str, Enum):
    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    SKIPPED = "skipped"


class JobEventType(str, Enum):
    ADDED = "added"
    STARTED = "started"
    PROGRESS = "progress"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    SKIPPED = "skipped"


@dataclass
class Job:
    name: str
    stage: str | None = None
    id: str = field(default_factory=lambda: uuid4().hex)
    status: JobStatus = JobStatus.PENDING
    progress: int = 0
    message: str = ""
    error: str | None = None
    created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    started_at: datetime | None = None
    finished_at: datetime | None = None

    def __post_init__(self) -> None:
        if not self.name or not self.name.strip():
            raise ValueError("job name must not be empty")
        if not 0 <= self.progress <= 100:
            raise ValueError("job progress must be between 0 and 100")
        if self.stage is None:
            self.stage = self.name


@dataclass(frozen=True)
class JobEvent:
    type: JobEventType
    job_id: str
    job_name: str
    stage: str
    status: JobStatus
    progress: int
    message: str
    timestamp: datetime


JobEventCallback = Callable[[JobEvent], None]


class JobQueue:
    """Track ordered workflow jobs and emit events for UI subscribers."""

    def __init__(
        self,
        jobs: Iterable[Job] | None = None,
        *,
        event_callback: JobEventCallback | None = None,
    ) -> None:
        self._jobs: list[Job] = []
        self._callbacks: list[JobEventCallback] = []
        self._events: list[JobEvent] = []
        if event_callback is not None:
            self.subscribe(event_callback)
        for job in jobs or ():
            self.add(job)

    @property
    def jobs(self) -> tuple[Job, ...]:
        return tuple(self._jobs)

    @property
    def events(self) -> tuple[JobEvent, ...]:
        return tuple(self._events)

    def subscribe(self, callback: JobEventCallback) -> None:
        if not callable(callback):
            raise TypeError("callback must be callable")
        if callback not in self._callbacks:
            self._callbacks.append(callback)

    def unsubscribe(self, callback: JobEventCallback) -> None:
        if callback in self._callbacks:
            self._callbacks.remove(callback)

    def add(self, job: Job) -> Job:
        if not isinstance(job, Job):
            raise TypeError("job must be a Job")
        if any(existing.id == job.id for existing in self._jobs):
            raise ValueError(f"duplicate job id: {job.id}")
        self._jobs.append(job)
        self._emit(job, JobEventType.ADDED, job.message or "Job added")
        return job

    def get(self, job_or_id: Job | str) -> Job:
        job_id = job_or_id.id if isinstance(job_or_id, Job) else job_or_id
        for job in self._jobs:
            if job.id == job_id:
                return job
        raise KeyError(f"unknown job: {job_id}")

    def get_by_stage(self, stage: str) -> Job | None:
        return next((job for job in self._jobs if job.stage == stage), None)

    def start(self, job_or_id: Job | str, message: str = "Job started") -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.PENDING})
        job.status = JobStatus.RUNNING
        job.started_at = datetime.now(timezone.utc)
        job.message = message
        self._emit(job, JobEventType.STARTED, message)
        return job

    def update_progress(self, job_or_id: Job | str, progress: int, message: str = "") -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.RUNNING})
        if not 0 <= progress <= 100:
            raise ValueError("progress must be between 0 and 100")
        job.progress = progress
        if message:
            job.message = message
        self._emit(job, JobEventType.PROGRESS, job.message)
        return job

    def complete(self, job_or_id: Job | str, message: str = "Job completed") -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.RUNNING})
        job.status = JobStatus.COMPLETED
        job.progress = 100
        job.message = message
        job.finished_at = datetime.now(timezone.utc)
        self._emit(job, JobEventType.COMPLETED, message)
        return job

    def fail(self, job_or_id: Job | str, error: Exception | str) -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.RUNNING})
        job.status = JobStatus.FAILED
        job.error = str(error)
        job.message = str(error)
        job.finished_at = datetime.now(timezone.utc)
        self._emit(job, JobEventType.FAILED, job.message)
        return job

    def cancel(self, job_or_id: Job | str, message: str = "Job cancelled") -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.PENDING, JobStatus.RUNNING})
        job.status = JobStatus.CANCELLED
        job.message = message
        job.finished_at = datetime.now(timezone.utc)
        self._emit(job, JobEventType.CANCELLED, message)
        return job

    def skip(self, job_or_id: Job | str, message: str = "Job skipped") -> Job:
        job = self.get(job_or_id)
        self._require_status(job, {JobStatus.PENDING})
        job.status = JobStatus.SKIPPED
        job.message = message
        job.finished_at = datetime.now(timezone.utc)
        self._emit(job, JobEventType.SKIPPED, message)
        return job

    def current_job(self) -> Job | None:
        return next((job for job in self._jobs if job.status == JobStatus.RUNNING), None)

    def running_jobs(self) -> tuple[Job, ...]:
        return self._with_status(JobStatus.RUNNING)

    def completed_jobs(self) -> tuple[Job, ...]:
        return self._with_status(JobStatus.COMPLETED)

    def failed_jobs(self) -> tuple[Job, ...]:
        return self._with_status(JobStatus.FAILED)

    def summary(self) -> dict[str, int]:
        summary = {status.value: 0 for status in JobStatus}
        for job in self._jobs:
            summary[job.status.value] += 1
        summary["total"] = len(self._jobs)
        return summary

    def handle_pipeline_event(self, event: PipelineEvent) -> None:
        """Update the matching job from a :class:`PipelineRunner` event."""
        job = self.get_by_stage(event.stage)
        if job is None:
            job = self.add(Job(name=event.stage, stage=event.stage))

        if event.status == StageStatus.RUNNING:
            if job.status == JobStatus.PENDING:
                self.start(job, event.message or "Stage started")
        elif event.status == StageStatus.SUCCEEDED:
            if job.status == JobStatus.PENDING:
                self.start(job)
            if job.status == JobStatus.RUNNING:
                self.complete(job, event.message or "Stage completed")
        elif event.status == StageStatus.FAILED:
            if job.status == JobStatus.PENDING:
                self.start(job)
            if job.status == JobStatus.RUNNING:
                self.fail(job, event.message or "Stage failed")
        elif event.status == StageStatus.SKIPPED and job.status == JobStatus.PENDING:
            self.skip(job, event.message or "Stage skipped")

    def activity_log(self) -> tuple[str, ...]:
        return tuple(
            f"{event.timestamp.astimezone().strftime('%H:%M:%S')} "
            f"{event.stage} {event.type.value}: {event.message}"
            for event in self._events
        )

    def _with_status(self, status: JobStatus) -> tuple[Job, ...]:
        return tuple(job for job in self._jobs if job.status == status)

    @staticmethod
    def _require_status(job: Job, allowed: set[JobStatus]) -> None:
        if job.status not in allowed:
            values = ", ".join(sorted(status.value for status in allowed))
            raise ValueError(
                f"job '{job.name}' must be in one of these states: {values}; "
                f"current state is {job.status.value}"
            )

    def _emit(self, job: Job, event_type: JobEventType, message: str) -> None:
        event = JobEvent(
            type=event_type,
            job_id=job.id,
            job_name=job.name,
            stage=job.stage or job.name,
            status=job.status,
            progress=job.progress,
            message=message,
            timestamp=datetime.now(timezone.utc),
        )
        self._events.append(event)
        for callback in tuple(self._callbacks):
            callback(event)


__all__ = [
    "Job",
    "JobEvent",
    "JobEventCallback",
    "JobEventType",
    "JobQueue",
    "JobStatus",
]
