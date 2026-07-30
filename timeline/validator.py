"""Validation rules for timeline models."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .models import ClipKind, Timeline


@dataclass(frozen=True, slots=True)
class ValidationIssue:
    code: str
    message: str
    location: str = "timeline"


class TimelineValidationError(ValueError):
    def __init__(self, issues: list[ValidationIssue]) -> None:
        self.issues = issues
        super().__init__("; ".join(issue.message for issue in issues))


class TimelineValidator:
    """Check structural integrity and optionally verify referenced media."""

    SOURCE_REQUIRED = {ClipKind.IMAGE, ClipKind.VIDEO, ClipKind.AUDIO}

    def validate(
        self,
        timeline: Timeline,
        *,
        media_root: str | Path | None = None,
        raise_on_error: bool = False,
    ) -> list[ValidationIssue]:
        issues: list[ValidationIssue] = []
        clip_ids: set[str] = set()
        root = Path(media_root) if media_root is not None else None

        for track in timeline.tracks:
            previous_end = 0.0
            for clip in track.clips:
                location = f"track:{track.name}/clip:{clip.id}"
                if clip.id in clip_ids:
                    issues.append(ValidationIssue("duplicate_clip_id", "clip IDs must be unique", location))
                clip_ids.add(clip.id)

                if clip.start < previous_end:
                    issues.append(
                        ValidationIssue(
                            "overlapping_clips",
                            f"clips overlap on track {track.name!r}",
                            location,
                        )
                    )
                previous_end = max(previous_end, clip.end)

                if clip.kind in self.SOURCE_REQUIRED and not clip.source:
                    issues.append(ValidationIssue("missing_source", "media clip has no source", location))
                elif root is not None and clip.source and clip.kind in self.SOURCE_REQUIRED:
                    source = Path(clip.source)
                    candidate = source if source.is_absolute() else root / source
                    if not candidate.exists():
                        issues.append(
                            ValidationIssue(
                                "source_not_found",
                                f"media source does not exist: {clip.source}",
                                location,
                            )
                        )

        scene_ids: set[str] = set()
        previous_scene_end = 0.0
        for scene in timeline.scenes:
            location = f"scene:{scene.id}"
            if scene.id in scene_ids:
                issues.append(ValidationIssue("duplicate_scene_id", "scene IDs must be unique", location))
            scene_ids.add(scene.id)
            if scene.start < previous_scene_end:
                issues.append(ValidationIssue("overlapping_scenes", "scenes overlap", location))
            previous_scene_end = max(previous_scene_end, scene.end)
            for clip_id in scene.clip_ids:
                if clip_id not in clip_ids:
                    issues.append(
                        ValidationIssue(
                            "unknown_scene_clip",
                            f"scene references unknown clip ID: {clip_id}",
                            location,
                        )
                    )

        if raise_on_error and issues:
            raise TimelineValidationError(issues)
        return issues
