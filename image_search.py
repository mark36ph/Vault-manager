import re
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from image_models import ImageSearchResult
from image_providers.errors import ImageSearchError
from image_providers.pixabay import PixabayProvider
from image_providers.pexels import PexelsProvider

USER_AGENT = "FactVaultManager/1.0"
PROVIDERS = {"pixabay": PixabayProvider, "pexels": PexelsProvider}


def get_provider(provider_name, settings):
    name = str(provider_name or "").strip().lower()
    if name == "pixabay":
        return PixabayProvider(settings.get("images", "pixabay_api_key", ""))
    if name == "pexels":
        return PexelsProvider(settings.get("images", "pexels_api_key", ""))
    raise ImageSearchError(f"Unsupported media provider: {provider_name}")


def search_media(provider_name, settings, query, *, media_type="image", page=1, per_page=20, orientation="vertical"):
    provider = get_provider(provider_name, settings)
    if str(media_type).lower() == "video":
        return provider.search_videos(
            query, page=page, per_page=per_page, orientation=orientation
        )
    return provider.search(
        query, page=page, per_page=per_page, orientation=orientation
    )


def search_images(provider_name, settings, query, *, page=1, per_page=20, orientation="vertical"):
    return search_media(
        provider_name,
        settings,
        query,
        media_type="image",
        page=page,
        per_page=per_page,
        orientation=orientation,
    )


def search_pixabay_images(query, api_key, *, page=1, per_page=20, orientation="vertical"):
    return PixabayProvider(api_key).search(
        query, page=page, per_page=per_page, orientation=orientation
    )


def download_media_to_project(result, project_folder):
    project_folder = Path(project_folder)
    media_type = str(getattr(result, "media_type", "image") or "image").lower()
    folder_name = "Videos" if media_type == "video" else "Images"
    media_folder = project_folder / "Assets" / folder_name
    media_folder.mkdir(parents=True, exist_ok=True)

    extension = _extension_from_url(result.download_url, media_type)
    first_tag = result.tags.split(",")[0] if result.tags else f"{result.provider}-{media_type}"
    stem = f"{_safe_filename(first_tag)}-{result.image_id}"
    media_path = _available_path(media_folder, stem, extension)

    request = urllib.request.Request(
        result.download_url,
        headers={"User-Agent": USER_AGENT},
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            media_path.write_bytes(response.read())
    except urllib.error.HTTPError as exc:
        raise ImageSearchError(
            f"The {media_type} download failed with HTTP {exc.code}."
        ) from exc
    except urllib.error.URLError as exc:
        raise ImageSearchError(f"Could not download the {media_type}: {exc.reason}") from exc
    except TimeoutError as exc:
        raise ImageSearchError(f"The {media_type} download timed out.") from exc

    _write_source_file(result, media_path)
    return media_path


def download_image_to_project(result, project_folder):
    return download_media_to_project(result, project_folder)


def is_media_saved(result, project_folder):
    media_type = str(getattr(result, "media_type", "image") or "image").lower()
    folder_name = "Videos" if media_type == "video" else "Images"
    folder = Path(project_folder) / "Assets" / folder_name
    if not folder.exists():
        return False
    provider_line = f"Provider: {result.provider}"
    id_line = f"Media ID: {result.image_id}"
    for source_file in folder.glob("*.source.txt"):
        try:
            text = source_file.read_text(encoding="utf-8")
        except OSError:
            continue
        if provider_line in text and id_line in text:
            return True
    return False


def _write_source_file(result, media_path):
    media_type = str(getattr(result, "media_type", "image") or "image").lower()
    lines = [
        f"Provider: {getattr(result, 'provider', '') or 'Unknown'}",
        f"Media Type: {media_type}",
        f"Media ID: {result.image_id}",
        f"Creator: {result.creator}",
    ]
    if getattr(result, "creator_url", ""):
        lines.append(f"Creator URL: {result.creator_url}")
    lines.extend(
        [
            f"Source page: {result.page_url}",
            f"Tags: {result.tags}",
            f"Dimensions: {result.width} x {result.height}",
        ]
    )
    if getattr(result, "duration", 0):
        lines.append(f"Duration: {result.duration} seconds")
    if getattr(result, "attribution", ""):
        lines.append(f"Attribution: {result.attribution}")
    media_path.with_suffix(".source.txt").write_text(
        "\n".join(lines) + "\n", encoding="utf-8"
    )


def _safe_filename(value):
    value = re.sub(r"[^a-zA-Z0-9_-]+", "-", str(value or "").strip().lower())
    return value.strip("-_") or "media"


def _extension_from_url(url, media_type="image"):
    extension = Path(urllib.parse.urlparse(url).path).suffix.lower()
    if media_type == "video":
        return extension if extension in {".mp4", ".mov", ".webm"} else ".mp4"
    return extension if extension in {".jpg", ".jpeg", ".png", ".webp"} else ".jpg"


def _available_path(folder, stem, extension):
    candidate = folder / f"{stem}{extension}"
    counter = 2
    while candidate.exists():
        candidate = folder / f"{stem}-{counter}{extension}"
        counter += 1
    return candidate


__all__ = [
    "ImageSearchError",
    "ImageSearchResult",
    "download_image_to_project",
    "download_media_to_project",
    "get_provider",
    "is_media_saved",
    "search_images",
    "search_media",
    "search_pixabay_images",
]
