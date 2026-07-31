"""Convert scene asset assignments into concrete timeline clips."""

from __future__ import annotations

from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

from .assets import Asset, AssetAssignmentEngine, AssetKind, AssetStatus
from .models import Clip, ClipKind, Timeline, Track, TrackKind, Transition


class ClipMaterializationError(ValueError):
    """Raised when assigned assets cannot be converted into timeline clips."""


class TimelineClipMaterializer:
    """Build deterministic timeline clips from scene-level asset assignments."""

    GENERATED_BY = "asset_assignment"
    TRACKS = {
        AssetKind.IMAGE: ("Video 1", TrackKind.VIDEO, ClipKind.IMAGE),
        AssetKind.VIDEO: ("Video 1", TrackKind.VIDEO, ClipKind.VIDEO),
        AssetKind.AUDIO: ("Narration", TrackKind.AUDIO, ClipKind.AUDIO),
        AssetKind.SUBTITLE: ("Subtitles", TrackKind.SUBTITLE, ClipKind.SUBTITLE),
    }

    def __init__(self, timeline: Timeline) -> None:
        if not isinstance(timeline, Timeline):
            raise TypeError("timeline must be a Timeline")
        self.timeline = timeline
        self.assignments = AssetAssignmentEngine(timeline)

    def _track(self, name: str, kind: TrackKind) -> Track:
        track = self.timeline.get_track(name)
        if track is None:
            track = self.timeline.add_track(Track(name=name, kind=kind))
        elif track.kind is not kind:
            raise ClipMaterializationError(
                f"track {name!r} has kind {track.kind.value}, expected {kind.value}"
            )
        return track

    def _clip_id(self, scene_id: str, asset_id: str) -> str:
        value = f"{self.timeline.id}:{scene_id}:{asset_id}:{self.GENERATED_BY}"
        return uuid5(NAMESPACE_URL, value).hex

    def _remove_generated_clips(self) -> set[str]:
        removed: set[str] = set()
        for track in self.timeline.tracks:
            retained = []
            for clip in track.clips:
                if clip.metadata.get("generated_by") == self.GENERATED_BY:
                    removed.add(clip.id)
                else:
                    retained.append(clip)
            track.clips = retained
        for scene in self.timeline.scenes:
            scene.clip_ids = [clip_id for clip_id in scene.clip_ids if clip_id not in removed]
        return removed

    def _build_clip(self, scene, asset: Asset) -> Clip:
        if asset.status is not AssetStatus.ASSIGNED:
            raise ClipMaterializationError(
                f"asset {asset.id} is not assigned (status: {asset.status.value})"
            )
        if not asset.path:
            raise ClipMaterializationError(f"assigned asset has no path: {asset.id}")

        _track_name, _track_kind, clip_kind = self.TRACKS[asset.kind]
        transition_name = str(scene.metadata.get("transition", "cut") or "cut")
        transition = None if transition_name == "cut" else Transition(name=transition_name)

        return Clip(
            id=self._clip_id(scene.id, asset.id),
            kind=clip_kind,
            start=scene.start,
            duration=scene.duration,
            source=asset.path,
            name=Path(asset.path).name,
            transition_in=transition,
            metadata={
                "generated_by": self.GENERATED_BY,
                "asset_id": asset.id,
                "scene_id": scene.id,
                "asset_kind": asset.kind.value,
                "asset_duration": asset.duration,
                "source": asset.source,
                "credit": asset.credit,
                "license": asset.license,
                **asset.metadata,
            },
        )

    def materialize(self) -> list[Clip]:
        """Replace previously generated clips and return the current clip set."""
        validation_issues = self.assignments.validate()
        if validation_issues:
            raise ClipMaterializationError("; ".join(validation_issues))

        self._remove_generated_clips()
        created: list[Clip] = []

        for scene in self.timeline.scenes:
            for asset in self.assignments.assets_for_scene(scene.id):
                if asset.status is not AssetStatus.ASSIGNED:
                    continue
                track_name, track_kind, _clip_kind = self.TRACKS[asset.kind]
                track = self._track(track_name, track_kind)
                clip = self._build_clip(scene, asset)
                track.add_clip(clip)
                if clip.id not in scene.clip_ids:
                    scene.clip_ids.append(clip.id)
                created.append(clip)

        return created


def materialize_timeline_clips(timeline: Timeline) -> list[Clip]:
    """Convenience entry point for workflow and export services."""
    return TimelineClipMaterializer(timeline).materialize()


__all__ = [
    "ClipMaterializationError",
    "TimelineClipMaterializer",
    "materialize_timeline_clips",
]
