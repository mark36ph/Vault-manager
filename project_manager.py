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

    def create_project(self, title, category):

        settings = self.load_settings()

        root = settings.get("projects_folder", "").strip()

        if not root:
            raise Exception(
                "Please select your Projects Folder in Settings."
            )

        project_info = {
            "title": title,
            "status": "In Progress"
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

            "status": "In Progress",

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
            "In Progress",
            str(project_folder),
            created
        )

        return project_folder

    # =====================================================
    # Database Functions
    # =====================================================

    def get_all_projects(self):

        return self.db.get_projects()

    def project_count(self):

        return self.db.count_projects()

    def delete_project(self, project_id):

        self.db.delete_project(project_id)

    def close(self):

        self.db.close()

    def count_projects_by_status(self, status):

        return self.db.count_projects_by_status(status)
        
    def get_project_folder(self, project):

        settings = self.load_settings()

        root = Path(settings["projects_folder"])

        return root / project["status"] / project["title"]