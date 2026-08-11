import pytest

from common.asset_acquisition import AssetCandidate
from common.named_asset_hierarchy import (
    NAMED_IDENTITY_BONUS,
    NAMED_SEARCH_BONUS,
    named_candidate_bonus,
    named_candidate_rank_tier,
    named_identity_evidence,
)


def candidate(*, provider="pexels", title="", source_page="", kind="image", metadata=None):
    values = {"source_page": source_page}
    values.update(metadata or {})
    return AssetCandidate(
        provider=provider,
        id="1",
        url="https://cdn.example.test/asset.jpg",
        kind=kind,
        title=title,
        metadata=values,
    )


def test_full_named_subject_in_provider_page_is_strong_evidence():
    item = candidate(
        source_page="https://www.pexels.com/video/aerial-view-of-mauna-loa-12345/",
    )
    assert named_identity_evidence(
        "nature Mauna Loa Hawaii shield volcano aerial",
        item,
    ) is True


def test_query_echo_title_is_not_identity_evidence():
    query = "nature Mauna Loa Hawaii shield volcano aerial"
    item = candidate(title=query, source_page="https://www.pexels.com/video/volcano-12345/")
    assert named_identity_evidence(query, item) is False


def test_focused_entity_query_echo_title_is_not_identity_evidence():
    query = "nature Mauna Loa Hawaii shield volcano aerial"
    item = candidate(title="Mauna Loa", source_page="https://www.pexels.com/video/volcano-12345/")
    assert named_identity_evidence(query, item) is False


def test_provider_tags_can_support_full_named_subject():
    item = candidate(
        provider="pixabay",
        title="mauna loa, hawaii, shield volcano, lava",
        source_page="https://pixabay.com/videos/id-123/",
    )
    assert named_identity_evidence(
        "nature Mauna Loa Hawaii shield volcano aerial",
        item,
    ) is True


def test_partial_name_is_not_enough_for_multiword_entity():
    item = candidate(
        provider="pixabay",
        title="mauna, volcano, hawaii",
        source_page="https://pixabay.com/videos/id-123/",
    )
    assert named_identity_evidence(
        "nature Mauna Loa Hawaii shield volcano aerial",
        item,
    ) is False


@pytest.mark.parametrize(
    ("query", "provider_text"),
    [
        (
            "nature Mauna Loa Hawaii shield volcano aerial",
            "https://stock.example/mauna-loa-hawaii-volcano",
        ),
        (
            "nature Mount Everest Himalayas summit mountain Nepal",
            "https://stock.example/mount-everest-nepal-summit",
        ),
        (
            "architecture Golden Gate Bridge San Francisco aerial",
            "https://stock.example/golden-gate-bridge-san-francisco",
        ),
        (
            "technology James Webb Space Telescope deep field",
            "https://stock.example/james-webb-space-telescope-observatory",
        ),
    ],
)
def test_named_entities_receive_strong_tier_from_provider_evidence(query, provider_text):
    item = candidate(source_page=provider_text)
    assert named_candidate_rank_tier(query, item) == 2
    assert named_candidate_bonus(query, item) == NAMED_IDENTITY_BONUS


def test_plausible_focused_named_result_beats_generic_same_class():
    query = "nature Mauna Loa Hawaii shield volcano aerial"
    plausible_named = candidate(
        title="Mauna Loa",
        source_page="https://stock.example/volcano-123",
        metadata={"_named_subject_search": "Mauna Loa"},
    )
    generic = candidate(
        title="dramatic geothermal volcanic landscape",
        source_page="https://stock.example/volcano-456",
    )

    assert named_candidate_rank_tier(query, plausible_named) == 1
    assert named_candidate_rank_tier(query, generic) == 0
    assert named_candidate_bonus(query, plausible_named) == NAMED_SEARCH_BONUS
    assert named_candidate_bonus(query, generic) == 0


@pytest.mark.parametrize("kind", ["video", "image"])
def test_video_and_image_candidates_use_same_named_hierarchy(kind):
    query = "architecture Golden Gate Bridge San Francisco aerial"
    strong = candidate(
        kind=kind,
        source_page="https://stock.example/golden-gate-bridge-san-francisco",
    )
    plausible = candidate(
        kind=kind,
        title="Golden Gate Bridge",
        metadata={"_named_subject_search": "Golden Gate Bridge"},
    )
    generic = candidate(kind=kind, title="large suspension bridge")

    assert [
        named_candidate_rank_tier(query, strong),
        named_candidate_rank_tier(query, plausible),
        named_candidate_rank_tier(query, generic),
    ] == [2, 1, 0]


def test_named_tier_bonuses_are_decisive_over_visual_quality_scores():
    # Mixed verification currently spans roughly -3..9 points. The hierarchy
    # bonuses must remain larger than that range so prettier generic footage
    # cannot outrank a surviving named-subject candidate.
    assert NAMED_IDENTITY_BONUS > NAMED_SEARCH_BONUS > 9


def test_generic_query_has_no_named_identity_requirement():
    item = candidate(
        title="fiber optic cable data center",
        metadata={"_named_subject_search": "fiber optic cable"},
    )
    query = "technology fiber optic cable light data"
    assert named_identity_evidence(query, item) is False
    assert named_candidate_rank_tier(query, item) == 0
    assert named_candidate_bonus(query, item) == 0
