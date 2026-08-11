from common.asset_acquisition import AssetAcquisitionEngine, AssetCandidate
from common.verified_asset_acquisition import install_visual_verification


class RepeatingProvider:
    name = "stock"

    def __init__(self, candidate):
        self.candidate = candidate
        self.search_calls = []

    def search(self, query, *, kind, limit):
        self.search_calls.append(query)
        if self.candidate.kind != kind:
            return []
        return [self.candidate]


def test_duplicate_candidate_is_downloaded_and_verified_once_across_fallback_searches(tmp_path):
    repeated = AssetCandidate(
        provider="stock",
        id="repeat",
        url="https://example.test/repeat.jpg",
        title="Generic symbolic science visual",
        kind="image",
        score=10,
        width=1080,
        height=1920,
    )
    provider = RepeatingProvider(repeated)
    downloads = []

    def downloader(url, path):
        downloads.append(url)
        path.write_bytes(b"image")

    engine = AssetAcquisitionEngine([provider], downloader=downloader)

    class DecorativeVerifier:
        def __init__(self):
            self.calls = 0
            self.last_quality = "weak"
            self.last_style = "decorative"
            self.last_subject_uncertain = False

        def __call__(self, _query, _asset):
            self.calls += 1
            self.last_quality = "weak"
            self.last_style = "decorative"
            self.last_subject_uncertain = False
            return True

    verifier = DecorativeVerifier()
    install_visual_verification(engine, verifier)

    result = engine.acquire("Science energy transfer process", tmp_path, attempts=1)

    assert result.candidate.id == "repeat"
    assert result.path.is_file()
    assert downloads == [repeated.url]
    assert verifier.calls == 1
    assert len(provider.search_calls) > 1
