from common.resolve_timeline_polish import (
    build_motion_plan,
    build_polish_plan,
    build_scene_markers,
    build_transition_plan,
    parse_srt_cues,
)


def test_parse_srt_cues_converts_timestamps_to_frames():
    cues = parse_srt_cues(
        "1\n00:00:01,000 --> 00:00:03,500\nTitanic fact\n",
        30,
    )

    assert len(cues) == 1
    assert cues[0].start_frame == 30
    assert cues[0].duration_frames == 75
    assert cues[0].text == "Titanic fact"


def test_build_scene_markers_uses_caption_then_visual_plan():
    markers = build_scene_markers(
        [
            {"index": 1, "start": 0, "caption": "Opening"},
            {"index": 2, "start": 2.5, "visual_plan": "Show archive photo"},
        ],
        30,
    )

    assert markers[0].frame == 0
    assert markers[0].name == "Scene 1"
    assert markers[0].note == "Opening"
    assert markers[1].frame == 75
    assert markers[1].note == "Show archive photo"


def test_build_motion_plan_adds_default_ken_burns_to_stills():
    motion = build_motion_plan({}, "Assets/Images/ship.jpg")

    assert motion["style"] == "slow_zoom_in"
    assert motion["start_zoom"] == 1.0
    assert motion["end_zoom"] == 1.08


def test_build_transition_plan_skips_first_scene_boundary():
    transitions = build_transition_plan(
        [
            {"index": 1},
            {"index": 2},
            {"index": 3, "transition": "dip_to_color"},
        ],
        30,
    )

    assert transitions == [
        {
            "before_scene": 2,
            "type": "cross_dissolve",
            "duration_frames": 8,
        },
        {
            "before_scene": 3,
            "type": "dip_to_color",
            "duration_frames": 8,
        },
    ]


def test_build_polish_plan_combines_tracks_markers_subtitles_and_motion():
    plan = build_polish_plan(
        {
            "fps": 30,
            "scenes": [
                {
                    "index": 1,
                    "start": 0,
                    "media_path": "Assets/Images/ship.png",
                    "caption": "Titanic",
                },
                {
                    "index": 2,
                    "start": 3,
                    "media_path": "Assets/Videos/ocean.mp4",
                },
            ],
        },
        "1\n00:00:00,000 --> 00:00:02,000\nTitanic\n",
    )

    assert plan["fps"] == 30
    assert len(plan["markers"]) == 2
    assert len(plan["subtitles"]) == 1
    assert len(plan["transitions"]) == 1
    assert plan["scenes"][0]["motion"]["style"] == "slow_zoom_in"
    assert plan["scenes"][1]["motion"]["style"] == "none"
    assert plan["track_layout"]["titles"] == 3
