import json
import urllib.error
import urllib.parse
import urllib.request

from image_models import ImageSearchResult
from image_providers.base import ImageProvider
from image_providers.errors import ImageSearchError


PEXELS_API_URL = "https://api.pexels.com/v1/search"
USER_AGENT = "FactVaultManager/1.0"


class PexelsProvider(ImageProvider):
    """Pexels image-search provider."""

    provider_name = "Pexels"

    VALID_ORIENTATIONS = {
        "all",
        "horizontal",
        "vertical",
        "square",
    }

    ORIENTATION_MAP = {
        "all": None,
        "horizontal": "landscape",
        "vertical": "portrait",
        "square": "square",
    }

    def __init__(self, api_key):
        self.api_key = str(api_key or "").strip()

    def search(
        self,
        query,
        *,
        page=1,
        per_page=20,
        orientation="vertical",
    ):
        """Search Pexels and return normalised image results."""
        query = str(query or "").strip()

        orientation = str(
            orientation or "vertical"
        ).strip().lower()

        if not query:
            raise ValueError(
                "Enter an image search term."
            )

        if not self.api_key:
            raise ValueError(
                "A Pexels API key is required."
            )

        if orientation not in self.VALID_ORIENTATIONS:
            raise ValueError(
                "Invalid image orientation."
            )

        page = self._normalise_page(page)
        per_page = self._normalise_per_page(per_page)

        parameters = {
            "query": query,
            "page": page,
            "per_page": per_page,
        }

        pexels_orientation = self.ORIENTATION_MAP[
            orientation
        ]

        if pexels_orientation:
            parameters["orientation"] = (
                pexels_orientation
            )

        encoded_parameters = urllib.parse.urlencode(
            parameters
        )

        url = (
            f"{PEXELS_API_URL}"
            f"?{encoded_parameters}"
        )

        request = urllib.request.Request(
            url,
            headers={
                "Authorization": self.api_key,
                "User-Agent": USER_AGENT,
                "Accept": "application/json",
            },
        )

        payload = self._send_request(
            request
        )

        return self._parse_results(
            payload,
            query=query,
        )

    def _send_request(self, request):
        try:
            with urllib.request.urlopen(
                request,
                timeout=20,
            ) as response:
                return json.load(response)

        except urllib.error.HTTPError as exc:
            message = self._read_http_error(
                exc
            )

            if exc.code == 401:
                message = (
                    "Pexels rejected the API key. "
                    "Check the key in Settings → Images."
                )

            elif exc.code == 429:
                message = (
                    "The Pexels API request limit "
                    "has been reached."
                )

            raise ImageSearchError(
                message
                or (
                    "Pexels returned "
                    f"HTTP {exc.code}."
                )
            ) from exc

        except urllib.error.URLError as exc:
            raise ImageSearchError(
                "Could not connect to Pexels: "
                f"{exc.reason}"
            ) from exc

        except TimeoutError as exc:
            raise ImageSearchError(
                "The Pexels request timed out."
            ) from exc

        except (
            json.JSONDecodeError,
            TypeError,
            ValueError,
        ) as exc:
            raise ImageSearchError(
                "Pexels returned an invalid response."
            ) from exc

    def _parse_results(
        self,
        payload,
        *,
        query,
    ):
        if not isinstance(payload, dict):
            raise ImageSearchError(
                "Pexels returned an invalid response."
            )

        photos = payload.get(
            "photos",
            [],
        )

        if not isinstance(photos, list):
            raise ImageSearchError(
                "Pexels returned an invalid photo list."
            )

        results = []

        for item in photos:
            result = self._parse_result(
                item,
                query=query,
            )

            if result is not None:
                results.append(
                    result
                )

        return results

    def _parse_result(
        self,
        item,
        *,
        query,
    ):
        if not isinstance(item, dict):
            return None

        source_images = item.get(
            "src",
            {},
        )

        if not isinstance(source_images, dict):
            return None

        preview_url = (
            source_images.get("medium")
            or source_images.get("small")
            or source_images.get("tiny")
            or ""
        )

        download_url = (
            source_images.get("large2x")
            or source_images.get("large")
            or source_images.get("original")
            or preview_url
        )

        if not preview_url or not download_url:
            return None

        creator = str(
            item.get("photographer")
            or "Unknown"
        )

        image_id = self._safe_int(
            item.get("id")
        )

        width = self._safe_int(
            item.get("width")
        )

        height = self._safe_int(
            item.get("height")
        )

        alt_text = str(
            item.get("alt")
            or ""
        ).strip()

        tags = (
            alt_text
            or query
        )

        return ImageSearchResult(
            image_id=image_id,
            preview_url=str(
                preview_url
            ),
            download_url=str(
                download_url
            ),
            page_url=str(
                item.get("url")
                or ""
            ),
            creator=creator,
            creator_url=str(
                item.get("photographer_url")
                or ""
            ),
            tags=tags,
            width=width,
            height=height,
            provider=self.provider_name,
            attribution=(
                f"Photo by {creator} on Pexels"
            ),
        )

    @staticmethod
    def _normalise_page(page):
        try:
            return max(
                1,
                int(page),
            )
        except (
            TypeError,
            ValueError,
        ) as exc:
            raise ValueError(
                "The page number must be an integer."
            ) from exc

    @staticmethod
    def _normalise_per_page(per_page):
        try:
            value = int(
                per_page
            )
        except (
            TypeError,
            ValueError,
        ) as exc:
            raise ValueError(
                "The results-per-page value "
                "must be an integer."
            ) from exc

        return max(
            1,
            min(
                value,
                80,
            ),
        )

    @staticmethod
    def _safe_int(value):
        try:
            return int(
                value or 0
            )
        except (
            TypeError,
            ValueError,
        ):
            return 0

    @staticmethod
    def _read_http_error(exc):
        try:
            body = exc.read().decode(
                "utf-8",
                errors="replace",
            ).strip()
        except Exception:
            return ""

        if not body:
            return ""

        try:
            payload = json.loads(
                body
            )
        except (
            json.JSONDecodeError,
            TypeError,
        ):
            return body

        if not isinstance(payload, dict):
            return body

        return str(
            payload.get("error")
            or payload.get("message")
            or body
        )