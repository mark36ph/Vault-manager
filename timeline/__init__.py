"""Core timeline domain model for FactVault Manager."""

from .assets import (
    Asset,
    AssetAssignmentEngine,
    AssetAssignmentError,
    AssetKind,
    AssetStatus,
)
from .builder import TimelineBuilder
from .materializer import (
    ClipMaterializationError,
    TimelineClipMaterializer,
    materialize_timeline_clips,
)
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
    "Asset",
    "AssetAssignmentEngine",
    "AssetAssignmentError",
    "AssetKind",
    "AssetStatus",
    "Clip",
    "ClipKind",
    "ClipMaterializationError",
    "ProjectTimelineStore",
    "Scene",
    "SceneBuilder",
    "TIMELINE_FILENAME",
    "Timeline",
    "TimelineBuilder",
    "TimelineClipMaterializer",
    "TimelineStorageError",
    "TimelineValidationError",
    "TimelineValidator",
    "Track",
    "TrackKind",
    "Transition",
    "ValidationIssue",
    "build_project_timeline",
    "ensure_project_timeline",
    "materialize_timeline_clips",
]
