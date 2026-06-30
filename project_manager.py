import json
import shutil
from pathlib import Path
from datetime import datetime
from database import Database
from common.json_utils import load_json, save_json

DATA_FOLDER = Path("data")
SETTINGS_FILE = DATA_FOLDER / "app_settings.json"


class ProjectManager:

    def __init__(self):
        DATA_FOLDER.mkdir(exist_ok=True)

        self.db = Database()

        if not SETTINGS_FILE.exists():

            save_json(
                SETTINGS_FILE,
                {
                    "projects_folder": "",
                    "theme": "dark"
                }
            )

    # =====================================================
    # Settings
    # =====================================================

    def load_settings(self):

        try:

            with open(
                SETTINGS_FILE,
                "r",
                encoding="utf-8-sig"
            ) as f:

                return json.load(f)

        except FileNotFoundError:

            settings = {
                "projects_folder": ""
            }

            self.save_settings(settings)

            return settings

        except json.JSONDecodeError as e:

            raise Exception(
                f"Settings file is invalid:\n{e}"
            )

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

            project_folder / "Export",

            project_folder / "Voice"

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

        save_json(
            project_folder / "project.json",
            project_info
        )

        self.db.add_project(
            title,
            category,
            status,
            str(project_folder),
            created
        )

        return project_folder

    def apply_template(self, project_folder, template):

        template_folder = Path("templates") / template

        if not template_folder.exists():
            return

        for item in template_folder.iterdir():

            destination = Path(project_folder) / item.name

            if item.is_dir():

                shutil.copytree(
                    item,
                    destination,
                    dirs_exist_ok=True
                )

            else:

                shutil.copy2(
                    item,
                    destination
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

        templates.sort(key=str.lower)

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

    def get_voice_folder(self, project):

        if "folder" in project and project["folder"]:

            folder = Path(project["folder"])

        else:

            folder = self.get_project_folder(project)

        voice_folder = folder / "Voice"

        voice_folder.mkdir(
            parents=True,
            exist_ok=True
        )

        return voice_folder