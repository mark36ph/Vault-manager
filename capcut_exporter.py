import re
import shutil
from pathlib import Path


IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
AUDIO_EXTENSIONS = {".wav", ".mp3", ".m4a", ".aac", ".ogg", ".flac"}


def _natural_sort_key(path):
    """Sort names containing numbers in the order a person expects."""
    return [
        int(part) if part.isdigit() else part.lower()
        for part in re.split(r"(\d+)", path.name)
    ]


def _project_value(project, key, default=""):
    """Read a project value from sqlite3.Row, dict, or similar mappings."""
    try:
        value = project[key]
    except (KeyError, IndexError, TypeError):
        value = default
    return default if value is None else value


def _caption_lines(text):
    """Turn project text into short caption cues."""
    lines = [line.strip() for line in text.splitlines() if line.strip()]

    if len(lines) > 1:
        return lines

    return [
        sentence.strip()
        for sentence in re.split(r"(?<=[.!?])\s+", text.strip())
        if sentence.strip()
    ]


def _format_srt_time(seconds):
    """Format floating-point seconds as an SRT timestamp."""
    milliseconds = round(seconds * 1000)
    hours, milliseconds = divmod(milliseconds, 3_600_000)
    minutes, milliseconds = divmod(milliseconds, 60_000)
    whole_seconds, milliseconds = divmod(milliseconds, 1000)
    return f"{hours:02d}:{minutes:02d}:{whole_seconds:02d},{milliseconds:03d}"


def _build_srt(text, words_per_second=2.5):
    """Create estimated SRT cues from text without requiring media libraries."""
    cues = []
    current_time = 0.0

    for index, caption in enumerate(_caption_lines(text), start=1):
        word_count = max(1, len(caption.split()))
        duration = min(5.0, max(1.5, word_count / words_per_second))
        end_time = current_time + duration
        cues.append(
            f"{index}\n"
            f"{_format_srt_time(current_time)} --> {_format_srt_time(end_time)}\n"
            f"{caption}\n"
        )
        current_time = end_time

    return "\n".join(cues), len(cues)


def prepare_capcut_package(project, project_folder, replace=True):
    """
    Build a CapCut-ready folder from one FactVaultManager project.

    The package is written to ``<project>/CapCut/Ready`` and contains:

        01-script.txt
        02-voiceover.<original extension>
        03-images/01.<ext>, 02.<ext>, ...
        04-captions.srt
        05-title-and-description.txt
        06-source-notes.txt

    Captions use on-screen text when available and otherwise fall back to the
    script. Their timings are estimates intended as a useful starting point in
    CapCut, where they can be aligned to the final voiceover.
    """
    project_folder = Path(project_folder)

    if not project_folder.exists():
        raise FileNotFoundError(
            f"The project folder does not exist: {project_folder}"
        )

    destination = project_folder / "CapCut" / "Ready"

    if destination.exists():
        if not replace:
            raise FileExistsError(
                f"The CapCut package already exists: {destination}"
            )
        shutil.rmtree(destination)

    images_destination = destination / "03-images"
    images_destination.mkdir(parents=True, exist_ok=True)

    copied = []
    missing = []

    script = str(_project_value(project, "script", "")).strip()
    if script:
        script_path = destination / "01-script.txt"
        script_path.write_text(script + "\n", encoding="utf-8")
        copied.append(script_path)
    else:
        missing.append("script")

    voice_folder = project_folder / "Voice"
    audio_files = []
    if voice_folder.exists():
        audio_files = sorted(
            (
                path
                for path in voice_folder.iterdir()
                if path.is_file() and path.suffix.lower() in AUDIO_EXTENSIONS
            ),
            key=lambda path: (path.stat().st_mtime, _natural_sort_key(path)),
            reverse=True,
        )

    if audio_files:
        source_audio = audio_files[0]
        audio_path = destination / f"02-voiceover{source_audio.suffix.lower()}"
        shutil.copy2(source_audio, audio_path)
        copied.append(audio_path)
    else:
        missing.append("voiceover")

    images_folder = project_folder / "Assets" / "Images"
    image_files = []
    if images_folder.exists():
        image_files = sorted(
            (
                path
                for path in images_folder.iterdir()
                if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
            ),
            key=_natural_sort_key,
        )

    for index, source_image in enumerate(image_files, start=1):
        image_path = images_destination / (
            f"{index:02d}{source_image.suffix.lower()}"
        )
        shutil.copy2(source_image, image_path)
        copied.append(image_path)

    if not image_files:
        missing.append("images")

    on_screen_text = str(
        _project_value(project, "on_screen_text", "")
    ).strip()
    caption_text = on_screen_text or script
    caption_source = "on-screen text" if on_screen_text else "script"
    caption_count = 0

    if caption_text:
        srt_text, caption_count = _build_srt(caption_text)
        captions_path = destination / "04-captions.srt"
        captions_path.write_text(srt_text, encoding="utf-8")
        copied.append(captions_path)
    else:
        caption_source = None
        missing.append("captions")

    title = str(_project_value(project, "title", "")).strip()
    description = str(_project_value(project, "description", "")).strip()
    pinned_comment = str(
        _project_value(project, "pinned_comment", "")
    ).strip()

    publishing_text = [
        f"TITLE\n{title or '[Add title]'}",
        f"DESCRIPTION\n{description or '[Add description]'}",
        f"PINNED COMMENT\n{pinned_comment or '[Add pinned comment]'}",
    ]
    publishing_path = destination / "05-title-and-description.txt"
    publishing_path.write_text(
        "\n\n".join(publishing_text) + "\n",
        encoding="utf-8",
    )
    copied.append(publishing_path)

    notes = str(_project_value(project, "notes", "")).strip()
    notes_path = destination / "06-source-notes.txt"
    notes_path.write_text(
        (notes or "No source notes have been added.") + "\n",
        encoding="utf-8",
    )
    copied.append(notes_path)

    checklist_lines = [
        f"{'OK' if script else 'MISSING'} - Script",
        f"{'OK' if audio_files else 'MISSING'} - Voiceover",
        f"{'OK' if image_files else 'MISSING'} - Images ({len(image_files)})",
        (
            f"OK - Draft captions ({caption_count}, from {caption_source})"
            if caption_count
            else "MISSING - Captions"
        ),
        "OK - Title and description file",
        "OK - Source notes file",
    ]
    checklist_path = destination / "00-readiness-checklist.txt"
    checklist_path.write_text(
        "\n".join(checklist_lines) + "\n",
        encoding="utf-8",
    )
    copied.append(checklist_path)

    return {
        "folder": destination,
        "copied_files": copied,
        "missing": missing,
        "image_count": len(image_files),
        "caption_count": caption_count,
        "caption_source": caption_source,
        "audio_source": audio_files[0] if audio_files else None,
        "ready": not missing,
    }
