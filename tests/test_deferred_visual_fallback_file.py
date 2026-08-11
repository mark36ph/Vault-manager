from common.asset_acquisition import AssetAcquisitionEngine, AssetCandidate
from common.verified_asset_acquisition import install_visual_verification


class Provider:
    name = "stock"

    def __init__(self, result):
        self.result = result

    def search(self, query, *, kind, limit):
        if self.result.kind != kind:
            return []
        return [self.result][:limit]


def test_deferred_decorative_fallback_remains_on_disk_across_retry_searches(tmp_path):
    repeated = AssetCandidate(
        provider="stock",
        id="same-decorative-result",
        url="https://example.test/redwood-symbolic.jpg",
        title="Redwood forest symbolic decorative composition",
        kind="image",
        score=100,
        width=1080,
        height=1920,
    )
    engine = AssetAcquisitionEngine(
        [Provider(repeated)],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class DecorativeVerifier:
        last_quality = "preferred"
        last_style = "decorative"
        last_subject_uncertain = False

        def __call__(self, _query, _asset):
            self.last_quality = "preferred"
            self.last_style = "decorative"
            self.last_subject_uncertain = False
            return True

    install_visual_verification(engine, DecorativeVerifier())
    result = engine.acquire("Nature redwood forest", tmp_path, attempts=1)

    assert result.candidate.id == "same-decorative-result"
    assert result.path.is_file()
    assert result.path.read_bytes() == b"image"
