import json
import urllib.error
import urllib.parse
import urllib.request

from image_models import ImageSearchResult
from image_providers.base import ImageProvider
from image_providers.errors import ImageSearchError


PEXELS_API_URL = "https://api.pexels.com/v1/search"
PEXELS_VIDEO_API_URL = "https://api.pexels.com/v1/videos/search"
USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/138.0.0.0 Safari/537.36"
)


class PexelsProvider(ImageProvider):
    """Pexels image and video search provider."""

    provider_name = "Pexels"
    VALID_ORIENTATIONS = {"all", "horizontal", "vertical", "square"}
    ORIENTATION_MAP = {
        "all": None,
        "horizontal": "landscape",
        "vertical": "portrait",
        "square": "square",
    }

    def __init__(self, api_key):
        self.api_key = str(api_key or "").strip()

    def search(self, query, *, page=1, per_page=20, orientation="vertical"):
        query, orientation = self._validate(query, orientation)
        parameters = {
            "query": query,
            "page": max(1, int(page)),
            "per_page": max(1, min(int(per_page), 80)),
        }
        mapped = self.ORIENTATION_MAP[orientation]
        if mapped:
            parameters["orientation"] = mapped
        payload = self._send_request(self._request(PEXELS_API_URL, parameters))
        results = []
        for item in payload.get("photos", []):
            result = self._parse_image(item, query)
            if result:
                results.append(result)
        return results

    def search_videos(self, query, *, page=1, per_page=20, orientation="vertical"):
        query, orientation = self._validate(query, orientation)
        parameters = {
            "query": query,
            "page": max(1, int(page)),
            "per_page": max(1, min(int(per_page), 80)),
        }
        mapped = self.ORIENTATION_MAP[orientation]
        if mapped:
            parameters["orientation"] = mapped
        payload = self._send_request(self._request(PEXELS_VIDEO_API_URL, parameters))
        results = []
        for item in payload.get("videos", []):
            result = self._parse_video(item, query, orientation)
            if result:
                results.append(result)
        return results

    def _validate(self, query, orientation):
        query = str(query or "").strip()
        orientation = str(orientation or "vertical").strip().lower()
        if not query:
            raise ValueError("Enter a media search term.")
        if not self.api_key:
            raise ValueError("A Pexels API key is required.")
        if orientation not in self.VALID_ORIENTATIONS:
            raise ValueError("Invalid media orientation.")
        return query, orientation

    def _request(self, endpoint, parameters):
        return urllib.request.Request(
            f"{endpoint}?{urllib.parse.urlencode(parameters)}",
            headers={
                "Authorization": self.api_key,
                "User-Agent": USER_AGENT,
                "Accept": "application/json",
            },
        )

    def _send_request(self, request):
        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            message = self._read_http_error(exc)

            if exc.code == 401:
                raise ImageSearchError(
                    "Pexels rejected the API key. Check the key in Settings → Images."
                ) from exc

            if exc.code == 429:
                raise ImageSearchError(
                    "The Pexels API request limit has been reached."
                ) from exc

            # Cloudflare / browser signature block
            if exc.code == 403:
                print(f"Pexels unavailable ({message}). Falling back to next provider.")
                return []

            raise ImageSearchError(message or f"Pexels returned HTTP {exc.code}.") from exc

    def _parse_image(self, item, query):
        src = item.get("src") or {}
        preview = src.get("medium") or src.get("small") or src.get("tiny") or ""
        download = src.get("large2x") or src.get("large") or src.get("original") or preview
        if not preview or not download:
            return None
        creator = str(item.get("photographer") or "Unknown")
        return ImageSearchResult(
            image_id=int(item.get("id") or 0),
            preview_url=str(preview),
            download_url=str(download),
            page_url=str(item.get("url") or ""),
            creator=creator,
            creator_url=str(item.get("photographer_url") or ""),
            tags=str(item.get("alt") or query),
            width=int(item.get("width") or 0),
            height=int(item.get("height") or 0),
            provider=self.provider_name,
            attribution=f"Photo by {creator} on Pexels",
            media_type="image",
        )

    def _parse_video(self, item, query, orientation):
        choices = []
        for file_info in item.get("video_files", []):
            if file_info.get("file_type") != "video/mp4":
                continue
            width = int(file_info.get("width") or 0)
            height = int(file_info.get("height") or 0)
            if orientation == "vertical" and width > height:
                continue
            if orientation == "horizontal" and height > width:
                continue
            link = str(file_info.get("link") or "")
            if link:
                choices.append((width * height, file_info))
        if not choices:
            return None
        _, selected = max(choices, key=lambda value: value[0])
        user = item.get("user") or {}
        creator = str(user.get("name") or "Unknown")
        preview = str(item.get("image") or "")
        if not preview:
            return None
        return ImageSearchResult(
            image_id=int(item.get("id") or 0),
            preview_url=preview,
            download_url=str(selected.get("link") or ""),
            page_url=str(item.get("url") or ""),
            creator=creator,
            creator_url=str(user.get("url") or ""),
            tags=query,
            width=int(selected.get("width") or 0),
            height=int(selected.get("height") or 0),
            provider=self.provider_name,
            attribution=f"Video by {creator} on Pexels",
            media_type="video",
            duration=int(item.get("duration") or 0),
        )

    @staticmethod
    def _read_http_error(exc):
        try:
            body = exc.read().decode("utf-8", errors="replace").strip()
        except Exception:
            return ""
        if not body:
            return ""
        try:
            payload = json.loads(body)
        except (json.JSONDecodeError, TypeError):
            return body
        return str(payload.get("error") or payload.get("message") or body) if isinstance(payload, dict) else body
