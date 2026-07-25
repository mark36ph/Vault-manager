import sqlite3
from pathlib import Path
from datetime import datetime

DB_PATH = Path("data") / "factvault.db"


class Database:

    def __init__(self):
        DB_PATH.parent.mkdir(exist_ok=True)

        self.conn = sqlite3.connect(DB_PATH)
        self.conn.row_factory = sqlite3.Row

        self.create_tables()
        self.migrate_database()
        self.migrate_legacy_project_files()
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

            youtube_url TEXT DEFAULT '',

            pinned INTEGER DEFAULT 0,
            updated TEXT DEFAULT ''


        )
        """)

        self.conn.execute("""
        CREATE TABLE IF NOT EXISTS fact_notes (

            id INTEGER PRIMARY KEY AUTOINCREMENT,

            title TEXT NOT NULL,

            category TEXT DEFAULT '',

            notes TEXT DEFAULT '',

            status TEXT DEFAULT 'Idea',

            created TEXT NOT NULL,
            pinned INTEGER DEFAULT 0

        )
        """)

        self.conn.commit()
        
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
            "youtube_url": "TEXT DEFAULT ''",
            "pinned": "INTEGER DEFAULT 0",
            "updated": "TEXT DEFAULT ''",
            "scheduled_for": "TEXT DEFAULT ''",
            "on_screen_text": "TEXT DEFAULT ''",
            "visual_plan": "TEXT DEFAULT ''",
            "search_terms": "TEXT DEFAULT ''",
            "broll_plan": "TEXT DEFAULT ''",
            "thumbnail_prompt": "TEXT DEFAULT ''",
            "tags": "TEXT DEFAULT ''",
            "sources": "TEXT DEFAULT ''",
            "subtitle_text": "TEXT DEFAULT ''",
            "narration_duration": "REAL DEFAULT 0",
            "research_complete": "INTEGER DEFAULT 0",
            "script_complete": "INTEGER DEFAULT 0",
            "voice_complete": "INTEGER DEFAULT 0",
            "subtitles_complete": "INTEGER DEFAULT 0",
            "broll_complete": "INTEGER DEFAULT 0",
            "graphics_complete": "INTEGER DEFAULT 0",
            "capcut_complete": "INTEGER DEFAULT 0",
            "export_complete": "INTEGER DEFAULT 0",
            "upload_complete": "INTEGER DEFAULT 0"

        }

        for name, sql_type in additions.items():

            if name not in columns:

                self.conn.execute(
                    f"ALTER TABLE projects ADD COLUMN {name} {sql_type}"
                )

        note_columns = [row["name"] for row in self.conn.execute(
            "PRAGMA table_info(fact_notes)"
        ).fetchall()]

        note_additions = {

            "pinned": "INTEGER DEFAULT 0",
            "checked": "INTEGER DEFAULT 0"

        }

        for name, sql_type in note_additions.items():

            if name not in note_columns:

                self.conn.execute(
                    f"ALTER TABLE fact_notes ADD COLUMN {name} {sql_type}"
                )
        self.conn.commit()


    def migrate_legacy_project_files(self):
        """Import legacy text files into empty database fields once.

        Files are intentionally left in place as a safety backup. The app no
        longer reads or writes them after this migration.
        """
        file_map = {
            "Script.txt": "script",
            "On Screen Text.txt": "on_screen_text",
            "Visual Plan.txt": "visual_plan",
            "Description.txt": "description",
            "Pinned Comment.txt": "pinned_comment",
            "Notes.txt": "notes",
            "Tags.txt": "tags",
            "Sources.txt": "sources"
        }

        rows = self.conn.execute("SELECT * FROM projects").fetchall()
        for row in rows:
            folder_value = row["folder"] or ""
            if not folder_value:
                continue
            folder = Path(folder_value)
            if not folder.exists():
                continue

            updates = {}
            for filename, column in file_map.items():
                current = row[column] if column in row.keys() else ""
                if current:
                    continue
                path = folder / filename
                if not path.exists() or not path.is_file():
                    continue
                try:
                    value = path.read_text(encoding="utf-8").strip()
                except (OSError, UnicodeError):
                    continue
                if value:
                    updates[column] = value

            if updates:
                assignments = ", ".join(f"{name}=?" for name in updates)
                values = list(updates.values()) + [row["id"]]
                self.conn.execute(
                    f"UPDATE projects SET {assignments} WHERE id=?",
                    values
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
        created,
        script="",
        description="",
        pinned_comment="",
        notes="",
        updated=""
    ):
        if not updated:

            updated = created

        self.conn.execute("""

        INSERT INTO projects(

            title,
            category,
            status,
            folder,
            created,
            script,
            description,
            pinned_comment,
            notes,
            updated

        )

        VALUES(?,?,?,?,?,?,?,?,?,?)

        """,

        (

            title,
            category,
            status,
            folder,
            created,
            script,
            description,
            pinned_comment,
            notes,
            updated

        ))

        self.conn.commit()

    def toggle_fact_note_checked(self, note_id):

        note = self.conn.execute(

            "SELECT * FROM fact_notes WHERE id=?",

            (note_id,)

        ).fetchone()

        if note is None:
            return

        current = note["checked"] or 0

        new_value = 0 if current else 1

        self.conn.execute(

            "UPDATE fact_notes SET checked=? WHERE id=?",

            (
                new_value,
                note_id
            )

        )

        self.conn.commit()

    def get_projects(self):

        return self.conn.execute(

            "SELECT * FROM projects ORDER BY pinned DESC, id DESC"

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

    def count_projects_by_status(self, status):

        return self.conn.execute(
            "SELECT COUNT(*) FROM projects WHERE status=?",
            (status,)
        ).fetchone()[0]

    def delete_project(self, project_id):

        self.conn.execute(

            "DELETE FROM projects WHERE id=?",

            (project_id,)

        )

        self.conn.commit()

    def toggle_project_pinned(self, project_id):

        project = self.get_project(project_id)

        if project is None:
            return

        current = project["pinned"] or 0

        new_value = 0 if current else 1

        self.conn.execute(

            "UPDATE projects SET pinned=? WHERE id=?",

            (
                new_value,
                project_id
            )

        )

        self.conn.commit()
        
    # -------------------------------------------------
    # Fact Notes
    # -------------------------------------------------

    def add_fact_note(
        self,
        title,
        category,
        notes,
        status,
        created
    ):

        self.conn.execute("""

            INSERT INTO fact_notes(

                title,
                category,
                notes,
                status,
                created

            )

            VALUES(?,?,?,?,?)

        """,

        (
            title,
            category,
            notes,
            status,
            created
        ))

        self.conn.commit()

    def get_fact_notes(self):

        return self.conn.execute("""

            SELECT *
            FROM fact_notes
            ORDER BY pinned DESC, id DESC

        """).fetchall()

    def update_fact_note(
        self,
        note_id,
        title,
        category,
        notes,
        status
    ):

        self.conn.execute("""

            UPDATE fact_notes

            SET

                title=?,
                category=?,
                notes=?,
                status=?

            WHERE id=?

        """,

        (
            title,
            category,
            notes,
            status,
            note_id
        ))

        self.conn.commit()

    def toggle_fact_note_pinned(self, note_id):

        note = self.conn.execute(

            "SELECT * FROM fact_notes WHERE id=?",

            (note_id,)

        ).fetchone()

        if note is None:
            return

        current = note["pinned"] or 0

        new_value = 0 if current else 1

        self.conn.execute(

            "UPDATE fact_notes SET pinned=? WHERE id=?",

            (
                new_value,
                note_id
            )

        )

        self.conn.commit()
        
    def delete_fact_note(self, note_id):

        self.conn.execute(

            "DELETE FROM fact_notes WHERE id=?",

            (note_id,)

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
        folder,
        script,
        description,
        pinned_comment,
        notes,
        on_screen_text="",
        visual_plan="",
        search_terms="",
        broll_plan="",
        thumbnail_prompt="",
        tags="",
        sources="",
        subtitle_text="",
        narration_duration=0,
        pipeline=None
    ):
        updated = datetime.now().strftime("%Y-%m-%d %H:%M")
        pipeline = pipeline or {}

        self.conn.execute("""
            UPDATE projects
            SET title=?, category=?, status=?, folder=?, script=?,
                description=?, pinned_comment=?, notes=?, updated=?,
                on_screen_text=?, visual_plan=?, search_terms=?, broll_plan=?,
                thumbnail_prompt=?, tags=?, sources=?, subtitle_text=?,
                narration_duration=?, research_complete=?, script_complete=?,
                voice_complete=?, subtitles_complete=?, broll_complete=?,
                graphics_complete=?, capcut_complete=?, export_complete=?,
                upload_complete=?
            WHERE id=?
        """, (
            title, category, status, folder, script, description,
            pinned_comment, notes, updated, on_screen_text, visual_plan,
            search_terms, broll_plan, thumbnail_prompt, tags, sources,
            subtitle_text, float(narration_duration or 0),
            int(bool(pipeline.get("research_complete", 0))),
            int(bool(pipeline.get("script_complete", 0))),
            int(bool(pipeline.get("voice_complete", 0))),
            int(bool(pipeline.get("subtitles_complete", 0))),
            int(bool(pipeline.get("broll_complete", 0))),
            int(bool(pipeline.get("graphics_complete", 0))),
            int(bool(pipeline.get("capcut_complete", 0))),
            int(bool(pipeline.get("export_complete", 0))),
            int(bool(pipeline.get("upload_complete", 0))),
            project_id
        ))
        self.conn.commit()


    # -------------------------------------------------
    # Close
    # -------------------------------------------------

    def close(self):

        self.conn.close()

    def get_latest_project(self):

        cur = self.conn.cursor()

        cur.execute("""

            SELECT *
            FROM projects
            ORDER BY id DESC
            LIMIT 1

        """)

        row = cur.fetchone()

        if row is None:
            return None

        return dict(row)

    def update_project_schedule(self, project_id, scheduled_for):

        self.conn.execute(

            "UPDATE projects SET scheduled_for=? WHERE id=?",

            (
                scheduled_for,
                project_id
            )

        )

        self.conn.commit()

    def update_project_status_and_folder(
        self,
        project_id,
        status,
        folder,
        scheduled_for=""
    ):
        """Update status, folder and scheduling details atomically."""

        updated = datetime.now().strftime("%Y-%m-%d %H:%M")

        self.conn.execute(
            """
            UPDATE projects
            SET status = ?,
                folder = ?,
                updated = ?,
                scheduled_for = ?
            WHERE id = ?
            """,
            (
                status,
                folder,
                updated,
                scheduled_for,
                project_id,
            ),
        )

        self.conn.commit()
    
    def update_project_folder(self, project_id, folder):
        """Update only the folder path for a project."""
        self.conn.execute(
            """
            UPDATE projects
            SET folder = ?
            WHERE id = ?
            """,
            (folder, project_id),
        )
        self.conn.commit()
    
    def get_due_scheduled_project_ids(self):
        """Return scheduled projects whose publish time has arrived."""

        now = datetime.now().strftime("%Y-%m-%d %H:%M")

        rows = self.conn.execute(
            """
            SELECT id
            FROM projects
            WHERE status = 'Scheduled'
              AND scheduled_for != ''
              AND scheduled_for <= ?
            """,
            (now,),
        ).fetchall()

        return [row["id"] for row in rows]