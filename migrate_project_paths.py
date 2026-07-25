import sqlite3
from pathlib import Path

DB_PATH = Path("data") / "factvault.db"

OLD_ROOT = Path(r"C:\Users\mark3\Documents\1 Minute Fact Vault")
NEW_ROOT = Path(r"C:\Users\mark3\Documents\FactVaultManager\Projects")

if not DB_PATH.exists():
    raise FileNotFoundError(f"Database not found: {DB_PATH.resolve()}")

connection = sqlite3.connect(DB_PATH)
connection.row_factory = sqlite3.Row

try:
    projects = connection.execute(
        "SELECT id, title, status, folder FROM projects"
    ).fetchall()

    updated = 0
    skipped = 0

    for project in projects:
        stored_value = (project["folder"] or "").strip()
        stored_path = Path(stored_value) if stored_value else None
        relative_path = None

        if stored_path:
            if not stored_path.is_absolute():
                print(
                    f"Already relative: {project['title']} -> "
                    f"{stored_path}"
                )
                skipped += 1
                continue

            try:
                relative_path = stored_path.relative_to(OLD_ROOT)
            except ValueError:
                try:
                    relative_path = stored_path.relative_to(NEW_ROOT)
                except ValueError:
                    relative_path = None

        # Safe fallback using the project's status and title.
        if relative_path is None:
            candidate = Path(project["status"]) / project["title"]

            if (NEW_ROOT / candidate).exists():
                relative_path = candidate
            else:
                print(
                    f"Could not locate: {project['title']}\n"
                    f"  Database path: {stored_value}\n"
                    f"  Expected path: {NEW_ROOT / candidate}"
                )
                skipped += 1
                continue

        full_new_path = NEW_ROOT / relative_path

        if not full_new_path.exists():
            print(
                f"Folder missing, not updated: {project['title']}\n"
                f"  Expected: {full_new_path}"
            )
            skipped += 1
            continue

        connection.execute(
            "UPDATE projects SET folder = ? WHERE id = ?",
            (str(relative_path), project["id"]),
        )

        print(f"Updated: {project['title']} -> {relative_path}")
        updated += 1

    connection.commit()

    print()
    print(f"Finished. Updated: {updated}, skipped: {skipped}")

finally:
    connection.close()