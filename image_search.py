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


PROVIDERS = {
    "pixabay": PixabayProvider,
    "pexels": PexelsProvider,
}


def get_provider(
    provider_name,
    settings,
):
    """
    Create the selected image provider using saved app settings.
    """
    normalised_name = str(
        provider_name or ""
    ).strip().lower()

    if normalised_name == "pixabay":
        return PixabayProvider(
            api_key=settings.get(
                "images",
                "pixabay_api_key",
                "",
            )
        )
    if normalised_name == "pexels":
        return PexelsProvider(
            api_key=settings.get(
                "images",
                "pexels_api_key",
                "",
            )
        )

    raise ImageSearchError(
        f"Unsupported image provider: {provider_name}"
    )


def search_images(
    provider_name,
    settings,
    query,
    *,
    page=1,
    per_page=20,
    orientation="vertical",
):
    """
    Search images using the selected provider.
    """
    provider = get_provider(
        provider_name,
        settings,
    )

    return provider.search(
        query,
        page=page,
        per_page=per_page,
        orientation=orientation,
    )


def search_pixabay_images(
    query,
    api_key,
    *,
    page=1,
    per_page=20,
    orientation="vertical",
):
    """
    Backwards-compatible Pixabay search function.

    Existing UI code and tests can continue importing this function.
    """
    provider = PixabayProvider(
        api_key=api_key
    )

    return provider.search(
        query,
        page=page,
        per_page=per_page,
        orientation=orientation,
    )


def download_image_to_project(
    result,
    project_folder,
):
    """
    Download an image result into the project's Assets/Images folder.

    A matching source-information text file is stored beside the image.
    """
    project_folder = Path(
        project_folder
    )

    images_folder = (
        project_folder
        / "Assets"
        / "Images"
    )

    images_folder.mkdir(
        parents=True,
        exist_ok=True,
    )

    extension = _extension_from_url(
        result.download_url
    )

    default_name = (
        f"{str(result.provider).lower()}-image"
        if getattr(result, "provider", "")
        else "image"
    )

    first_tag = (
        result.tags.split(",")[0]
        if result.tags
        else default_name
    )

    base_name = _safe_filename(
        first_tag
    )

    image_path = _available_path(
        images_folder,
        f"{base_name}-{result.image_id}",
        extension,
    )

    request = urllib.request.Request(
        result.download_url,
        headers={
            "User-Agent": USER_AGENT,
        },
    )

    try:
        with urllib.request.urlopen(
            request,
            timeout=30,
        ) as response:
            image_path.write_bytes(
                response.read()
            )

    except urllib.error.HTTPError as exc:
        raise ImageSearchError(
            (
                "The image download failed "
                f"with HTTP {exc.code}."
            )
        ) from exc

    except urllib.error.URLError as exc:
        raise ImageSearchError(
            f"Could not download the image: {exc.reason}"
        ) from exc

    except TimeoutError as exc:
        raise ImageSearchError(
            "The image download timed out."
        ) from exc

    _write_source_file(
        result,
        image_path,
    )

    return image_path


def _write_source_file(
    result,
    image_path,
):
    provider = (
        getattr(
            result,
            "provider",
            "",
        )
        or "Unknown"
    )

    attribution = (
        getattr(
            result,
            "attribution",
            "",
        )
        or ""
    )

    creator_url = (
        getattr(
            result,
            "creator_url",
            "",
        )
        or ""
    )

    source_lines = [
        f"Provider: {provider}",
        f"Creator: {result.creator}",
    ]

    if creator_url:
        source_lines.append(
            f"Creator URL: {creator_url}"
        )

    source_lines.extend(
        [
            f"Source page: {result.page_url}",
            f"Tags: {result.tags}",
            f"Image ID: {result.image_id}",
            (
                "Dimensions: "
                f"{result.width} x {result.height}"
            ),
        ]
    )

    if attribution:
        source_lines.append(
            f"Attribution: {attribution}"
        )

    source_path = image_path.with_suffix(
        ".source.txt"
    )

    source_path.write_text(
        "\n".join(source_lines) + "\n",
        encoding="utf-8",
    )


def _safe_filename(value):
    value = re.sub(
        r"[^a-zA-Z0-9_-]+",
        "-",
        str(value or "").strip().lower(),
    )

    return (
        value.strip("-_")
        or "image"
    )


def _extension_from_url(url):
    path = urllib.parse.urlparse(
        url
    ).path

    extension = Path(
        path
    ).suffix.lower()

    if extension not in {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
    }:
        return ".jpg"

    return extension


def _available_path(
    folder,
    stem,
    extension,
):
    candidate = folder / (
        f"{stem}{extension}"
    )

    counter = 2

    while candidate.exists():
        candidate = folder / (
            f"{stem}-{counter}{extension}"
        )

        counter += 1

    return candidate


__all__ = [
    "ImageSearchError",
    "ImageSearchResult",
    "download_image_to_project",
    "get_provider",
    "search_images",
    "search_pixabay_images",
]