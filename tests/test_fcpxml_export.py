from pathlib import Path
import xml.etree.ElementTree as ET

import pytest

from common.fcpxml_export import FCPXMLExportError, export_fcpxml
from timeline import Clip, ClipKind, Timeline, Track, TrackKind


def make_timeline(tmp_path):
    image = tmp_path / "tower image.jpg"
    audio = tmp_path / "narration.mp3"
    image.write_bytes(b"image")
    audio.write_bytes(b"audio")
    video = Track(kind=TrackKind.VIDEO, name="Visuals")
    video.add_clip(Clip(kind=ClipKind.IMAGE, start=0, duration=3, source=str(image), name="Tower"))
    narration = Track(kind=TrackKind.AUDIO, name="Narration")
    narration.add_clip(Clip(kind=ClipKind.AUDIO, start=0, duration=3, source=str(audio), name="Narration"))
    return Timeline(name="Eiffel Tower", width=1080, height=1920, frame_rate=30, tracks=[video, narration])


def test_exports_importable_fcpxml_structure(tmp_path):
    output = tmp_path / "Resolve" / "tower.fcpxml"
    result = export_fcpxml(make_timeline(tmp_path), output)
    assert result.path == output
    assert result.media_count == 2
    assert result.clip_count == 2
    root = ET.parse(output).getroot()
    assert root.tag == "fcpxml"
    assert root.attrib["version"] == "1.10"
    sequence = root.find("./library/event/project/sequence")
    assert sequence is not None
    assert sequence.attrib["format"] == "r1"


def test_writes_vertical_format_and_file_urls(tmp_path):
    output = tmp_path / "timeline.fcpxml"
    export_fcpxml(make_timeline(tmp_path), output)
    root = ET.parse(output).getroot()
    fmt = root.find("./resources/format")
    assert fmt.attrib["width"] == "1080"
    assert fmt.attrib["height"] == "1920"
    assets = root.findall("./resources/asset")
    assert all(asset.attrib["src"].startswith("file://localhost/") for asset in assets)
    assert any("tower%20image.jpg" in asset.attrib["src"] for asset in assets)


def test_places_narration_on_connected_audio_lane(tmp_path):
    output = tmp_path / "timeline.fcpxml"
    export_fcpxml(make_timeline(tmp_path), output)
    clips = ET.parse(output).getroot().findall("./library/event/project/sequence/spine/asset-clip")
    assert len(clips) == 2
    assert any(clip.attrib.get("lane", "").startswith("-") for clip in clips)


def test_rejects_missing_media(tmp_path):
    track = Track(kind=TrackKind.VIDEO, name="Visuals")
    track.add_clip(Clip(kind=ClipKind.IMAGE, start=0, duration=1, source=str(tmp_path / "missing.jpg")))
    timeline = Timeline(name="Missing", tracks=[track])
    with pytest.raises(FCPXMLExportError, match="does not exist"):
        export_fcpxml(timeline, tmp_path / "missing.fcpxml")


def test_rejects_empty_timeline(tmp_path):
    with pytest.raises(FCPXMLExportError, match="timed item"):
        export_fcpxml(Timeline(name="Empty"), tmp_path / "empty.fcpxml")
