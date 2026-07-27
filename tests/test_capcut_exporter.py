import pytest

from capcut_exporter import prepare_capcut_package


def make_project_folder(tmp_path):
    project_folder = tmp_path / "Draft" / "Test Fact"
    (project_folder / "Assets" / "Images").mkdir(parents=True)
    (project_folder / "Voice").mkdir(parents=True)
    (project_folder / "CapCut").mkdir(parents=True)
    return project_folder


def test_prepare_capcut_package_copies_and_numbers_assets(tmp_path):
    project_folder = make_project_folder(tmp_path)

    (project_folder / "Assets" / "Images" / "image10.png").write_bytes(b"10")
    (project_folder / "Assets" / "Images" / "image2.jpg").write_bytes(b"2")
    (project_folder / "Voice" / "voice.wav").write_bytes(b"audio")

    project = {
        "title": "A surprising fact",
        "script": "This is the narration.",
        "description": "Video description",
        "pinned_comment": "What do you think?",
        "notes": "Source: example",
    }

    result = prepare_capcut_package(project, project_folder)
    export_folder = result["folder"]

    assert result["ready"] is True
    assert result["missing"] == []
    assert result["image_count"] == 2
    assert result["caption_source"] == "script"

    assert (export_folder / "01-script.txt").read_text(
        encoding="utf-8"
    ) == "This is the narration.\n"

    assert (export_folder / "02-voiceover.wav").read_bytes() == b"audio"
    assert (export_folder / "03-images" / "01.jpg").read_bytes() == b"2"
    assert (export_folder / "03-images" / "02.png").read_bytes() == b"10"

    caption_text = (export_folder / "05-caption-text.txt").read_text(
        encoding="utf-8"
    )
    assert caption_text == "This is the narration.\n"
    assert not (export_folder / "04-captions.srt").exists()

    publishing_text = (
        export_folder / "04-title-and-description.txt"
    ).read_text(encoding="utf-8")

    assert "A surprising fact" in publishing_text
    assert "Video description" in publishing_text
    assert "What do you think?" in publishing_text

    workflow = (
        export_folder / "00-workflow.txt"
    ).read_text(encoding="utf-8")

    assert "OK - Script exported" in workflow
    assert "OK - Images copied (2)" in workflow
    assert "Generate the AI voice inside CapCut" in workflow


def test_prepare_capcut_package_prefers_on_screen_text_for_captions(tmp_path):
    project_folder = make_project_folder(tmp_path)

    (project_folder / "Assets" / "Images" / "image.png").write_bytes(b"image")
    (project_folder / "Voice" / "voice.mp3").write_bytes(b"audio")

    result = prepare_capcut_package(
        {
            "title": "Fact",
            "script": "Narration that should not be used as captions.",
            "on_screen_text": "First caption\nSecond caption",
        },
        project_folder,
    )

    caption_text = (
        result["folder"] / "05-caption-text.txt"
    ).read_text(encoding="utf-8")

    assert result["caption_source"] == "on-screen text"
    assert caption_text == "First caption\nSecond caption\n"
    assert "Narration that should not be used" not in caption_text
    assert not (result["folder"] / "04-captions.srt").exists()


def test_prepare_capcut_package_reports_missing_required_assets(tmp_path):
    project_folder = make_project_folder(tmp_path)

    result = prepare_capcut_package(
        {"title": "Incomplete fact", "script": ""},
        project_folder,
    )

    assert result["ready"] is False
    assert result["missing"] == ["script", "images"]
    assert result["image_count"] == 0

    workflow = (
        result["folder"] / "00-workflow.txt"
    ).read_text(encoding="utf-8")

    assert "MISSING - Script exported" in workflow
    assert "MISSING - Images copied (0)" in workflow
    assert "MISSING - Caption text" in workflow
    assert "Generate the AI voice inside CapCut" in workflow
    assert "Generate automatic captions" in workflow

def test_prepare_capcut_package_replaces_previous_export(tmp_path):
    project_folder = make_project_folder(tmp_path)
    old_export = project_folder / "CapCut" / "Ready"

    old_export.mkdir(parents=True)
    (old_export / "old.txt").write_text("old", encoding="utf-8")

    result = prepare_capcut_package(
        {"title": "Fact", "script": "Script"},
        project_folder,
        replace=True,
    )

    assert not (result["folder"] / "old.txt").exists()


def test_prepare_capcut_package_can_refuse_to_replace(tmp_path):
    project_folder = make_project_folder(tmp_path)
    (project_folder / "CapCut" / "Ready").mkdir(parents=True)

    with pytest.raises(FileExistsError):
        prepare_capcut_package(
            {"title": "Fact"},
            project_folder,
            replace=False,
        )


def test_prepare_capcut_package_requires_project_folder(tmp_path):
    missing_folder = tmp_path / "missing"

    with pytest.raises(FileNotFoundError):
        prepare_capcut_package({}, missing_folder)