"""Asset models and scene assignment helpers for project timelines."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any
from uuid import uuid4

from .models import Scene, Timeline


class AssetKind(str, Enum):
    IMAGE = "image"
    VIDEO = "video"
    AUDIO = "audio"
    SUBTITLE = "subtitle"


class AssetStatus(str, Enum):
    PENDING = "pending"
    ASSIGNED = "assigned"
    MISSING = "missing"


@dataclass(slots=True)
class Asset:
    kind: AssetKind
    path: str | None = None
    status: AssetStatus = AssetStatus.PENDING
    duration: float | None = None
    source: str | None = None
    credit: str | None = None
    license: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)
    id: str = field(default_factory=lambda: uuid4().hex)

    def __post_init__(self) -> None:
        self.kind = AssetKind(self.kind)
        self.status = AssetStatus(self.status)
        if not self.id or not isinstance(self.id, str):
            raise ValueError("asset id must be a non-empty string")
        if self.duration is not None and self.duration < 0:
            raise ValueError("asset duration cannot be negative")
        if not isinstance(self.metadata, dict):
            raise TypeError("asset metadata must be a dictionary")

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Asset":
        if not isinstance(data, dict):
            raise TypeError("asset data must be a dictionary")
        return cls(**data)


class AssetAssignmentError(ValueError):
    """Raised when an asset assignment cannot be completed safely."""


class AssetAssignmentEngine:
    """Assign, move, remove, and validate assets stored on timeline scenes."""

    METADATA_KEY = "assets"

    def __init__(self, timeline: Timeline) -> None:
        if not isinstance(timeline, Timeline):
            raise TypeError("timeline must be a Timeline")
        self.timeline = timeline

    def _scene(self, scene_id: str) -> Scene:
        scene = next((item for item in self.timeline.scenes if item.id == scene_id), None)
        if scene is None:
            raise AssetAssignmentError(f"unknown scene id: {scene_id}")
        return scene

    def assets_for_scene(self, scene_id: str) -> list[Asset]:
        scene = self._scene(scene_id)
        raw_assets = scene.metadata.get(self.METADATA_KEY, [])
        if not isinstance(raw_assets, list):
            raise AssetAssignmentError(f"scene {scene_id} assets must be a list")
        try:
            return [Asset.from_dict(item) for item in raw_assets]
        except (TypeError, ValueError, KeyError) as error:
            raise AssetAssignmentError(f"scene {scene_id} contains invalid asset data") from error

    def find(self, asset_id: str) -> tuple[Scene, Asset] | None:
        for scene in self.timeline.scenes:
            for asset in self.assets_for_scene(scene.id):
                if asset.id == asset_id:
                    return scene, asset
        return None

    def assign(self, scene_id: str, asset: Asset, *, allow_duplicate: bool = False) -> Asset:
        if not isinstance(asset, Asset):
            raise TypeError("asset must be an Asset")
        existing = self.find(asset.id)
        if existing is not None and not allow_duplicate:
            raise AssetAssignmentError(f"asset id already assigned: {asset.id}")
        scene = self._scene(scene_id)
        assigned = Asset.from_dict(asset.to_dict())
        assigned.status = AssetStatus.ASSIGNED
        assets = self.assets_for_scene(scene_id)
        assets.append(assigned)
        scene.metadata[self.METADATA_KEY] = [item.to_dict() for item in assets]
        return assigned

    def remove(self, scene_id: str, asset_id: str) -> Asset:
        scene = self._scene(scene_id)
        assets = self.assets_for_scene(scene_id)
        for index, asset in enumerate(assets):
            if asset.id == asset_id:
                removed = assets.pop(index)
                scene.metadata[self.METADATA_KEY] = [item.to_dict() for item in assets]
                return removed
        raise AssetAssignmentError(f"asset {asset_id} is not assigned to scene {scene_id}")

    def move(self, asset_id: str, target_scene_id: str) -> Asset:
        found = self.find(asset_id)
        if found is None:
            raise AssetAssignmentError(f"unknown asset id: {asset_id}")
        source_scene, asset = found
        self._scene(target_scene_id)
        if source_scene.id == target_scene_id:
            return asset
        self.remove(source_scene.id, asset_id)
        return self.assign(target_scene_id, asset)

    def validate(self) -> list[str]:
        issues: list[str] = []
        seen: set[str] = set()
        for scene in self.timeline.scenes:
            raw_assets = scene.metadata.get(self.METADATA_KEY, [])
            if not isinstance(raw_assets, list):
                issues.append(f"scene {scene.id}: assets must be a list")
                continue
            for index, raw_asset in enumerate(raw_assets):
                try:
                    asset = Asset.from_dict(raw_asset)
                except (TypeError, ValueError, KeyError) as error:
                    issues.append(f"scene {scene.id}: invalid asset at index {index}: {error}")
                    continue
                if asset.id in seen:
                    issues.append(f"duplicate asset id: {asset.id}")
                seen.add(asset.id)
                if asset.status is AssetStatus.ASSIGNED and not asset.path:
                    issues.append(f"assigned asset has no path: {asset.id}")
        return issues


__all__ = [
    "Asset",
    "AssetAssignmentEngine",
    "AssetAssignmentError",
    "AssetKind",
    "AssetStatus",
]
