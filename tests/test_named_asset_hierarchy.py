from common.asset_acquisition import AssetCandidate
from common.named_asset_hierarchy import named_identity_evidence


def candidate(*, provider="pexels", title="", source_page=""):
    return AssetCandidate(
        provider=provider,
        id="1",
        url="https://cdn.example.test/asset.jpg",
        title=title,
        metadata={"source_page": source_page},
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


def test_generic_query_has_no_named_identity_requirement():
    item = candidate(title="fiber optic cable data center")
    assert named_identity_evidence(
        "technology fiber optic cable light data",
        item,
    ) is False
