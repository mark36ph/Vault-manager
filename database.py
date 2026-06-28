import sqlite3
from pathlib import Path


DB_PATH = Path("data") / "factvault.db"


class Database:

    def __init__(self):
        DB_PATH.parent.mkdir(exist_ok=True)

        self.conn = sqlite3.connect(DB_PATH)
        self.conn.row_factory = sqlite3.Row

        self.create_tables()
        self.migrate_database()
        self.create_default_categories()

    # -------------------------------------------------
    # Tables
    # -------------------------------------------------

    def create_tables(self):

        self.conn.execute("""
        CREATE TABLE IF NOT EXISTS projects (

            id INTEGER PRIMARY KEY AUTOINCREMENT,

            title TEXT NOT NULL,

            category TEXT NOT NULL,

            status TEXT NOT NULL,

            folder TEXT NOT NULL,

            created TEXT NOT NULL,

            script TEXT DEFAULT '',

            description TEXT DEFAULT '',

            pinned_comment TEXT DEFAULT '',

            notes TEXT DEFAULT '',

            views INTEGER DEFAULT 0,

            likes INTEGER DEFAULT 0,

            upload_date TEXT DEFAULT '',

            youtube_url TEXT DEFAULT ''

        )
        """)

        self.conn.execute("""
        CREATE TABLE IF NOT EXISTS settings (

            key TEXT PRIMARY KEY,

            value TEXT

        )
        """)

        self.conn.execute("""
        CREATE TABLE IF NOT EXISTS categories (

            id INTEGER PRIMARY KEY AUTOINCREMENT,

            name TEXT UNIQUE

        )
        """)

        self.conn.commit()



    def migrate_database(self):

        columns = [row["name"] for row in self.conn.execute(
            "PRAGMA table_info(projects)"
        ).fetchall()]

        additions = {

            "script": "TEXT DEFAULT ''",
            "description": "TEXT DEFAULT ''",
            "pinned_comment": "TEXT DEFAULT ''",
            "notes": "TEXT DEFAULT ''",
            "views": "INTEGER DEFAULT 0",
            "likes": "INTEGER DEFAULT 0",
            "upload_date": "TEXT DEFAULT ''",
            "youtube_url": "TEXT DEFAULT ''"

        }

        for name, sql_type in additions.items():

            if name not in columns:

                self.conn.execute(
                    f"ALTER TABLE projects ADD COLUMN {name} {sql_type}"
                )

        self.conn.commit()


    # -------------------------------------------------
    # Default Categories
    # -------------------------------------------------

    def create_default_categories(self):

        categories = [

            "Weather",
            "History",
            "Science",
            "Animals",
            "Space",
            "Sports",
            "Money",
            "Technology",
            "Geography",
            "Haunted",
            "Food",
            "People",
            "Misc"

        ]

        for category in categories:

            self.conn.execute(

                "INSERT OR IGNORE INTO categories(name) VALUES(?)",

                (category,)

            )

        self.conn.commit()

    # -------------------------------------------------
    # Projects
    # -------------------------------------------------

    def add_project(
        self,
        title,
        category,
        status,
        folder,
        created
    ):

        self.conn.execute("""

        INSERT INTO projects(

            title,
            category,
            status,
            folder,
            created

        )

        VALUES(?,?,?,?,?)

        """,

        (

            title,
            category,
            status,
            folder,
            created

        ))

        self.conn.commit()

    def get_projects(self):

        return self.conn.execute(

            "SELECT * FROM projects ORDER BY id DESC"

        ).fetchall()

    def get_project(self, project_id):

        return self.conn.execute(

            "SELECT * FROM projects WHERE id=?",

            (project_id,)

        ).fetchone()

    def count_projects(self):

        return self.conn.execute(

            "SELECT COUNT(*) FROM projects"

        ).fetchone()[0]

    def delete_project(self, project_id):

        self.conn.execute(

            "DELETE FROM projects WHERE id=?",

            (project_id,)

        )

        self.conn.commit()

    # -------------------------------------------------
    # Settings
    # -------------------------------------------------

    def save_setting(self, key, value):

        self.conn.execute("""

        INSERT INTO settings(key,value)

        VALUES(?,?)

        ON CONFLICT(key)

        DO UPDATE SET

        value=excluded.value

        """,

        (

            key,
            value

        ))

        self.conn.commit()

    def load_setting(self, key):

        row = self.conn.execute(

            "SELECT value FROM settings WHERE key=?",

            (key,)

        ).fetchone()

        if row:

            return row["value"]

        return ""

    # -------------------------------------------------
    # Categories
    # -------------------------------------------------

    def get_categories(self):

        rows = self.conn.execute(

            "SELECT name FROM categories ORDER BY name"

        ).fetchall()

        return [row["name"] for row in rows]

    # -------------------------------------------------
    # Update
    # -------------------------------------------------

    def update_project(
        self,
        project_id,
        title,
        category,
        status,
        script,
        description,
        pinned_comment,
        notes
    ):

        self.conn.execute("""

            UPDATE projects

            SET

                title=?,
                category=?,
                status=?,
                script=?,
                description=?,
                pinned_comment=?,
                notes=?

            WHERE id=?

        """,

        (

            title,
            category,
            status,
            script,
            description,
            pinned_comment,
            notes,
            project_id

        ))

        self.conn.commit()


    # -------------------------------------------------
    # Close
    # -------------------------------------------------

    def close(self):

        self.conn.close()