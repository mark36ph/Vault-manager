import json
import urllib.error
import urllib.parse
import urllib.request

from image_models import ImageSearchResult
from image_providers.base import ImageProvider
from image_providers.errors import ImageSearchError


PIXABAY_API_URL = "https://pixabay.com/api/"
PIXABAY_VIDEO_API_URL = "https://pixabay.com/api/videos/"
USER_AGENT = "FactVaultManager/1.0"


class PixabayProvider(ImageProvider):
    """Pixabay image and video search provider."""

    provider_name = "Pixabay"
    VALID_ORIENTATIONS = {"all", "horizontal", "vertical"}

    def __init__(self, api_key):
        self.api_key = str(api_key or "").strip()

    def search(self, query, *, page=1, per_page=20, orientation="vertical"):
        query, orientation = self._validate(query, orientation)
        parameters = urllib.parse.urlencode(
            {
                "key": self.api_key,
                "q": query,
                "image_type": "photo",
                "orientation": orientation,
                "safesearch": "true",
                "page": max(1, int(page)),
                "per_page": max(3, min(int(per_page), 200)),
            }
        )
        payload = self._send_request(
            urllib.request.Request(
                f"{PIXABAY_API_URL}?{parameters}",
                headers={"User-Agent": USER_AGENT},
            )
        )
        return [result for item in payload.get("hits", []) if (result := self._parse_image(item))]

    def search_videos(self, query, *, page=1, per_page=20, orientation="vertical"):
        query, orientation = self._validate(query, orientation)
        parameters = urllib.parse.urlencode(
            {
                "key": self.api_key,
                "q": query,
                "safesearch": "true",
                "page": max(1, int(page)),
                "per_page": max(3, min(int(per_page), 200)),
            }
        )
        payload = self._send_request(
            urllib.request.Request(
                f"{PIXABAY_VIDEO_API_URL}?{parameters}",
                headers={"User-Agent": USER_AGENT},
            )
        )
        results = []
        for item in payload.get("hits", []):
            result = self._parse_video(item, orientation)
            if result is not None:
                results.append(result)
        return results

    def _validate(self, query, orientation):
        query = str(query or "").strip()
        orientation = str(orientation or "vertical").strip().lower()
        if not query:
            raise ValueError("Enter a media search term.")
        if not self.api_key:
            raise ValueError("A Pixabay API key is required.")
        if orientation not in self.VALID_ORIENTATIONS:
            raise ValueError("Invalid media orientation.")
        return query, orientation

    def _send_request(self, request):
        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            message = exc.read().decode("utf-8", errors="replace").strip()
            raise ImageSearchError(message or f"Pixabay returned HTTP {exc.code}.") from exc
        except urllib.error.URLError as exc:
            raise ImageSearchError(f"Could not connect to Pixabay: {exc.reason}") from exc
        except TimeoutError as exc:
            raise ImageSearchError("The Pixabay request timed out.") from exc
        except (json.JSONDecodeError, TypeError) as exc:
            raise ImageSearchError("Pixabay returned an invalid response.") from exc

    def _parse_image(self, item):
        download_url = item.get("largeImageURL") or item.get("webformatURL") or ""
        preview_url = item.get("webformatURL") or item.get("previewURL") or download_url
        if not download_url or not preview_url:
            return None
        creator = str(item.get("user") or "Unknown")
        return ImageSearchResult(
            image_id=int(item.get("id", 0)),
            preview_url=preview_url,
            download_url=download_url,
            page_url=str(item.get("pageURL") or ""),
            creator=creator,
            creator_url=str(item.get("userImageURL") or ""),
            tags=str(item.get("tags") or ""),
            width=int(item.get("imageWidth") or 0),
            height=int(item.get("imageHeight") or 0),
            provider=self.provider_name,
            attribution=f"Image by {creator} from Pixabay",
            media_type="image",
        )

    def _parse_video(self, item, orientation):
        renditions = item.get("videos") or {}
        choices = []
        for name in ("medium", "large", "small", "tiny"):
            rendition = renditions.get(name) or {}
            url = str(rendition.get("url") or "")
            width = int(rendition.get("width") or 0)
            height = int(rendition.get("height") or 0)
            if not url:
                continue
            if orientation == "vertical" and width > height:
                continue
            if orientation == "horizontal" and height > width:
                continue
            choices.append((width * height, rendition))
        if not choices:
            return None
        _, rendition = max(choices, key=lambda value: value[0])
        creator = str(item.get("user") or "Unknown")
        return ImageSearchResult(
            image_id=int(item.get("id", 0)),
            preview_url=str(rendition.get("thumbnail") or ""),
            download_url=str(rendition.get("url") or ""),
            page_url=str(item.get("pageURL") or ""),
            creator=creator,
            creator_url="",
            tags=str(item.get("tags") or "Video"),
            width=int(rendition.get("width") or 0),
            height=int(rendition.get("height") or 0),
            provider=self.provider_name,
            attribution=f"Video by {creator} from Pixabay",
            media_type="video",
            duration=int(item.get("duration") or 0),
            file_size=int(rendition.get("size") or 0),
        )
