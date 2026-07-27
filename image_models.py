from dataclasses import dataclass


@dataclass(frozen=True)
class ImageSearchResult:
    image_id: int
    preview_url: str
    download_url: str
    page_url: str
    creator: str
    creator_url: str
    tags: str
    width: int
    height: int

    # Provider-neutral fields for future providers.
    provider: str = "Pixabay"
    attribution: str = ""
    download_tracking_url: str = ""