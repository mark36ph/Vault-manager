from types import SimpleNamespace

from common.provider_setup import _anchor_imported_searches


def context(topic, category):
    return SimpleNamespace(topic=topic, project={"category": category})


def test_existing_common_subject_is_not_injected_twice():
    prompts, category = _anchor_imported_searches(
        ["wombat walking wildlife close up"],
        context("Wombat Poop Is Cube-Shaped", "Nature"),
    )

    assert category == "Nature"
    assert prompts == ["Nature wombat walking wildlife close up"]
    assert "Wombat wombat" not in prompts[0]


def test_later_named_location_does_not_hide_existing_common_subject():
    prompts, _ = _anchor_imported_searches(
        ["wombat close up Australia wildlife"],
        context("Wombat Poop Is Cube-Shaped", "Nature"),
    )

    assert prompts == ["Nature wombat close up Australia wildlife"]


def test_different_named_subject_at_query_start_still_overrides_topic_subject():
    prompts, _ = _anchor_imported_searches(
        ["Mauna Loa Hawaii broad shield volcano"],
        context("Yellowstone Isn't the Biggest Volcano", "Nature"),
    )

    assert prompts == ["Nature Mauna Loa Hawaii broad shield volcano"]
    assert "Yellowstone" not in prompts[0]


def test_generic_imported_query_still_receives_project_subject():
    prompts, _ = _anchor_imported_searches(
        ["rotating planet cloud layers"],
        context("Venus Spins Backwards", "Space"),
    )

    assert prompts == ["Space Venus rotating planet cloud layers"]
