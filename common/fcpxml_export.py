"""Export internal timelines as FCPXML for DaVinci Resolve Free."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from urllib.parse import quote
import xml.etree.ElementTree as ET

from timeline import ClipKind, Timeline, TrackKind


class FCPXMLExportError(RuntimeError):
    """Raised when a timeline cannot be exported safely."""


@dataclass(frozen=True)
class FCPXMLExportResult:
    path: Path
    media_count: int
    clip_count: int


def _time(seconds: float, fps: float) -> str:
    frames = max(0, round(float(seconds) * float(fps)))
    return f"{frames}/{round(fps)}s"


def _file_url(path: Path) -> str:
    resolved = path.resolve()
    return "file://localhost/" + quote(resolved.as_posix(), safe="/:~!$&'()*+,;=@")


def export_fcpxml(timeline: Timeline, destination: str | Path) -> FCPXMLExportResult:
    """Write an FCPXML 1.10 timeline that Resolve Free can import."""
    if not isinstance(timeline, Timeline):
        raise TypeError("timeline must be a Timeline")
    if timeline.duration <= 0:
        raise FCPXMLExportError("timeline must contain at least one timed item")

    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    fps = float(timeline.frame_rate)
    frame_duration = _time(1 / fps, fps)

    root = ET.Element("fcpxml", version="1.10")
    resources = ET.SubElement(root, "resources")
    ET.SubElement(
        resources,
        "format",
        id="r1",
        name=f"FFVideoFormat{timeline.height}p{round(fps)}",
        frameDuration=frame_duration,
        width=str(timeline.width),
        height=str(timeline.height),
        colorSpace="1-1-1 (Rec. 709)",
    )

    media_clips = []
    for track in timeline.tracks:
        if track.kind not in {TrackKind.VIDEO, TrackKind.AUDIO}:
            continue
        for clip in track.clips:
            if clip.kind not in {ClipKind.IMAGE, ClipKind.VIDEO, ClipKind.AUDIO} or not clip.source:
                continue
            source = Path(clip.source)
            if not source.is_file():
                raise FCPXMLExportError(f"clip source does not exist: {source}")
            media_clips.append((track, clip, source.resolve()))

    asset_ids: dict[str, str] = {}
    for index, (_track, clip, source) in enumerate(media_clips, start=2):
        key = str(source)
        if key in asset_ids:
            continue
        asset_id = f"r{index}"
        asset_ids[key] = asset_id
        attrs = {
            "id": asset_id,
            "name": clip.name or source.name,
            "src": _file_url(source),
            "start": "0s",
            "duration": _time(max(clip.duration, timeline.duration), fps),
            "hasVideo": "0" if clip.kind == ClipKind.AUDIO else "1",
            "hasAudio": "1" if clip.kind in {ClipKind.AUDIO, ClipKind.VIDEO} else "0",
        }
        ET.SubElement(resources, "asset", attrs)

    library = ET.SubElement(root, "library")
    event = ET.SubElement(library, "event", name="FactVault Exports")
    project = ET.SubElement(event, "project", name=timeline.name)
    sequence = ET.SubElement(
        project,
        "sequence",
        duration=_time(timeline.duration, fps),
        format="r1",
        tcStart="0s",
        tcFormat="NDF",
        audioLayout="stereo",
        audioRate="48k",
    )
    spine = ET.SubElement(sequence, "spine")

    video_items = [(track, clip, source) for track, clip, source in media_clips if track.kind == TrackKind.VIDEO]
    audio_items = [(track, clip, source) for track, clip, source in media_clips if track.kind == TrackKind.AUDIO]

    cursor = 0.0
    clip_count = 0
    for _track, clip, source in sorted(video_items, key=lambda item: item[1].start):
        if clip.start > cursor:
            ET.SubElement(spine, "gap", name="Gap", offset=_time(cursor, fps), start="0s", duration=_time(clip.start - cursor, fps))
        attrs = {
            "name": clip.name or source.name,
            "ref": asset_ids[str(source)],
            "offset": _time(clip.start, fps),
            "start": _time(clip.source_in, fps),
            "duration": _time(clip.duration, fps),
        }
        ET.SubElement(spine, "asset-clip", attrs)
        cursor = max(cursor, clip.start + clip.duration)
        clip_count += 1

    if not video_items:
        ET.SubElement(spine, "gap", name="Primary Storyline", offset="0s", start="0s", duration=_time(timeline.duration, fps))

    for lane, (_track, clip, source) in enumerate(sorted(audio_items, key=lambda item: item[1].start), start=1):
        attrs = {
            "name": clip.name or source.name,
            "ref": asset_ids[str(source)],
            "lane": str(-lane),
            "offset": _time(clip.start, fps),
            "start": _time(clip.source_in, fps),
            "duration": _time(clip.duration, fps),
        }
        ET.SubElement(spine, "asset-clip", attrs)
        clip_count += 1

    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ")
    tree.write(destination, encoding="utf-8", xml_declaration=True)
    return FCPXMLExportResult(destination, len(asset_ids), clip_count)


__all__ = ["FCPXMLExportError", "FCPXMLExportResult", "export_fcpxml"]
