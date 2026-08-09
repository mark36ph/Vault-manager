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


def _file_url(path: Path, *, relative_to: Path | None = None) -> str:
    """Return an FCPXML media URI, optionally relative to a portable package."""
    resolved = path.resolve()
    if relative_to is None:
        return resolved.as_uri()

    base = relative_to.resolve()
    try:
        relative = resolved.relative_to(base)
    except ValueError as error:
        raise FCPXMLExportError(
            f"clip source is outside the requested portable media base: {resolved}"
        ) from error

    return quote(relative.as_posix(), safe="/")


def export_fcpxml(
    timeline: Timeline,
    destination: str | Path,
    *,
    media_base: str | Path | None = None,
) -> FCPXMLExportResult:
    """Write an FCPXML 1.10 timeline that Resolve Free can import."""
    if not isinstance(timeline, Timeline):
        raise TypeError("timeline must be a Timeline")
    if timeline.duration <= 0:
        raise FCPXMLExportError("timeline must contain at least one timed item")

    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    base_path = Path(media_base) if media_base is not None else None
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
    next_asset_id = 2
    for _track, clip, source in media_clips:
        key = str(source)
        if key in asset_ids:
            continue
        asset_id = f"r{next_asset_id}"
        next_asset_id += 1
        asset_ids[key] = asset_id
        attrs = {
            "id": asset_id,
            "name": clip.name or source.name,
            "src": _file_url(source, relative_to=base_path),
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
        gap_frames = round(
            (float(clip.start) - float(cursor)) * fps
        )

        if gap_frames > 0:
            ET.SubElement(
                spine,
                "gap",
                name="Gap",
                offset=_time(cursor, fps),
                start="0s",
                duration=f"{gap_frames}/{round(fps)}s",
            )
        attrs = {
            "name": clip.name or source.name,
            "ref": asset_ids[str(source)],
            "offset": _time(clip.start, fps),
            "start": _time(clip.source_in, fps),
            "duration": _time(clip.duration, fps),
        }
        ET.SubElement(spine, "asset-clip", attrs)
        clip_end_frames = (
            round(float(clip.start) * fps)
            + round(float(clip.duration) * fps)
        )
        cursor = clip_end_frames / fps
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
