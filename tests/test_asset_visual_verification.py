import json

from common.asset_acquisition import AcquiredAsset, AssetAcquisitionEngine, AssetCandidate
from common.asset_visual_verification import OpenAIImageRelevanceVerifier
from common.verified_asset_acquisition import install_visual_verification


class Provider:
    name = "stock"

    def __init__(self, results):
        self.results = list(results)

    def search(self, query, *, kind, limit):
        return [item for item in self.results if item.kind == kind][:limit]


def candidate(identifier, title, score, url):
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=url,
        title=title,
        kind="image",
        score=score,
        width=1080,
        height=1920,
    )


def test_openai_visual_verifier_sends_downloaded_image_and_accepts(tmp_path):
    image = tmp_path / "venus.jpg"
    image.write_bytes(b"fake-jpeg-bytes")
    asset = AcquiredAsset(
        candidate=candidate(
            "venus",
            "Venus cloudy planet",
            1,
            "https://example.test/venus.jpg",
        ),
        path=image,
    )
    requests = []

    def transport(request):
        requests.append(request)
        body = json.loads(request.data.decode("utf-8"))
        content = body["input"][0]["content"]
        assert body["model"] == "gpt-5-mini"
        assert content[0]["type"] == "input_text"
        assert "Space Venus planet rotation" in content[0]["text"]
        assert content[1]["type"] == "input_image"
        assert content[1]["image_url"].startswith("data:image/jpeg;base64,")
        return {"output_text": "ACCEPT"}

    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        model="gpt-5-mini",
        transport=transport,
    )

    assert verifier("Space Venus planet rotation", asset) is True
    assert len(requests) == 1


def test_visual_verification_rejects_bad_candidate_and_tries_next(tmp_path):
    bad = candidate(
        "dragon",
        "Venus planet illustration",
        100,
        "https://example.test/dragon.jpg",
    )
    good = candidate(
        "venus",
        "Venus planet clouds",
        1,
        "https://example.test/venus.jpg",
    )
    provider = Provider([bad, good])

    engine = AssetAcquisitionEngine(
        [provider],
        downloader=lambda url, path: path.write_bytes(b"image"),
    )
    checked = []

    def verifier(query, asset):
        checked.append((query, asset.candidate.id))
        return asset.candidate.id == "venus"

    install_visual_verification(engine, verifier)
    result = engine.acquire("Space Venus planet", tmp_path, attempts=2)

    assert result.candidate.id == "venus"
    assert checked == [
        ("Space Venus planet", "dragon"),
        ("Space Venus planet", "venus"),
    ]
