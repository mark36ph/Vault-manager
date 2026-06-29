import json
from pathlib import Path
from datetime import datetime
from database import Database

DATA_FOLDER = Path("data")
SETTINGS_FILE = DATA_FOLDER / "settings.json"


class ProjectManager:

    def __init__(self):
        DATA_FOLDER.mkdir(exist_ok=True)

        self.db = Database()

        if not SETTINGS_FILE.exists():
            SETTINGS_FILE.write_text(
                json.dumps({
                    "projects_folder": "",
                    "theme": "dark"
                }, indent=4),
                encoding="utf-8"
            )

    # =====================================================
    # Settings
    # =====================================================

    def load_settings(self):

        with open(SETTINGS_FILE, "r", encoding="utf-8") as f:
            return json.load(f)

    def save_settings(self, settings):

        with open(SETTINGS_FILE, "w", encoding="utf-8") as f:
            json.dump(settings, f, indent=4)

    # =====================================================
    # Create Project
    # =====================================================

    def create_project(self, title, category, status):

        settings = self.load_settings()

        root = settings.get("projects_folder", "").strip()

        if not root:
            raise Exception(
                "Please select your Projects Folder in Settings."
            )

        project_info = {
            "title": title,
            "status": status
        }

        project_folder = self.get_project_folder(project_info)

        folders = [

            project_folder,

            project_folder / "Assets",

            project_folder / "Assets" / "Images",

            project_folder / "Assets" / "Videos",

            project_folder / "Assets" / "Music",

            project_folder / "Assets" / "SFX",

            project_folder / "CapCut",

            project_folder / "Export"

        ]

        for folder in folders:
            folder.mkdir(parents=True, exist_ok=True)

        files = [

            "Script.txt",

            "Description.txt",

            "Pinned Comment.txt",

            "Notes.txt"

        ]

        for filename in files:

            (project_folder / filename).touch(exist_ok=True)

        created = datetime.now().strftime("%Y-%m-%d %H:%M")

        project_info = {

            "title": title,

            "category": category,

            "status": status,

            "created": created,

            "folder": str(project_folder)

        }

        with open(
            project_folder / "project.json",
            "w",
            encoding="utf-8"
        ) as f:

            json.dump(
                project_info,
                f,
                indent=4
            )

        self.db.add_project(
            title,
            category,
            status,
            str(project_folder),
            created
        )

        return project_folder

    def apply_template(self, folder, template):

        from pathlib import Path

        folder = Path(folder)

        templates = {

            "Standard Fact": {
                "Script.txt": """HOOK

    INTRO

    FACT 1

    FACT 2

    FACT 3

    OUTRO
    """,

                "Description.txt": """Description...

    #facts #shorts
    """,

                "Notes.txt": """Thumbnail ideas

    Research

    Voice-over notes
    """
            },

            "Animal Fact": {
                "Script.txt": """HOOK

    Amazing animal fact...

    FACT 1

    FACT 2

    FACT 3

    OUTRO
    """
            },

            "History Fact": {
                "Script.txt": """HOOK

    Today's historical event...

    BACKGROUND

    KEY EVENTS

    LEGACY

    OUTRO
    """
            },

            "Science Fact": {
                "Script.txt": """HOOK

    Scientific discovery...

    HOW IT WORKS

    WHY IT MATTERS

    OUTRO
    """
            },

            "Space Fact": {
                "Script.txt": """HOOK

    Space discovery...

    FACT 1

    FACT 2

    OUTRO
    """
            },

            "Haunted Fact": {
                "Script.txt": """HOOK

    Haunted location...

    HISTORY

    PARANORMAL REPORTS

    OUTRO
    """
            }

        }

        data = templates.get(template, {})

        for filename, content in data.items():

            path = folder / filename

            path.write_text(
                content,
                encoding="utf-8"
            )

    # =====================================================
    # Database Functions
    # =====================================================

    def get_all_projects(self):

        return self.db.get_projects()

    def project_count(self):

        return self.db.count_projects()

    def delete_project(self, project_id):

        self.db.delete_project(project_id)

    # ==========================================
    # Templates
    # ==========================================

    def get_templates(self):

        templates_folder = Path("templates")

        if not templates_folder.exists():
            return ["Standard Fact"]

        templates = [
            folder.name
            for folder in templates_folder.iterdir()
            if folder.is_dir()
        ]

        templates.sort()

        if not templates:
            templates.append("Standard Fact")

        return templates

    def close(self):

        self.db.close()

    def count_projects_by_status(self, status):

        return self.db.count_projects_by_status(status)
        
    def get_project_folder(self, project):

        settings = self.load_settings()

        root = Path(settings["projects_folder"])

        return root / project["status"] / project["title"]