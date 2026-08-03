from common.scene_asset_planning import clean_visual_query, plan_visual_queries


def test_cleans_numbered_and_bulleted_queries():
    assert clean_visual_query("1. Eiffel Tower summer") == "Eiffel Tower summer"
    assert clean_visual_query("- iron lattice close-up") == "iron lattice close-up"
    assert clean_visual_query("Image Prompts:") == ""


def test_plan_produces_one_query_per_scene_and_removes_duplicates():
    script = "First sentence about Paris.\n\nSecond sentence about iron.\n\nThird sentence about summer heat."
    plan = plan_visual_queries(
        script,
        "1. Eiffel Tower Paris\n2. Eiffel Tower Paris",
        topic="Eiffel Tower",
    )
    assert plan.scene_count == 3
    assert len(plan.queries) == 3
    assert len({query.casefold() for query in plan.queries}) == 3
    assert plan.generated_fallbacks == 2


def test_plan_preserves_extra_distinct_queries():
    plan = plan_visual_queries("One scene only.", ["one", "two", "three"], topic="Topic")
    assert plan.queries == ("one", "two", "three")
