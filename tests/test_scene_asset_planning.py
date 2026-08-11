from common.scene_asset_planning import clean_visual_query, plan_visual_queries


def test_cleans_numbered_and_bulleted_queries():
    assert clean_visual_query("1. Eiffel Tower summer") == "Eiffel Tower summer"
    assert clean_visual_query("- iron lattice close-up") == "iron lattice close-up"
    assert clean_visual_query("Image Prompts:") == ""


def test_plan_preserves_repeated_queries_in_scene_positions():
    script = "First sentence about Paris.\n\nSecond sentence about iron.\n\nThird sentence about summer heat."
    plan = plan_visual_queries(
        script,
        "1. Eiffel Tower Paris\n2. Eiffel Tower Paris",
        topic="Eiffel Tower",
    )
    assert plan.scene_count == 3
    assert len(plan.queries) == 3
    assert plan.queries[:2] == ("Eiffel Tower Paris", "Eiffel Tower Paris")
    assert "Third sentence about summer heat" in plan.queries[2]
    assert plan.generated_fallbacks == 1


def test_transition_scene_replaces_stale_repeat_with_destination_query():
    script = (
        "Yellowstone has an enormous volcanic caldera.\n\n"
        "But it is not the biggest volcanic system in the comparison.\n\n"
        "Mauna Loa is a broad shield volcano in Hawaii."
    )
    plan = plan_visual_queries(
        script,
        [
            "Yellowstone geothermal caldera landscape",
            "Yellowstone geothermal caldera landscape",
            "Mauna Loa Hawaii broad shield volcano",
        ],
        topic="Volcano comparison",
    )
    assert plan.queries == (
        "Yellowstone geothermal caldera landscape",
        "Mauna Loa Hawaii broad shield volcano",
        "Mauna Loa Hawaii broad shield volcano",
    )


def test_transition_scene_keeps_distinct_current_search():
    script = (
        "Earth has a dense atmosphere.\n\n"
        "However the comparison now shifts toward a dry red planetary surface.\n\n"
        "Mars has enormous volcanoes."
    )
    plan = plan_visual_queries(
        script,
        [
            "Earth blue planet atmosphere",
            "rocky red planet dry surface comparison",
            "Mars Olympus Mons volcano",
        ],
        topic="Planet comparison",
    )
    assert plan.queries[1] == "rocky red planet dry surface comparison"


def test_repeated_same_subject_without_transition_is_not_rewritten():
    script = (
        "The Eiffel Tower is made from iron.\n\n"
        "Its iron lattice contains thousands of metal pieces.\n\n"
        "Summer heat makes the iron expand."
    )
    plan = plan_visual_queries(
        script,
        [
            "Eiffel Tower Paris iron lattice",
            "Eiffel Tower Paris iron lattice",
            "Eiffel Tower summer heat iron expansion",
        ],
        topic="Eiffel Tower",
    )
    assert plan.queries[1] == "Eiffel Tower Paris iron lattice"


def test_plan_preserves_extra_distinct_queries():
    plan = plan_visual_queries("One scene only.", ["one", "two", "three"], topic="Topic")
    assert plan.queries == ("one", "two", "three")
