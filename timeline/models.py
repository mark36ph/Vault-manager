"""Serializable domain models used to describe an edit timeline."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any
from uuid import uuid4


class TrackKind(str, Enum):
    VIDEO = "video"
    AUDIO = "audio"
    SUBTITLE = "subtitle"
    MARKER = "marker"


class ClipKind(str, Enum):
    IMAGE = "image"
    VIDEO = "video"
    AUDIO = "audio"
    SUBTITLE = "subtitle"
    MARKER = "marker"


def _id() -> str:
    return uuid4().hex


@dataclass(slots=True)
class Transition:
    name: str = "cut"
    duration: float = 0.0

    def __post_init__(self) -> None:
        if self.duration < 0:
            raise ValueError("transition duration cannot be negative")


@dataclass(slots=True)
class Clip:
    kind: ClipKind
    start: float
    duration: float
    source: str | None = None
    name: str = ""
    source_in: float = 0.0
    transition_in: Transition | None = None
    transition_out: Transition | None = None
    metadata: dict[str, Any] = field(default_factory=dict)
    id: str = field(default_factory=_id)

    def __post_init__(self) -> None:
        self.kind = ClipKind(self.kind)
        if self.start < 0:
            raise ValueError("clip start cannot be negative")
        if self.duration <= 0:
            raise ValueError("clip duration must be greater than zero")
        if self.source_in < 0:
            raise ValueError("clip source_in cannot be negative")

    @property
    def end(self) -> float:
        return self.start + self.duration


@dataclass(slots=True)
class Track:
    kind: TrackKind
    name: str
    clips: list[Clip] = field(default_factory=list)
    id: str = field(default_factory=_id)

    def __post_init__(self) -> None:
        self.kind = TrackKind(self.kind)

    def add_clip(self, clip: Clip) -> Clip:
        self.clips.append(clip)
        self.clips.sort(key=lambda item: (item.start, item.id))
        return clip

    @property
    def duration(self) -> float:
        return max((clip.end for clip in self.clips), default=0.0)


@dataclass(slots=True)
class Scene:
    title: str
    start: float
    duration: float
    narration: str = ""
    clip_ids: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
    id: str = field(default_factory=_id)

    def __post_init__(self) -> None:
        if self.start < 0:
            raise ValueError("scene start cannot be negative")
        if self.duration <= 0:
            raise ValueError("scene duration must be greater than zero")

    @property
    def end(self) -> float:
        return self.start + self.duration


@dataclass(slots=True)
class Timeline:
    name: str
    frame_rate: float = 30.0
    width: int = 1920
    height: int = 1080
    tracks: list[Track] = field(default_factory=list)
    scenes: list[Scene] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
    version: int = 1
    id: str = field(default_factory=_id)

    def __post_init__(self) -> None:
        if self.frame_rate <= 0:
            raise ValueError("frame_rate must be greater than zero")
        if self.width <= 0 or self.height <= 0:
            raise ValueError("timeline dimensions must be greater than zero")

    @property
    def duration(self) -> float:
        track_end = max((track.duration for track in self.tracks), default=0.0)
        scene_end = max((scene.end for scene in self.scenes), default=0.0)
        return max(track_end, scene_end)

    def add_track(self, track: Track) -> Track:
        self.tracks.append(track)
        return track

    def add_scene(self, scene: Scene) -> Scene:
        self.scenes.append(scene)
        self.scenes.sort(key=lambda item: (item.start, item.id))
        return scene

    def get_track(self, name: str) -> Track | None:
        return next((track for track in self.tracks if track.name == name), None)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Timeline":
        payload = dict(data)
        payload["tracks"] = [
            Track(
                id=track.get("id", _id()),
                kind=TrackKind(track["kind"]),
                name=track["name"],
                clips=[
                    Clip(
                        id=clip.get("id", _id()),
                        kind=ClipKind(clip["kind"]),
                        start=clip["start"],
                        duration=clip["duration"],
                        source=clip.get("source"),
                        name=clip.get("name", ""),
                        source_in=clip.get("source_in", 0.0),
                        transition_in=(Transition(**clip["transition_in"]) if clip.get("transition_in") else None),
                        transition_out=(Transition(**clip["transition_out"]) if clip.get("transition_out") else None),
                        metadata=clip.get("metadata", {}),
                    )
                    for clip in track.get("clips", [])
                ],
            )
            for track in payload.get("tracks", [])
        ]
        payload["scenes"] = [Scene(**scene) for scene in payload.get("scenes", [])]
        return cls(**payload)
