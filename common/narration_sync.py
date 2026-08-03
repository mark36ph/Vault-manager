"""Keep generated narration tied to the exact script shown in the project."""
from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Callable


class NarrationSyncError(RuntimeError):
    """Raised when narration cannot be regenerated from the saved project script."""


@dataclass(frozen=True)
class NarrationSyncResult:
    audio_path: Path
    script_path: Path
    hash_path: Path
    script_hash: str
    word_count: int


def normalize_script(text: str) -> str:
    """Normalize line endings and surrounding whitespace without rewriting words."""
    return str(text or "").replace("\r\n", "\n").replace("\r", "\n").strip()


def script_digest(script: str) -> str:
    return hashlib.sha256(normalize_script(script).encode("utf-8")).hexdigest()


def load_project_script(project_folder: str | Path) -> str:
    path = Path(project_folder) / "Script.txt"
    if not path.is_file():
        raise NarrationSyncError(f"Project script could not be found: {path}")
    script = normalize_script(path.read_text(encoding="utf-8"))
    if not script:
        raise NarrationSyncError("Project Script.txt is empty")
    return script


def regenerate_narration(
    project_folder: str | Path,
    speech_provider: Callable[[Any], str | Path],
) -> NarrationSyncResult:
    """Regenerate narration using exactly the current Script.txt contents."""
    folder = Path(project_folder)
    script = load_project_script(folder)
    voice_folder = folder / "Voice"
    voice_folder.mkdir(parents=True, exist_ok=True)

    script_path = voice_folder / "narration_script.txt"
    hash_path = voice_folder / "narration_script.sha256"
    script_path.write_text(script + "\n", encoding="utf-8")
    digest = script_digest(script)
    hash_path.write_text(digest + "\n", encoding="utf-8")

    result = Path(speech_provider(SimpleNamespace(script=script, project_folder=folder)))
    if not result.is_file() or result.stat().st_size == 0:
        raise NarrationSyncError(f"Narration provider did not create usable audio: {result}")

    return NarrationSyncResult(
        audio_path=result,
        script_path=script_path,
        hash_path=hash_path,
        script_hash=digest,
        word_count=len(script.split()),
    )


def narration_matches_script(project_folder: str | Path) -> bool:
    folder = Path(project_folder)
    hash_path = folder / "Voice" / "narration_script.sha256"
    try:
        expected = script_digest(load_project_script(folder))
        actual = hash_path.read_text(encoding="utf-8").strip()
    except (OSError, UnicodeError, NarrationSyncError):
        return False
    return bool(actual) and actual == expected


__all__ = [
    "NarrationSyncError",
    "NarrationSyncResult",
    "load_project_script",
    "narration_matches_script",
    "normalize_script",
    "regenerate_narration",
    "script_digest",
]
