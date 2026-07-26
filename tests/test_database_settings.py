def test_save_and_load_setting(database):
    database.save_setting(
        "projects_folder",
        "C:/FactVault/Projects",
    )

    value = database.load_setting(
        "projects_folder",
    )

    assert value == "C:/FactVault/Projects"


def test_save_setting_updates_existing_value(database):
    database.save_setting(
        "theme",
        "light",
    )

    database.save_setting(
        "theme",
        "dark",
    )

    value = database.load_setting(
        "theme",
    )

    assert value == "dark"


def test_load_setting_returns_empty_string_for_missing_key(database):
    value = database.load_setting(
        "missing-setting",
    )

    assert value == ""


def test_get_categories_returns_names_in_alphabetical_order(database):
    # Remove any default categories created during database setup
    database.conn.execute(
        "DELETE FROM categories"
    )
    database.conn.commit()

    database.conn.executemany(
        """
        INSERT INTO categories (name)
        VALUES (?)
        """,
        [
            ("Science",),
            ("History",),
            ("Art",),
        ],
    )
    database.conn.commit()

    categories = database.get_categories()

    assert categories == [
        "Art",
        "History",
        "Science",
    ]