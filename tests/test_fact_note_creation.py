def test_add_fact_note_stores_all_fields(database):
    database.add_fact_note(
        title="Gravity",
        category="Science",
        notes="Objects attract each other.",
        status="Idea",
        created="2026-01-01",
    )

    notes = database.get_fact_notes()

    assert len(notes) == 1

    note = notes[0]

    assert note["title"] == "Gravity"
    assert note["category"] == "Science"
    assert note["notes"] == "Objects attract each other."
    assert note["status"] == "Idea"
    assert note["created"] == "2026-01-01"


def test_get_fact_notes_returns_newest_first(database):
    database.add_fact_note(
        title="First",
        category="Science",
        notes="",
        status="Idea",
        created="2026-01-01",
    )

    database.add_fact_note(
        title="Second",
        category="Science",
        notes="",
        status="Idea",
        created="2026-01-02",
    )

    notes = database.get_fact_notes()

    assert [note["title"] for note in notes] == [
        "Second",
        "First",
    ]

