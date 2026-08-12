"""Orchestrate timeline preparation, portable packaging, FCPXML, and live Resolve creation."""
from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping
from common.fcpxml_export import FCPXMLExportResult
from common.resolve_export_v2 import export_resolve_free_v2
from common.resolve_live import LiveResolveResult, LiveResolveService
from common.resolve_portable_package import PortableResolvePackageResult, export_portable_resolve_package
from timeline import ProjectTimelineStore, Timeline, materialize_timeline_clips
import hashlib
import shutil
import subprocess
from timeline import ClipKind
import uuid
import textwrap
import re

ProgressCallback = Callable[[str, float, str], None]


class ResolveProductionError(RuntimeError):
    """Raised when the one-click Resolve production workflow cannot complete."""

def _media_duration(path: Path) -> float:
    ffprobe = shutil.which("ffprobe")
    if not ffprobe:
        raise ResolveProductionError("FFprobe was not found in PATH")

    result = subprocess.run(
        [
            ffprobe,
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        capture_output=True,
        text=True,
        timeout=30,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )

    if result.returncode != 0:
        raise ResolveProductionError(
            f"Could not read media duration: {path.name}"
        )

    try:
        return float(result.stdout.strip())
    except ValueError as error:
        raise ResolveProductionError(
            f"Invalid media duration returned for: {path.name}"
        ) from error


def _sync_visuals_to_narration(timeline: Timeline) -> None:
    narration_clip = None

    for track in timeline.tracks:
        track_kind = str(
            getattr(track.kind, "value", track.kind)
        ).lower()

        if track_kind != "audio":
            continue

        for clip in track.clips:
            if clip.source:
                narration_clip = clip
                break

        if narration_clip is not None:
            break

    if narration_clip is None:
        return

    narration_path = Path(narration_clip.source).expanduser().resolve()
    
    if narration_path.name == "narration_with_fact_unlocked.wav":
        raise ResolveProductionError(
            "The export-only combined narration was reused as the source narration. "
            "The project timeline should point to the original narration file."
        )
    
    if not narration_path.is_file():
        raise ResolveProductionError(
            f"Narration file does not exist: {narration_path}"
        )

    narration_duration = _media_duration(narration_path)

    visual_clips = []

    for track in timeline.tracks:
        track_kind = str(
            getattr(track.kind, "value", track.kind)
        ).lower()

        if track_kind != "video":
            continue

        for clip in track.clips:
            if clip.kind in {ClipKind.IMAGE, ClipKind.VIDEO}:
                visual_clips.append(clip)

    if not visual_clips:
        narration_clip.duration = narration_duration
        return

    original_duration = max(
        float(clip.start) + float(clip.duration)
        for clip in visual_clips
    )

    if original_duration <= 0:
        return

    scale = narration_duration / original_duration

    for clip in visual_clips:
        clip.start = float(clip.start) * scale
        clip.duration = float(clip.duration) * scale

    for scene in timeline.scenes:
        scene.start = float(scene.start) * scale
        scene.duration = float(scene.duration) * scale

    narration_clip.start = 0.0
    narration_clip.source_in = 0.0
    narration_clip.duration = narration_duration
    
def _logo_path() -> Path:
    path = Path(__file__).resolve().parent.parent / "assets" / "facts_logo.png"

    if not path.is_file():
        raise ResolveProductionError(
            f"Logo file does not exist: {path}"
        )

    return path

def _cleanup_unused_resolve_clips(
    timeline: Timeline,
    project_folder: Path,
) -> None:
    output_folder = project_folder / "ResolveClips"

    if not output_folder.is_dir():
        return

    output_folder_resolved = output_folder.resolve()

    used_paths: set[Path] = set()

    for track in timeline.tracks:
        for clip in track.clips:
            if not clip.source:
                continue

            try:
                source = Path(
                    clip.source
                ).expanduser().resolve()
            except OSError:
                continue

            if source.parent == output_folder_resolved:
                used_paths.add(source)

    # Remove obsolete generated scene videos.
    for path in output_folder.glob("scene_*.mp4"):
        try:
            resolved = path.resolve()
        except OSError:
            continue

        if resolved not in used_paths:
            path.unlink(missing_ok=True)

    # Remove caption files belonging to clips that are no longer used.
    used_caption_ids = {
        str(clip.id)
        for track in timeline.tracks
        for clip in track.clips
    }

    for path in output_folder.glob("caption_*.txt"):
        name = path.stem

        if not any(
            clip_id in name
            for clip_id in used_caption_ids
        ):
            path.unlink(missing_ok=True)

def _wrap_caption(value: str, width: int = 24) -> str:
    text = str(value or "").strip()

    if not text:
        return ""

    lines = textwrap.wrap(
        text,
        width=width,
        break_long_words=False,
        break_on_hyphens=False,
    )

    return "\n".join(lines)
    
def _remove_emojis(value: str) -> str:
    text = str(value or "")

    emoji_pattern = re.compile(
        "["
        "\U0001F1E6-\U0001F1FF"  # flags
        "\U0001F300-\U0001F5FF"  # symbols and pictographs
        "\U0001F600-\U0001F64F"  # emoticons
        "\U0001F680-\U0001F6FF"  # transport and map
        "\U0001F700-\U0001F77F"
        "\U0001F780-\U0001F7FF"
        "\U0001F800-\U0001F8FF"
        "\U0001F900-\U0001F9FF"
        "\U0001FA00-\U0001FAFF"
        "\U00002700-\U000027BF"  # dingbats
        "\U00002600-\U000026FF"  # miscellaneous symbols
        "\u200d"                 # zero-width joiner
        "\ufe0f"                 # variation selector
        "]+",
        flags=re.UNICODE,
    )

    cleaned = emoji_pattern.sub("", text)

    return " ".join(cleaned.split())
    
def _add_fact_unlocked_outro(
    timeline: Timeline,
    project_folder: Path,
) -> Timeline:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise ResolveProductionError("FFmpeg was not found in PATH")

    outro_audio = project_folder / "Voice" / "fact_unlocked.mp3"

    if not outro_audio.is_file():
        raise ResolveProductionError(
            "Fact unlocked audio was not found. "
            "Run the voice stage again first."
        )

    payload = timeline.to_dict()

    audio_track = next(
        (
            track
            for track in payload["tracks"]
            if track.get("kind") == "audio"
        ),
        None,
    )

    if audio_track is None or not audio_track.get("clips"):
        raise ResolveProductionError(
            "Narration audio clip was not found."
        )

    narration_clip = audio_track["clips"][0]
    narration_path = Path(
        narration_clip["source"]
    ).expanduser().resolve()

    if not narration_path.is_file():
        raise ResolveProductionError(
            f"Narration file does not exist: {narration_path}"
        )

    narration_duration = _media_duration(narration_path)
    outro_voice_duration = _media_duration(outro_audio)

    # The logo begins as soon as the narration finishes.
    outro_start = narration_duration

    # Keep the logo visible until the outro voice has finished,
    # plus a short closing pause.
    outro_duration = outro_voice_duration + 0.35

    output_folder = project_folder / "ResolveClips"
    output_folder.mkdir(parents=True, exist_ok=True)

    combined_audio = (
        output_folder / "narration_with_fact_unlocked.wav"
    )

    command = [
        ffmpeg,
        "-y",
        "-i",
        str(narration_path),
        "-i",
        str(outro_audio),
        "-filter_complex",
        (
            "[0:a]"
            "aresample=48000,"
            "aformat=sample_fmts=fltp:channel_layouts=stereo"
            "[narration];"

            "[1:a]"
            "aresample=48000,"
            "aformat=sample_fmts=fltp:channel_layouts=stereo"
            "[outro];"

            "[narration][outro]"
            "concat=n=2:v=0:a=1[combined];"

            "[combined]"
            "loudnorm=I=-13:TP=-1.0:LRA=7:"
            "dual_mono=true"
            "[audio]"
        ),
        "-map",
        "[audio]",
        "-c:a",
        "pcm_s16le",
        "-ar",
        "48000",
        str(combined_audio),
    ]

    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        timeout=120,
        creationflags=getattr(
            subprocess, "CREATE_NO_WINDOW", 0
        ),
    )

    if result.returncode != 0:
        combined_audio.unlink(missing_ok=True)
        raise ResolveProductionError(
            "Could not combine narration and outro audio:\n"
            + (
                result.stderr.strip()
                or "Unknown FFmpeg error"
            )
        )

    combined_duration = _media_duration(combined_audio)

    # Replace the original narration with the combined audio.
    narration_clip["source"] = str(combined_audio.resolve())
    narration_clip["start"] = 0.0
    narration_clip["source_in"] = 0.0
    narration_clip["duration"] = combined_duration
    narration_clip["name"] = "Narration and Fact Unlocked"

    # Remove any previously added separate outro audio clips.
    audio_track["clips"] = [narration_clip]

    fps = float(timeline.frame_rate)

    outro_video = _create_outro_video(
        project_folder,
        outro_duration,
        fps,
    )

    video_track = next(
        (
            track
            for track in payload["tracks"]
            if track.get("kind") == "video"
            and track.get("name") == "Visuals"
        ),
        None,
    )

    if video_track is None:
        video_track = next(
            track
            for track in payload["tracks"]
            if track.get("kind") == "video"
        )

    # Remove an old outro before adding the corrected one.
    video_track["clips"] = [
        clip
        for clip in video_track["clips"]
        if clip.get("name") != "Fact Unlocked Outro"
    ]

    video_track["clips"].append(
        {
            "id": uuid.uuid4().hex,
            "kind": "video",
            "start": outro_start,
            "duration": outro_duration,
            "source": str(outro_video.resolve()),
            "name": "Fact Unlocked Outro",
            "source_in": 0.0,
            "transition_in": None,
            "transition_out": None,
            "metadata": {
                "branding": True,
            },
        }
    )

    return Timeline.from_dict(payload)
    
def _create_outro_video(
    project_folder: Path,
    duration: float,
    fps: float,
) -> Path:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise ResolveProductionError("FFmpeg was not found in PATH")

    logo = _logo_path()
    output_folder = project_folder / "ResolveClips"
    output_folder.mkdir(parents=True, exist_ok=True)

    frame_count = max(1, round(duration * fps))
    exact_duration = frame_count / fps
    destination = output_folder / "fact_unlocked_outro.mp4"

    command = [
        ffmpeg,
        "-y",
        "-f",
        "lavfi",
        "-i",
        f"color=c=black:s=1080x1920:r={fps}",
        "-i",
        str(logo),
        "-filter_complex",
        (
            "[1:v]format=rgba,"
            "scale=850:-1[logo];"
            "[0:v][logo]"
            "overlay=(W-w)/2:(H-h)/2,"
            "format=yuv420p"
        ),
        "-frames:v",
        str(frame_count),
        "-c:v",
        "libx264",
        "-preset",
        "fast",
        "-pix_fmt",
        "yuv420p",
        "-movflags",
        "+faststart",
        "-an",
        str(destination),
    ]

    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        timeout=120,
        creationflags=getattr(
            subprocess, "CREATE_NO_WINDOW", 0
        ),
    )

    if result.returncode != 0:
        destination.unlink(missing_ok=True)
        raise ResolveProductionError(
            "Could not create branded outro:\n"
            + (result.stderr.strip() or "Unknown FFmpeg error")
        )

    return destination
    
def _escape_drawtext(value: str) -> str:
    # Straight apostrophes inside FFmpeg single-quoted drawtext values can break
    # the filtergraph quoting state. Normalize them to a visually equivalent
    # typographic apostrophe before applying the ordinary filter escaping.
    text = str(value or "").strip().replace("'", "’")

    replacements = {
        "\\": r"\\",
        ":": r"\:",
        "%": r"\%",
        "[": r"\[",
        "]": r"\]",
        ",": r"\,",
        ";": r"\;",
    }

    for old, new in replacements.items():
        text = text.replace(old, new)

    return text.replace("\n", r"\n")


def _parse_onscreen_text(value: str) -> list[tuple[float, float, str]]:
    """Parse entries such as '0–3 sec' followed by caption text."""
    text = str(value or "").replace("\r\n", "\n").strip()
    if not text:
        return []

    pattern = re.compile(
        r"(?m)^\s*(\d+(?:\.\d+)?)\s*[–—-]\s*"
        r"(\d+(?:\.\d+)?)\s*(?:sec|secs|seconds?)?\s*$"
    )

    matches = list(pattern.finditer(text))
    entries: list[tuple[float, float, str]] = []

    for index, match in enumerate(matches):
        caption_start = match.end()
        caption_end = (
            matches[index + 1].start()
            if index + 1 < len(matches)
            else len(text)
        )

        caption = text[caption_start:caption_end].strip()

        if caption:
            entries.append(
                (
                    float(match.group(1)),
                    float(match.group(2)),
                    caption,
                )
            )

    return entries


def _captions_for_clip(
    clip_start: float,
    clip_duration: float,
    entries: list[tuple[float, float, str]],
) -> list[tuple[float, float, str]]:
    """Return captions overlapping a clip, using clip-local timestamps."""
    clip_end = clip_start + clip_duration
    captions: list[tuple[float, float, str]] = []

    for caption_start, caption_end, caption_text in entries:
        overlap_start = max(clip_start, caption_start)
        overlap_end = min(clip_end, caption_end)

        if overlap_end <= overlap_start:
            continue

        local_start = overlap_start - clip_start
        local_end = max(
            local_start,
            overlap_end - clip_start - (1.0 / 30.0),
        )

        captions.append(
            (
                local_start,
                local_end,
                caption_text,
            )
        )

    return captions
    
def _convert_stills_to_video(
    timeline: Timeline,
    project_folder: Path,
    onscreen_text: str = "",
    caption_end_limit: float | None = None,
) -> None:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise ResolveProductionError("FFmpeg was not found in PATH")

    output_folder = project_folder / "ResolveClips"
    output_folder.mkdir(parents=True, exist_ok=True)

    fps = float(timeline.frame_rate)

    caption_entries = _parse_onscreen_text(onscreen_text)

    if caption_end_limit is not None:
        limited_entries: list[tuple[float, float, str]] = []

        for start, end, text in caption_entries:
            if start >= caption_end_limit:
                continue

            limited_entries.append(
                (
                    start,
                    min(end, caption_end_limit),
                    text,
                )
            )

        caption_entries = limited_entries
    
    font_candidates = [
        Path("C:/Windows/Fonts/arialbd.ttf"),
        Path("C:/Windows/Fonts/seguisb.ttf"),
        Path("C:/Windows/Fonts/segoeui.ttf"),
        Path("C:/Windows/Fonts/arial.ttf"),
    ]

    font_path = next(
        (path for path in font_candidates if path.is_file()),
        None,
    )

    if font_path is None:
        raise ResolveProductionError(
            "A compatible Windows caption font could not be found."
        )
    motion_index = 0
    for track in timeline.tracks:
        for clip in track.clips:
            if clip.kind != ClipKind.IMAGE or not clip.source:
                continue

            source = Path(clip.source).expanduser().resolve()

            if not source.is_file():
                raise ResolveProductionError(
                    f"Image does not exist: {source}"
                )

            duration = max(0.1, float(clip.duration))
            frame_count = max(1, round(duration * fps))
            exact_duration = frame_count / fps

            clip_start = float(clip.start)
    
            motion_type = motion_index % 4
            motion_index += 1

            progress_expr = f"on/{max(1, frame_count - 1)}"

            if motion_type == 0:
                zoom_expr = f"1+0.10*{progress_expr}"
                x_expr = "iw/2-(iw/zoom/2)"
                y_expr = "ih/2-(ih/zoom/2)"

            elif motion_type == 1:
                zoom_expr = f"1.10-0.10*{progress_expr}"
                x_expr = "iw/2-(iw/zoom/2)"
                y_expr = "ih/2-(ih/zoom/2)"

            elif motion_type == 2:
                zoom_expr = "1.08"
                x_expr = f"(iw-iw/zoom)*{progress_expr}"
                y_expr = "ih/2-(ih/zoom/2)"

            else:
                zoom_expr = "1.08"
                x_expr = f"(iw-iw/zoom)*(1-{progress_expr})"
                y_expr = "ih/2-(ih/zoom/2)"
                
            clip_captions = _captions_for_clip(
                clip_start,
                exact_duration,
                caption_entries,
            )

            caption_filters: list[str] = []
            caption_identity_parts: list[str] = []

            font_file_filter = (
                str(font_path.resolve())
                .replace("\\", "/")
                .replace(":", r"\:")
            )

            for caption_index, (
                local_start,
                local_end,
                caption_text,
            ) in enumerate(clip_captions):
                caption_text = _remove_emojis(caption_text)
                caption_text = _wrap_caption(caption_text, width=24)
                caption_text_filter = _escape_drawtext(caption_text)

                input_label = (
                    "branded"
                    if caption_index == 0
                    else f"captioned{caption_index}"
                )

                output_label = f"captioned{caption_index + 1}"

                caption_filters.append(
                    f"[{input_label}]"
                    f"drawtext=fontfile='{font_file_filter}':"
                    f"text='{caption_text_filter}':"
                    "fontcolor=white:"
                    "fontsize=76:"
                    "line_spacing=12:"
                    "borderw=5:"
                    "bordercolor=black:"
                    "box=1:"
                    "boxcolor=black@0.45:"
                    "boxborderw=22:"
                    "x=(w-text_w)/2:"
                    "y=120:"
                    f"enable='between(t\\,{local_start:.3f}\\,{local_end:.3f})'"
                    f"[{output_label}];"
                )

                caption_identity_parts.append(
                    f"{local_start:.3f}-"
                    f"{local_end:.3f}-"
                    f"{caption_text}"
                )

            caption_filter_chain = "".join(caption_filters)

            final_caption_label = (
                f"captioned{len(clip_captions)}"
                if clip_captions
                else "branded"
            )

            caption_identity = "|".join(caption_identity_parts)

            identity = (
                f"{source}|{source.stat().st_mtime_ns}|"
                f"{clip_start:.3f}|"
                f"{duration:.3f}|{fps:.3f}|"
                f"{caption_identity}|"
                f"{caption_end_limit}|"
                f"motion={motion_type}|"
                "1080x1920-wrap-audio-motion-v32"
            )

            digest = hashlib.sha256(
                identity.encode("utf-8")
            ).hexdigest()[:12]

            destination = output_folder / f"scene_{digest}.mp4"

            if (
                not destination.is_file()
                or destination.stat().st_size == 0
            ):
                logo = _logo_path()

                command = [
                    ffmpeg,
                    "-y",
                    "-i",
                    str(source),
                    "-i",
                    str(logo),
                    "-filter_complex",
                    (
                        "[0:v]"
                        "format=rgba,"
                        "scale=1200:2134:"
                        "force_original_aspect_ratio=increase,"
                        "crop=1080:1920,"
                        f"zoompan="
                        f"z='{zoom_expr}':"
                        f"x='{x_expr}':"
                        f"y='{y_expr}':"
                        f"d={frame_count}:"
                        "s=1080x1920:"
                        f"fps={fps}[scene];"

                        "[1:v]"
                        "format=rgba,"
                        "scale=190:-1[logo];"

                        "[scene][logo]"
                        "overlay=W-w-35:H-h-35:"
                        "repeatlast=1:"
                        "shortest=0[branded];"

                        f"{caption_filter_chain}"

                        f"[{final_caption_label}]"
                        "format=yuv420p[out]"
                    ),
                    "-map",
                    "[out]",
                    "-frames:v",
                    str(frame_count),
                    "-r",
                    str(fps),
                    "-c:v",
                    "libx264",
                    "-preset",
                    "veryfast",
                    "-profile:v",
                    "high",
                    "-level",
                    "4.1",
                    "-pix_fmt",
                    "yuv420p",
                    "-movflags",
                    "+faststart",
                    "-an",
                    str(destination),
                ]

                try:
                    result = subprocess.run(
                        command,
                        capture_output=True,
                        text=True,
                        timeout=300,
                        creationflags=getattr(
                            subprocess, "CREATE_NO_WINDOW", 0
                        ),
                    )
                except subprocess.TimeoutExpired as error:
                    destination.unlink(missing_ok=True)
                    raise ResolveProductionError(
                        f"FFmpeg timed out converting: {source.name}"
                    ) from error

                if result.returncode != 0:
                    destination.unlink(missing_ok=True)
                    raise ResolveProductionError(
                        "FFmpeg conversion failed:\n"
                        + (
                            result.stderr.strip()
                            or "Unknown FFmpeg error"
                        )
                    )

            clip.source = str(destination.resolve())
            clip.kind = ClipKind.VIDEO
            clip.source_in = 0.0
            clip.duration = exact_duration
            
@dataclass(frozen=True)
class ResolveProductionResult:
    project_folder: Path
    timeline_path: Path
    package: PortableResolvePackageResult
    fcpxml: FCPXMLExportResult
    launched: bool
    command: tuple[str, ...] | None
    warnings: tuple[str, ...]
    live: LiveResolveResult | None = None


class ResolveProductionService:
    """Prepare a self-contained portable package and Resolve Free timeline."""

    def __init__(
        self,
        *,
        live_service: LiveResolveService | None = None,
        process_runner: Callable[..., Any] | None = None,
        python_executable: str | None = None,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self.live_service = live_service or LiveResolveService()
        self.legacy_runner = process_runner
        self.python_executable = python_executable or sys.executable
        self.progress_callback = progress_callback

    def _progress(self, stage: str, fraction: float, message: str) -> None:
        if self.progress_callback is not None:
            self.progress_callback(stage, fraction, message)

    @staticmethod
    def _project_title(project: Mapping[str, Any]) -> str:
        return str(project.get("title") or "Fact Vault Video")

    def run(
        self,
        project: Mapping[str, Any],
        project_folder: str | Path,
        settings: Mapping[str, Any],
        *,
        timeline: Timeline | None = None,
        materialize: bool = True,
        strict: bool = True,
        overwrite: bool = True,
        launch: bool = False,
    ) -> ResolveProductionResult:
        if not isinstance(project, Mapping):
            raise TypeError("project must be a mapping")
        if not isinstance(settings, Mapping):
            raise TypeError("settings must be a mapping")
        folder = Path(project_folder)
        if not folder.is_dir():
            raise FileNotFoundError(f"Project folder could not be found: {folder}")

        store = ProjectTimelineStore(folder)
        self._progress("timeline", 0.1, "Loading project timeline")
        current = timeline or store.ensure(
            self._project_title(project),
            width=int(settings.get("timeline_width", 1080)),
            height=int(settings.get("timeline_height", 1920)),
            frame_rate=float(settings.get("frame_rate", 30)),
        )
        if not isinstance(current, Timeline):
            raise TypeError("timeline must be a Timeline")
        current.width = 1080
        current.height = 1920

        if materialize:
            self._progress("timeline", 0.25, "Materializing assigned assets")
            materialize_timeline_clips(current)

        # Save the real project timeline before making export-only changes.
        timeline_path = store.save(current)

        # Work on a separate copy for Resolve export.
        export_timeline = Timeline.from_dict(current.to_dict())
        _sync_visuals_to_narration(export_timeline)
        _close_visual_gaps(export_timeline)

        narration_duration = None

        for track in export_timeline.tracks:
            track_kind = str(
                getattr(track.kind, "value", track.kind)
            ).lower()

            if track_kind != "audio":
                continue

            for clip in track.clips:
                if clip.source:
                    narration_duration = _media_duration(
                        Path(clip.source).expanduser().resolve()
                    )
                    break

            if narration_duration is not None:
                break
        
        onscreen_text = str(
            project.get("onscreen_text")
            or project.get("on_screen_text")
            or project.get("on-screen_text")
            or project.get("On-Screen Text")
            or ""
        )

        self._progress(
            "timeline",
            0.35,
            "Converting still images into Resolve-compatible video clips",
        )

        caption_end_limit = (
            max(0.0, narration_duration - 1.25)
            if narration_duration is not None
            else None
        )

        _convert_stills_to_video(
            export_timeline,
            folder,
            onscreen_text,
            caption_end_limit=caption_end_limit,
        )

        export_timeline = _add_fact_unlocked_outro(
            export_timeline,
            folder,
        )

        self._progress("package", 0.48, "Building self-contained Resolve package")
        package = export_portable_resolve_package(
            project,
            folder,
            dict(settings),
            export_timeline,
            strict=strict,
            overwrite=overwrite,
        )

        self._progress("fcpxml", 0.68, "Creating validated Resolve Free timeline")
        fcpxml_path = package.package_folder / f"{self._project_title(project)}.fcpxml"
        export_v2 = export_resolve_free_v2(
            export_timeline,
            package,
            fcpxml_path,
        )
        fcpxml = export_v2.fcpxml

        _cleanup_unused_resolve_clips(
            export_timeline,
            folder,
        )

        readme = package.package_folder / "IMPORT_IN_RESOLVE_FREE.txt"
        readme.write_text(
            "DaVinci Resolve Free Import\n"
            "===========================\n\n"
            "1. Keep this entire Portable package folder together.\n"
            "2. Open DaVinci Resolve and create or open a project.\n"
            "3. Choose File > Import > Timeline.\n"
            f"4. Select {fcpxml.path.name}.\n"
            "5. The FCPXML references only files inside this package's Media folder.\n"
            f"6. Validated media files: {len(export_v2.validated_media)}.\n",
            encoding="utf-8",
        )

        live = None
        command = None
        launched = False
        if launch and self.legacy_runner is not None:
            runner = package.package_folder / "build_resolve_timeline.py"
            command = (self.python_executable, str(runner))
            self._progress("launch", 0.82, "Launching Resolve timeline builder")
            try:
                self.legacy_runner(command, cwd=package.package_folder)
            except OSError as error:
                raise ResolveProductionError(f"Could not launch Resolve builder: {error}") from error
            launched = True
        elif launch:
            self._progress("launch", 0.82, "Connecting to DaVinci Resolve Studio")
            try:
                live = self.live_service.build_package(package.package_folder, settings, launch_if_needed=True)
            except Exception as error:
                raise ResolveProductionError(
                    "Live Resolve scripting is unavailable. Import the generated FCPXML in Resolve Free instead: "
                    f"{fcpxml.path}. Details: {error}"
                ) from error
            launched = True

        warnings = list(package.warnings)
        if live is not None:
            warnings.extend(live.warnings)
        self._progress("complete", 1.0, "Validated Resolve Free export is ready")
        return ResolveProductionResult(
            project_folder=folder,
            timeline_path=timeline_path,
            package=package,
            fcpxml=fcpxml,
            launched=launched,
            command=command,
            warnings=tuple(warnings),
            live=live,
        )

def _close_visual_gaps(timeline: Timeline) -> None:
    """Ensure the combined visual sequence contains no empty sections."""
    visual_clips = []

    for track in timeline.tracks:
        track_kind = str(
            getattr(track.kind, "value", track.kind)
        ).lower()

        if track_kind != "video":
            continue

        for clip in track.clips:
            if clip.kind not in {ClipKind.IMAGE, ClipKind.VIDEO}:
                continue

            if not clip.source:
                continue

            visual_clips.append(clip)

    visual_clips.sort(key=lambda clip: float(clip.start))

    if not visual_clips:
        return

    # Prevent a blank section at the beginning.
    first = visual_clips[0]
    if float(first.start) > 0:
        first.duration = float(first.duration) + float(first.start)
        first.start = 0.0

    # Extend each visual precisely to the beginning of the next one.
    for current_clip, next_clip in zip(
        visual_clips,
        visual_clips[1:],
    ):
        current_start = float(current_clip.start)
        next_start = float(next_clip.start)

        required_duration = max(
            0.0,
            next_start - current_start,
        )

        if required_duration > float(current_clip.duration):
            current_clip.duration = required_duration

    # Extend the final visual through the end of the timeline.
    last = visual_clips[-1]
    required_duration = max(
        0.0,
        float(timeline.duration) - float(last.start),
    )

    if required_duration > float(last.duration):
        last.duration = required_duration
            
def build_resolve_production(project, project_folder, settings, **options):
    return ResolveProductionService().run(project, project_folder, settings, **options)


def make_resolve_workflow_service(project_folder, settings, *, service=None, **options):
    producer = service or ResolveProductionService()

    def run(context):
        project = context.get("project")
        if not isinstance(project, Mapping):
            raise ResolveProductionError("workflow context does not contain a project mapping")
        return producer.run(project, project_folder, settings, **options)

    return run


__all__ = [
    "ResolveProductionError", "ResolveProductionResult", "ResolveProductionService",
    "build_resolve_production", "make_resolve_workflow_service",
]
