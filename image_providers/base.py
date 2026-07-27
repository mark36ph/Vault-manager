from abc import ABC, abstractmethod


class ImageProvider(ABC):
    """Base class for image-search providers."""

    provider_name = ""

    @abstractmethod
    def search(
        self,
        query,
        *,
        page=1,
        per_page=20,
        orientation="all",
    ):
        """Search the provider and return image results."""
        raise NotImplementedError

    def notify_download(self, result):
        """
        Perform provider-specific download notification.

        Most providers do not require this. Unsplash will override it.
        """
        return None