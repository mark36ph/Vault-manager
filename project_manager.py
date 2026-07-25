import shutil
from pathlib import Path
from datetime import datetime
from database import Database
from common.settings_manager import SettingsManager

DATA_FOLDER = Path("data")

class ProjectManager:

    def __init__(self, db=None, settings=None):
        DATA_FOLDER.mkdir(exist_ok=True)

        self.db = db or Database()
        self.settings = settings or SettingsManager()

    # =====================================================
    # Create Project
    # =====================================================

    def create_project(
        self,
        title,
        category,
        status,
        script="",
        description="",
        pinned_comment="",
        notes=""
    ):

        settings = self.settings.section("general")
        root = settings.get("projects_folder", "").strip()

        if not root:
            raise Exception(
                "Please select your Projects Folder in Settings."
            )

        # Build the project folder path using the same app structure:
        # Projects Folder / Status / Project Title
        project_info = {
            "title": title,
            "status": status
        }

        project_folder = self.get_project_folder(project_info)

        # Only media/import folders live on disk. All text and project
        # metadata are stored in SQLite as the single source of truth.
        folders = [
            project_folder,
            project_folder / "Assets" / "Images",
            project_folder / "Assets" / "Videos",
            project_folder / "Assets" / "Music",
            project_folder / "Assets" / "SFX",
            project_folder / "Assets" / "Overlays",
            project_folder / "Assets" / "Thumbnails",
            project_folder / "CapCut",
            project_folder / "Export",
            project_folder / "Voice"
        ]

        for folder in folders:
            folder.mkdir(parents=True, exist_ok=True)

        created = datetime.now().strftime("%Y-%m-%d %H:%M")

        # Save project to database
        relative_folder = self.get_relative_project_folder(project_folder)

        self.db.add_project(
            title,
            category,
            status,
            str(relative_folder),
            created,
            script,
            description,
            pinned_comment,
            notes
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
        project = self.db.get_project(project_id)

        if project is None:
            return False

        project_folder = self.resolve_project_folder(project)

        if project_folder.exists():
            shutil.rmtree(project_folder)

        try:
            self.db.delete_project(project_id)
        except Exception:
            # Best-effort rollback is not possible after permanent folder deletion,
            # so surface the error clearly.
            raise

        return True

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
        
    def get_projects_root(self):
        """Return the configured Projects folder as an absolute path."""
        settings = self.settings.section("general")
        root = settings.get("projects_folder", "").strip()

        if not root:
            raise Exception(
                "Please select your Projects Folder in Settings."
            )

        return Path(root).resolve()


    def resolve_project_folder(self, project):
        """
        Convert the project's stored folder value into a full path.

        New records store relative paths such as:
            Published\\Project Title

        Old records may still contain absolute paths.
        """
        stored_folder = project["folder"] if "folder" in project else ""

        if stored_folder:
            stored_path = Path(stored_folder)

            if stored_path.is_absolute():
                return stored_path

            return self.get_projects_root() / stored_path

        return self.get_project_folder(project)
    
    def get_project_folder(self, project):
        return (
            self.get_projects_root()
            / project["status"]
            / project["title"]
        )
    
    def get_relative_project_folder(self, project_folder):
        """Convert a full project folder path into a database-safe relative path."""
        project_folder = Path(project_folder).resolve()
        projects_root = self.get_projects_root()

        try:
            return project_folder.relative_to(projects_root)
        except ValueError as error:
            raise Exception(
                f"Project folder must be inside the configured Projects folder:\n"
                f"{projects_root}"
            ) from error

    def get_voice_folder(self, project):

        folder = self.resolve_project_folder(project)

        voice_folder = folder / "Voice"

        voice_folder.mkdir(
            parents=True,
            exist_ok=True
        )

        return voice_folder

    def change_project_status(
        self,
        project_id,
        new_status,
        scheduled_for=""
    ):
        """
        Move a project into its new status folder and update the database.

        Returns the refreshed project row.
        """

        project = self.db.get_project(project_id)

        if project is None:
            raise ValueError(f"Project {project_id} was not found.")

        old_folder = self.resolve_project_folder(project)

        destination_project = {
            "title": project["title"],
            "status": new_status,
        }

        new_folder = self.get_project_folder(destination_project)

        old_resolved = old_folder.resolve()
        new_resolved = new_folder.resolve()

        folder_moved = False

        try:
            if old_resolved != new_resolved:

                if not old_folder.exists():
                    raise FileNotFoundError(
                        f"The current project folder does not exist:\n{old_folder}"
                    )

                if new_folder.exists():
                    raise FileExistsError(
                        f"The destination folder already exists:\n{new_folder}"
                    )

                new_folder.parent.mkdir(
                    parents=True,
                    exist_ok=True
                )

                shutil.move(
                    str(old_folder),
                    str(new_folder)
                )

                folder_moved = True

            relative_folder = str(
                self.get_relative_project_folder(new_folder)
            )

            if new_status != "Scheduled":
                scheduled_for = ""

            self.db.update_project_status_and_folder(
                project_id=project_id,
                status=new_status,
                folder=relative_folder,
                scheduled_for=scheduled_for,
            )

        except Exception:

            # Restore the folder if the move succeeded but the database update failed.
            if (
                folder_moved
                and new_folder.exists()
                and not old_folder.exists()
            ):
                old_folder.parent.mkdir(
                    parents=True,
                    exist_ok=True
                )

                shutil.move(
                    str(new_folder),
                    str(old_folder)
                )

            raise

        return self.db.get_project(project_id)
        
    def complete_due_scheduled_projects(self):
        """Publish all due scheduled projects using the normal move logic."""

        project_ids = self.db.get_due_scheduled_project_ids()

        completed_count = 0

        for project_id in project_ids:
            try:
                self.change_project_status(
                    project_id=project_id,
                    new_status="Published",
                    scheduled_for="",
                )

                completed_count += 1

            except Exception as error:
                print(
                    f"Could not publish scheduled project "
                    f"{project_id}: {error}"
                )

        return completed_count

    def check_project_integrity(self):
        """
        Check database project rows against the project folders on disk.

        Returns a list of issue dictionaries.
        """

        issues = []
        projects_root = self.get_projects_root().resolve()
        projects = self.db.get_projects()

        referenced_folders = set()

        for project in projects:
            project_id = project["id"]
            title = project["title"] or ""
            status = project["status"] or ""
            folder_value = project["folder"] or ""

            if not folder_value:
                issues.append({
                    "type": "missing_folder_value",
                    "project_id": project_id,
                    "title": title,
                    "message": "No folder path is stored in the database.",
                })
                continue

            stored_path = Path(folder_value)

            if stored_path.is_absolute():
                issues.append({
                    "type": "absolute_path",
                    "project_id": project_id,
                    "title": title,
                    "folder": folder_value,
                    "message": "The database still contains an absolute path.",
                })

            resolved_folder = self.resolve_project_folder(project).resolve()
            referenced_folders.add(resolved_folder)

            if not resolved_folder.exists():
                issues.append({
                    "type": "missing_folder",
                    "project_id": project_id,
                    "title": title,
                    "folder": str(resolved_folder),
                    "message": "The project folder does not exist.",
                })

            try:
                relative_folder = resolved_folder.relative_to(projects_root)
                folder_status = relative_folder.parts[0]
            except (ValueError, IndexError):
                issues.append({
                    "type": "outside_projects_root",
                    "project_id": project_id,
                    "title": title,
                    "folder": str(resolved_folder),
                    "message": "The project folder is outside the Projects root.",
                })
                continue

            if folder_status != status:
                issues.append({
                    "type": "status_folder_mismatch",
                    "project_id": project_id,
                    "title": title,
                    "status": status,
                    "folder_status": folder_status,
                    "folder": str(resolved_folder),
                    "message": (
                        f"Database status is '{status}' but the folder "
                        f"is under '{folder_status}'."
                    ),
                })

        if projects_root.exists():
            for status_folder in projects_root.iterdir():
                if not status_folder.is_dir():
                    continue

                for project_folder in status_folder.iterdir():
                    if not project_folder.is_dir():
                        continue

                    resolved = project_folder.resolve()

                    if resolved not in referenced_folders:
                        issues.append({
                            "type": "orphan_folder",
                            "folder": str(resolved),
                            "message": (
                                "This folder is not linked to any database project."
                            ),
                        })

        return issues