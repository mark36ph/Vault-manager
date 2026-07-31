"""Core timeline domain model for FactVault Manager."""

from .builder import TimelineBuilder
from .models import Clip, ClipKind, Scene, Timeline, Track, TrackKind, Transition
from .scene_builder import SceneBuilder, build_project_timeline
from .storage import (
    ProjectTimelineStore,
    TIMELINE_FILENAME,
    TimelineStorageError,
    ensure_project_timeline,
)
from .validator import TimelineValidationError, TimelineValidator, ValidationIssue

__all__ = [
    "Clip",
    "ClipKind",
    "ProjectTimelineStore",
    "Scene",
    "SceneBuilder",
    "TIMELINE_FILENAME",
    "Timeline",
    "TimelineBuilder",
    "TimelineStorageError",
    "TimelineValidationError",
    "TimelineValidator",
    "Track",
    "TrackKind",
    "Transition",
    "ValidationIssue",
    "build_project_timeline",
    "ensure_project_timeline",
]
