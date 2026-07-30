from pathlib import Path

from common.youtube_publish import (
    build_upload_metadata,
    build_video_filename,
    publish_project,
)


class FakeYouTubeService:
    def __init__(self):
        self.uploaded = False
        self.thumbnail_uploaded = False

    def upload(self, video_path, metadata, privacy_status):
        self.uploaded = True
        return {
            "id": "abc123xyz",
            "url": "https://youtu.be/abc123xyz",
            "privacyStatus": privacy_status,
        }

    def upload_thumbnail(self, video_id, thumbnail_path):
        self.thumbnail_uploaded = True
        return True


def test_build_video_filename_removes_invalid_characters():
    assert (
        build_video_filename("Titanic: Facts? *2026*")
        == "Titanic Facts 2026.mp4"
    )


def test_build_upload_metadata():
    project = {
        "title": "Titanic Facts",
        "description": "Amazing facts about the Titanic.",
        "tags": ["history", "facts"],
    }

    metadata = build_upload_metadata(project)

    assert metadata["snippet"]["title"] == "Titanic Facts"
    assert "history" in metadata["snippet"]["tags"]


def test_publish_project_returns_video_information(tmp_path):
    video = tmp_path / "video.mp4"
    video.write_bytes(b"video")

    youtube = FakeYouTubeService()

    result = publish_project(
        youtube,
        video,
        {
            "title": "Titanic Facts",
            "description": "Desc",
            "tags": [],
        },
    )

    assert youtube.uploaded
    assert result["video_id"] == "abc123xyz"
    assert result["url"] == "https://youtu.be/abc123xyz"


def test_publish_project_uploads_thumbnail(tmp_path):
    video = tmp_path / "video.mp4"
    thumb = tmp_path / "thumb.jpg"

    video.write_bytes(b"video")
    thumb.write_bytes(b"image")

    youtube = FakeYouTubeService()

    publish_project(
        youtube,
        video,
        {
            "title": "Titanic",
            "description": "",
            "tags": [],
        },
        thumbnail=thumb,
    )

    assert youtube.thumbnail_uploaded


def test_publish_project_missing_video_raises_error(tmp_path):
    youtube = FakeYouTubeService()

    try:
        publish_project(
            youtube,
            tmp_path / "missing.mp4",
            {
                "title": "Titanic",
                "description": "",
                "tags": [],
            },
        )
        assert False
    except FileNotFoundError:
        pass