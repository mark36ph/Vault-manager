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


def prepare_capcut_package(project, project_folder, replace=True):
    """
    Build a CapCut-ready folder from one FactVaultManager project.

    The package is written to ``<project>/CapCut/Ready`` and contains:

        01-script.txt
        02-voiceover.<original extension>
        03-images/01.<ext>, 02.<ext>, ...
        05-title-and-description.txt
        06-source-notes.txt

    Existing database text remains the source of truth. Media is copied from
    ``Assets/Images`` and ``Voice``. The function returns a result dictionary
    suitable for a UI readiness summary.
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
        "NOT YET GENERATED - Captions",
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
        "audio_source": audio_files[0] if audio_files else None,
        "ready": not missing,
    }
