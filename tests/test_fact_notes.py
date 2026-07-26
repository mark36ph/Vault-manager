from datetime import datetime


def insert_fact_note(database, checked=0):
    cursor = database.conn.execute(
        """
        INSERT INTO fact_notes (
            title,
            created,
            checked
        )
        VALUES (?, ?, ?)
        """,
        (
            "Test fact note",
            datetime.now().isoformat(),
            checked,
        ),
    )
    database.conn.commit()

    return cursor.lastrowid


def get_fact_note(database, note_id):
    return database.conn.execute(
        """
        SELECT *
        FROM fact_notes
        WHERE id=?
        """,
        (note_id,),
    ).fetchone()


def test_toggle_fact_note_checked_marks_unchecked_note_as_checked(
    database,
):
    note_id = insert_fact_note(
        database,
        checked=0,
    )

    database.toggle_fact_note_checked(note_id)

    note = get_fact_note(database, note_id)

    assert note["checked"] == 1


def test_toggle_fact_note_checked_marks_checked_note_as_unchecked(
    database,
):
    note_id = insert_fact_note(
        database,
        checked=1,
    )

    database.toggle_fact_note_checked(note_id)

    note = get_fact_note(database, note_id)

    assert note["checked"] == 0


def test_toggle_fact_note_checked_ignores_missing_note(
    database,
):
    before = database.conn.total_changes

    database.toggle_fact_note_checked(999999)

    after = database.conn.total_changes

    assert after == before

def get_fact_note(database, note_id):
    return database.conn.execute(
        """
        SELECT *
        FROM fact_notes
        WHERE id=?
        """,
        (note_id,),
    ).fetchone()

def test_update_fact_note_changes_editable_fields(database):
    database.add_fact_note(
        title="Original title",
        category="Science",
        notes="Original notes",
        status="Idea",
        created="2026-01-01",
    )

    note = database.get_fact_notes()[0]

    database.update_fact_note(
        note["id"],
        title="Updated title",
        category="History",
        notes="Updated notes",
        status="Complete",
    )

    updated = get_fact_note(
        database,
        note["id"],
    )

    assert updated["title"] == "Updated title"
    assert updated["category"] == "History"
    assert updated["notes"] == "Updated notes"
    assert updated["status"] == "Complete"
    assert updated["created"] == "2026-01-01"

def test_toggle_fact_note_pinned_marks_note_as_pinned(database):
    database.add_fact_note(
        title="Pinned note",
        category="Testing",
        notes="",
        status="Idea",
        created="2026-01-01",
    )

    note = database.get_fact_notes()[0]

    database.toggle_fact_note_pinned(note["id"])

    updated = get_fact_note(
        database,
        note["id"],
    )

    assert updated["pinned"] == 1


def test_toggle_fact_note_pinned_marks_note_as_unpinned(database):
    database.add_fact_note(
        title="Unpinned note",
        category="Testing",
        notes="",
        status="Idea",
        created="2026-01-01",
    )

    note = database.get_fact_notes()[0]

    database.toggle_fact_note_pinned(note["id"])
    database.toggle_fact_note_pinned(note["id"])

    updated = get_fact_note(
        database,
        note["id"],
    )

    assert updated["pinned"] == 0


def test_toggle_fact_note_pinned_ignores_missing_note(database):
    before = database.conn.total_changes

    database.toggle_fact_note_pinned(999999)

    after = database.conn.total_changes

    assert after == before

def test_delete_fact_note_removes_note(database):
    database.add_fact_note(
        title="Delete me",
        category="Testing",
        notes="",
        status="Idea",
        created="2026-01-01",
    )

    note = database.get_fact_notes()[0]

    database.delete_fact_note(note["id"])

    deleted = get_fact_note(
        database,
        note["id"],
    )

    assert deleted is None