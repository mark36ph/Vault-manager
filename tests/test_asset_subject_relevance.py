from common.asset_acquisition import AssetAcquisitionEngine, AssetCandidate


class QueryProvider:
    name = "stock"

    def __init__(self):
        self.calls = []

    def search(self, query, *, kind, limit):
        self.calls.append(query)
        if query == "Space Venus planet rotation":
            return [
                AssetCandidate(
                    provider="stock",
                    id="earth",
                    url="https://example.test/earth.jpg",
                    title="Earth planet glowing in outer space",
                    kind=kind,
                    score=1000,
                    width=1080,
                    height=1920,
                ),
                AssetCandidate(
                    provider="stock",
                    id="venus",
                    url="https://example.test/venus.jpg",
                    title="Venus planet atmosphere in space",
                    kind=kind,
                    score=1,
                    width=1080,
                    height=1920,
                ),
            ]
        if query in {"Space Venus planet", "Venus planet", "Venus"}:
            return [
                AssetCandidate(
                    provider="stock",
                    id="venus-fallback",
                    url="https://example.test/venus-fallback.jpg",
                    title="Venus cloudy planet",
                    kind=kind,
                    score=1,
                    width=1080,
                    height=1920,
                )
            ]
        return []


def test_search_requires_concrete_subject_over_popular_generic_result():
    provider = QueryProvider()
    engine = AssetAcquisitionEngine([provider])

    results = engine.search("Space Venus planet rotation")

    assert [item.id for item in results] == ["venus"]


def test_acquire_tries_subject_preserving_fallbacks_before_generic(tmp_path):
    class GenericThenSubjectProvider(QueryProvider):
        def search(self, query, *, kind, limit):
            self.calls.append(query)
            if query == "Space Venus rotating planet":
                return [
                    AssetCandidate(
                        provider="stock",
                        id="earth",
                        url="https://example.test/earth.jpg",
                        title="Earth planet in space",
                        kind=kind,
                        score=500,
                    )
                ]
            if query == "Venus planet":
                return [
                    AssetCandidate(
                        provider="stock",
                        id="venus",
                        url="https://example.test/venus.jpg",
                        title="Venus planet clouds",
                        kind=kind,
                        score=1,
                    )
                ]
            return []

    provider = GenericThenSubjectProvider()
    engine = AssetAcquisitionEngine(
        [provider],
        downloader=lambda _url, path: path.write_bytes(b"asset"),
    )

    result = engine.acquire("Space Venus rotating planet", tmp_path)

    assert result.candidate.id == "venus"
    assert "Venus planet" in provider.calls
    assert provider.calls.index("Venus planet") > provider.calls.index("Space Venus rotating planet")
