from unittest.mock import Mock

import pytest

from image_providers.pixabay import PixabayProvider
from image_search import (
    ImageSearchError,
    get_provider,
)


def test_get_provider_returns_pixabay_provider():
    settings = Mock()

    settings.get.return_value = (
        "test-api-key"
    )

    provider = get_provider(
        "Pixabay",
        settings,
    )

    assert isinstance(
        provider,
        PixabayProvider,
    )

    assert provider.api_key == (
        "test-api-key"
    )


def test_get_provider_is_case_insensitive():
    settings = Mock()

    settings.get.return_value = (
        "test-api-key"
    )

    provider = get_provider(
        "pixabay",
        settings,
    )

    assert isinstance(
        provider,
        PixabayProvider,
    )


def test_get_provider_rejects_unknown_provider():
    settings = Mock()

    with pytest.raises(
        ImageSearchError,
        match="Unsupported .* provider",
    ):
        get_provider(
            "Unknown",
            settings,
        )