from common.asset_acquisition import AssetAcquisitionEngine, AssetCandidate
from common.verified_asset_acquisition import install_visual_verification


class Provider:
    name = "stock"

    def __init__(self, results):
        self.results = list(results)

    def search(self, query, *, kind, limit):
        return [item for item in self.results if item.kind == kind][:limit]


def candidate(identifier, title, score):
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=f"https://example.test/{identifier}.jpg",
        title=title,
        kind="image",
        score=score,
        width=1080,
        height=1920,
    )


class QualityVerifier:
    def __init__(self, qualities):
        self.qualities = dict(qualities)
        self.last_quality = "preferred"
        self.checked = []

    def __call__(self, _query, asset):
        identifier = asset.candidate.id
        self.checked.append(identifier)
        self.last_quality = self.qualities[identifier]
        return True


def test_visual_selection_compares_all_scanned_candidates_and_picks_best(tmp_path):
    candidates = [
        candidate("weak", "Redwood forest symbolic graphic", 100),
        candidate("acceptable", "Redwood forest illustration", 90),
        candidate("preferred", "Redwood forest photograph", 80),
        candidate("later-weak", "Redwood forest icon composition", 70),
    ]
    engine = AssetAcquisitionEngine(
        [Provider(candidates)],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    verifier = QualityVerifier(
        {
            "weak": "weak",
            "acceptable": "acceptable",
            "preferred": "preferred",
            "later-weak": "weak",
        }
    )
    install_visual_verification(engine, verifier)

    result = engine.acquire("Nature redwood forest", tmp_path, attempts=2)

    assert result.candidate.id == "preferred"
    assert verifier.checked == ["weak", "acceptable", "preferred", "later-weak"]
    assert sorted(path.name for path in tmp_path.iterdir()) == [result.path.name]


def test_visual_selection_uses_best_available_when_no_preferred_candidate_exists(tmp_path):
    candidates = [
        candidate("weak", "Steam turbine symbolic graphic", 100),
        candidate("acceptable", "Steam turbine technical illustration", 90),
        candidate("weak-two", "Steam turbine icon composition", 80),
    ]
    engine = AssetAcquisitionEngine(
        [Provider(candidates)],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    verifier = QualityVerifier(
        {
            "weak": "weak",
            "acceptable": "acceptable",
            "weak-two": "weak",
        }
    )
    install_visual_verification(engine, verifier)

    result = engine.acquire("Technology steam turbine", tmp_path, attempts=1)

    assert result.candidate.id == "acceptable"
    assert verifier.checked == ["weak", "acceptable", "weak-two"]
