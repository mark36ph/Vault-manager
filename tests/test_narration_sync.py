from pathlib import Path

import pytest

from common.narration_sync import (
    NarrationSyncError,
    load_project_script,
    narration_matches_script,
    normalize_script,
    regenerate_narration,
    script_digest,
)


def test_normalize_script_preserves_words_and_normalizes_line_endings():
    assert normalize_script("  One\r\nTwo\r  ") == "One\nTwo"


def test_load_project_script_reads_exact_saved_script(tmp_path):
    (tmp_path / "Script.txt").write_text("Line one.\n\nLine two.\n", encoding="utf-8")
    assert load_project_script(tmp_path) == "Line one.\n\nLine two."


def test_load_project_script_rejects_missing_file(tmp_path):
    with pytest.raises(NarrationSyncError, match="could not be found"):
        load_project_script(tmp_path)


def test_regenerate_narration_passes_exact_script_to_provider(tmp_path):
    script = "This is the exact app script.\nNothing else should be narrated."
    (tmp_path / "Script.txt").write_text(script, encoding="utf-8")
    seen = {}

    def provider(context):
        seen["script"] = context.script
        destination = Path(context.project_folder) / "Voice" / "narration.mp3"
        destination.write_bytes(b"audio")
        return destination

    result = regenerate_narration(tmp_path, provider)

    assert seen["script"] == script
    assert result.audio_path.read_bytes() == b"audio"
    assert result.script_path.read_text(encoding="utf-8") == script + "\n"
    assert result.hash_path.read_text(encoding="utf-8").strip() == script_digest(script)
    assert result.word_count == 10
    assert narration_matches_script(tmp_path) is True


def test_narration_match_becomes_false_when_script_changes(tmp_path):
    (tmp_path / "Script.txt").write_text("Original script", encoding="utf-8")

    def provider(context):
        destination = Path(context.project_folder) / "Voice" / "narration.mp3"
        destination.write_bytes(b"audio")
        return destination

    regenerate_narration(tmp_path, provider)
    (tmp_path / "Script.txt").write_text("Updated script", encoding="utf-8")
    assert narration_matches_script(tmp_path) is False


def test_regenerate_narration_rejects_missing_audio(tmp_path):
    (tmp_path / "Script.txt").write_text("Script", encoding="utf-8")
    with pytest.raises(NarrationSyncError, match="usable audio"):
        regenerate_narration(tmp_path, lambda context: tmp_path / "Voice" / "missing.mp3")
