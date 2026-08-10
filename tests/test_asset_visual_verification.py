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


def asset_for(tmp_path, identifier="venus", title="Venus cloudy planet"):
    image = tmp_path / f"{identifier}.jpg"
    image.write_bytes(b"fake-jpeg-bytes")
    return AcquiredAsset(
        candidate=candidate(
            identifier,
            title,
            1,
            f"https://example.test/{identifier}.jpg",
        ),
        path=image,
    )


def test_openai_visual_verifier_sends_downloaded_image_and_accepts(tmp_path):
    asset = asset_for(tmp_path)
    requests = []

    def transport(request):
        requests.append(request)
        body = json.loads(request.data.decode("utf-8"))
        content = body["input"][0]["content"]
        schema = body["text"]["format"]
        assert body["model"] == "gpt-5-mini"
        assert body["max_output_tokens"] == 300
        assert schema["type"] == "json_schema"
        assert schema["strict"] is True
        assert schema["schema"]["required"] == ["decision", "confidence", "reason"]
        assert content[0]["type"] == "input_text"
        assert "Space Venus planet rotation" in content[0]["text"]
        assert "dragons" in content[0]["text"]
        assert "UNCERTAIN" in content[0]["text"]
        assert content[1]["type"] == "input_image"
        assert content[1]["detail"] == "high"
        assert content[1]["image_url"].startswith("data:image/jpeg;base64,")
        return {
            "output_text": json.dumps(
                {
                    "decision": "ACCEPT",
                    "confidence": 0.96,
                    "reason": "The image visibly shows Venus.",
                }
            )
        }

    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        model="gpt-5-mini",
        transport=transport,
    )

    assert verifier("Space Venus planet rotation", asset) is True
    assert len(requests) == 1


def test_openai_visual_verifier_reads_nested_responses_output(tmp_path):
    asset = asset_for(tmp_path)
    decision = json.dumps(
        {
            "decision": "REJECT",
            "confidence": 0.99,
            "reason": "The image shows a dragon, not Venus.",
        }
    )
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "status": "completed",
            "output": [
                {
                    "type": "message",
                    "content": [{"type": "output_text", "text": decision}],
                }
            ],
        },
    )

    assert verifier("Space Venus planet rotation", asset) is False


def test_openai_visual_verifier_rejects_uncertain_and_low_confidence_accepts(tmp_path):
    asset = asset_for(tmp_path)
    responses = iter(
        [
            {"decision": "UNCERTAIN", "confidence": 0.91, "reason": "Could be another planet."},
            {"decision": "ACCEPT", "confidence": 0.62, "reason": "Probably Venus."},
        ]
    )

    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {"output_text": json.dumps(next(responses))},
    )

    assert verifier("Space Venus planet rotation", asset) is False
    assert verifier("Space Venus planet rotation", asset) is False


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
    assert not any("dragon" in path.name.casefold() for path in tmp_path.iterdir())


def test_visual_verification_error_rejects_candidate_and_tries_next(tmp_path):
    bad = candidate(
        "broken-verifier",
        "Venus planet illustration",
        100,
        "https://example.test/broken.jpg",
    )
    good = candidate(
        "venus",
        "Venus planet clouds",
        1,
        "https://example.test/venus.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([bad, good])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    checked = []

    def verifier(_query, asset):
        checked.append(asset.candidate.id)
        if asset.candidate.id == "broken-verifier":
            raise RuntimeError("verifier unavailable")
        return True

    install_visual_verification(engine, verifier)
    result = engine.acquire("Space Venus planet", tmp_path, attempts=2)

    assert result.candidate.id == "venus"
    assert checked == ["broken-verifier", "venus"]
