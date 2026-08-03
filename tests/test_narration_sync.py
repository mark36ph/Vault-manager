from pathlib import Path

import pytest

from common.narration_sync import (
    NarrationSyncError,
    narration_matches_script,
    normalize_script,
    regenerate_narration,
    require_project_script,
    script_digest,
)


def test_normalize_script_preserves_words_and_normalizes_line_endings():
    assert normalize_script("  One\r\nTwo\r  ") == "One\nTwo"


def test_require_project_script_reads_database_value():
    assert require_project_script("Line one.\n\nLine two.\n") == "Line one.\n\nLine two."


def test_require_project_script_rejects_empty_database_value():
    with pytest.raises(NarrationSyncError, match="database script is empty"):
        require_project_script("   ")


def test_regenerate_narration_passes_exact_database_script_to_provider(tmp_path):
    script = "This is the exact app script.\nNothing else should be narrated."
    seen = {}

    def provider(context):
        seen["script"] = context.script
        destination = Path(context.project_folder) / "Voice" / "narration.mp3"
        destination.write_bytes(b"audio")
        return destination

    result = regenerate_narration(tmp_path, script, provider, duration_reader=lambda path: 44.0)

    assert seen["script"] == script
    assert result.audio_path.read_bytes() == b"audio"
    assert result.script_path.read_text(encoding="utf-8") == script + "\n"
    assert result.hash_path.read_text(encoding="utf-8").strip() == script_digest(script)
    assert result.word_count == 11
    assert result.duration == 44.0
    assert narration_matches_script(tmp_path, script) is True


def test_narration_match_becomes_false_when_database_script_changes(tmp_path):
    original = "Original script"

    def provider(context):
        destination = Path(context.project_folder) / "Voice" / "narration.mp3"
        destination.write_bytes(b"audio")
        return destination

    regenerate_narration(tmp_path, original, provider, duration_reader=lambda path: 2.0)
    assert narration_matches_script(tmp_path, "Updated script") is False


def test_regenerate_narration_rejects_missing_audio(tmp_path):
    with pytest.raises(NarrationSyncError, match="usable audio"):
        regenerate_narration(
            tmp_path,
            "Script from database",
            lambda context: tmp_path / "Voice" / "missing.mp3",
            duration_reader=lambda path: 1.0,
        )
