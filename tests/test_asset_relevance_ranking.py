from common.asset_acquisition import AssetAcquisitionEngine, AssetCandidate


class Provider:
    name = "stock"

    def __init__(self, results):
        self.results = list(results)

    def search(self, query, *, kind, limit):
        return self.results


def item(identifier, title, score, url):
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=url,
        title=title,
        score=score,
        width=1080,
        height=1920,
        kind="image",
    )


def test_scene_subject_beats_more_popular_generic_candidate():
    earth = item(
        "earth",
        "Earth planet glowing in outer space",
        5000,
        "https://example.test/earth.jpg",
    )
    venus = item(
        "venus",
        "Venus planet atmosphere in space",
        1,
        "https://example.test/venus.jpg",
    )

    results = AssetAcquisitionEngine([Provider([earth, venus])]).search(
        "Space Venus planet rotation"
    )

    assert [candidate.id for candidate in results] == ["venus"]


def test_scene_subject_filters_keyword_distractor_when_matching_candidate_exists():
    ufo = item(
        "ufo",
        "UFO alien spacecraft above distant planet in space",
        999,
        "https://example.test/ufo.jpg",
    )
    venus = item(
        "venus",
        "Venus cloudy planet surface astronomy",
        0,
        "https://example.test/venus.jpg",
    )

    results = AssetAcquisitionEngine([Provider([ufo, venus])]).search(
        "Space Venus planet surface"
    )

    assert [candidate.id for candidate in results] == ["venus"]


def test_search_keeps_fallback_candidates_when_no_subject_match_exists():
    earth = item(
        "earth",
        "Earth planet in space",
        3,
        "https://example.test/earth.jpg",
    )

    results = AssetAcquisitionEngine([Provider([earth])]).search(
        "Space Venus planet rotation"
    )

    assert [candidate.id for candidate in results] == ["earth"]
