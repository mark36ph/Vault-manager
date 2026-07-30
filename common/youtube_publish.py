import re
from pathlib import Path


def build_video_filename(title):
    """Return a Windows-safe MP4 filename."""
    cleaned = re.sub(r'[<>:"/\\|?*]', "", str(title))
    cleaned = " ".join(cleaned.split()).strip(" .")

    if not cleaned:
        cleaned = "video"

    return f"{cleaned}.mp4"


def build_upload_metadata(project):
    """Build YouTube-style snippet and status metadata."""
    project = project or {}

    title = str(project.get("title") or "Untitled Video").strip()
    description = str(project.get("description") or "")
    tags = project.get("tags") or []

    if isinstance(tags, str):
        tags = [tag.strip() for tag in tags.split(",") if tag.strip()]
    else:
        tags = [str(tag).strip() for tag in tags if str(tag).strip()]

    privacy_status = str(
        project.get("privacy_status")
        or project.get("privacyStatus")
        or "private"
    ).lower()

    if privacy_status not in {"private", "unlisted", "public"}:
        raise ValueError(
            "privacy_status must be private, unlisted, or public."
        )

    metadata = {
        "snippet": {
            "title": title,
            "description": description,
            "tags": tags,
        },
        "status": {
            "privacyStatus": privacy_status,
        },
    }

    category_id = project.get("category_id") or project.get("categoryId")
    if category_id is not None:
        metadata["snippet"]["categoryId"] = str(category_id)

    publish_at = project.get("publish_at") or project.get("publishAt")
    if publish_at:
        metadata["status"]["publishAt"] = str(publish_at)

    return metadata


def publish_project(
    youtube,
    video_path,
    project,
    thumbnail=None,
    privacy_status=None,
):
    """Upload a rendered video and optionally its thumbnail."""
    video_path = Path(video_path)

    if not video_path.is_file():
        raise FileNotFoundError(f"Rendered video not found: {video_path}")

    metadata = build_upload_metadata(project)

    selected_privacy = (
        privacy_status or metadata["status"]["privacyStatus"]
    )

    response = youtube.upload(
        video_path,
        metadata,
        selected_privacy,
    )

    if not response or not response.get("id"):
        raise RuntimeError("YouTube upload did not return a video ID.")

    video_id = response["id"]
    url = response.get("url") or f"https://youtu.be/{video_id}"

    thumbnail_uploaded = False

    if thumbnail is not None:
        thumbnail_path = Path(thumbnail)

        if not thumbnail_path.is_file():
            raise FileNotFoundError(
                f"Thumbnail not found: {thumbnail_path}"
            )

        thumbnail_uploaded = bool(
            youtube.upload_thumbnail(video_id, thumbnail_path)
        )

    return {
        "video_id": video_id,
        "url": url,
        "privacy_status": response.get(
            "privacyStatus",
            selected_privacy,
        ),
        "thumbnail_uploaded": thumbnail_uploaded,
        "response": response,
    }


__all__ = [
    "build_video_filename",
    "build_upload_metadata",
    "publish_project",
]