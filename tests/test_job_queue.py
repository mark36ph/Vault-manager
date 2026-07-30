import pytest

from common.job_queue import Job, JobEventType, JobQueue, JobStatus
from common.pipeline import PipelineRunner, PipelineStage


def test_queue_adds_jobs_in_order_and_emits_event():
    events = []
    queue = JobQueue(event_callback=events.append)
    first = queue.add(Job("Research"))
    second = queue.add(Job("Script"))

    assert queue.jobs == (first, second)
    assert [event.type for event in events] == [JobEventType.ADDED, JobEventType.ADDED]


def test_job_lifecycle_records_timestamps_and_progress():
    queue = JobQueue([Job("Render")])
    job = queue.jobs[0]

    queue.start(job)
    queue.update_progress(job, 45, "Rendering frames")
    queue.complete(job)

    assert job.status == JobStatus.COMPLETED
    assert job.progress == 100
    assert job.started_at is not None
    assert job.finished_at is not None


def test_queue_tracks_failed_jobs():
    queue = JobQueue([Job("Upload")])
    job = queue.start(queue.jobs[0])

    queue.fail(job, OSError("offline"))

    assert queue.failed_jobs() == (job,)
    assert job.error == "offline"
    assert queue.current_job() is None


def test_queue_can_cancel_pending_and_running_jobs():
    pending = Job("Pending")
    running = Job("Running")
    queue = JobQueue([pending, running])
    queue.start(running)

    queue.cancel(pending)
    queue.cancel(running)

    assert pending.status == JobStatus.CANCELLED
    assert running.status == JobStatus.CANCELLED


def test_progress_requires_running_job_and_valid_percentage():
    queue = JobQueue([Job("Images")])
    job = queue.jobs[0]

    with pytest.raises(ValueError, match="current state is pending"):
        queue.update_progress(job, 10)

    queue.start(job)
    with pytest.raises(ValueError, match="between 0 and 100"):
        queue.update_progress(job, 101)


def test_summary_counts_all_job_states():
    jobs = [Job("One"), Job("Two"), Job("Three"), Job("Four")]
    queue = JobQueue(jobs)
    queue.start(jobs[0])
    queue.complete(jobs[0])
    queue.start(jobs[1])
    queue.fail(jobs[1], "bad")
    queue.cancel(jobs[2])

    assert queue.summary() == {
        "pending": 1,
        "running": 0,
        "completed": 1,
        "failed": 1,
        "cancelled": 1,
        "skipped": 0,
        "total": 4,
    }


def test_queue_rejects_duplicate_ids():
    job = Job("Research", id="same")
    queue = JobQueue([job])

    with pytest.raises(ValueError, match="duplicate job id"):
        queue.add(Job("Other", id="same"))


def test_pipeline_events_update_matching_jobs():
    queue = JobQueue([Job("Research", stage="research"), Job("Script", stage="script")])
    runner = PipelineRunner(
        [
            PipelineStage("research", lambda context: "facts"),
            PipelineStage("script", lambda context: "script"),
        ],
        progress_callback=queue.handle_pipeline_event,
    )

    runner.run()

    assert [job.status for job in queue.jobs] == [
        JobStatus.COMPLETED,
        JobStatus.COMPLETED,
    ]


def test_pipeline_failure_skips_remaining_job():
    queue = JobQueue([Job("Render", stage="render"), Job("Publish", stage="publish")])
    runner = PipelineRunner(
        [
            PipelineStage("render", lambda context: 1 / 0),
            PipelineStage("publish", lambda context: "uploaded"),
        ],
        progress_callback=queue.handle_pipeline_event,
    )

    runner.run()

    assert queue.jobs[0].status == JobStatus.FAILED
    assert queue.jobs[1].status == JobStatus.SKIPPED


def test_activity_log_contains_stage_and_messages():
    queue = JobQueue([Job("Research", stage="research")])
    job = queue.start(queue.jobs[0], "Looking for sources")
    queue.complete(job, "Research complete")

    log = queue.activity_log()

    assert any("research started: Looking for sources" in line for line in log)
    assert any("research completed: Research complete" in line for line in log)
