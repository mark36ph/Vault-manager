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

    # Provider-neutral fields shared by image and video results.
    provider: str = "Pixabay"
    attribution: str = ""
    download_tracking_url: str = ""
    media_type: str = "image"
    duration: int = 0
    file_size: int = 0

    @property
    def media_id(self):
        """Provider-neutral alias retained alongside the legacy image_id field."""
        return self.image_id
