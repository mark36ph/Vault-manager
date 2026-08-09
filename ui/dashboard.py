import ctypes
import os
from pathlib import Path
from tkinter import messagebox

import customtkinter as ctk
from PIL import Image, ImageTk

from common.app_info import AppInfo
from common.settings_manager import SettingsManager
from common.update_manager import UpdateManager
from pages.dashboard_page import DashboardPage
from pages.edit_project_page import EditProjectPage
from pages.edit_template_page import EditTemplatePage
from pages.fact_notes_page import FactNotesPage
from pages.new_fact_page import NewFactPage
from pages.project_viewer_page import ProjectViewerPage
from pages.projects_page import ProjectsPage
from pages.settings_page import SettingsPage
from pages.statistics_page import StatisticsPage
from pages.templates_page import TemplatesPage
from project_manager import ProjectManager
from widgets.update_dialog import UpdateDialog


class Dashboard(ctk.CTk):
    """Main Fact Vault Manager application shell."""

    SIDEBAR_WIDTH = 178
    NAV_HEIGHT = 38

    def __init__(self):
        super().__init__()
        self.app_info = AppInfo()
        self.settings = SettingsManager()
        self.sidebar_buttons = {}
        self.active_sidebar_text = None
        self.current_page = None
        self.sidebar_logo = None

        self.ensure_app_icon()
        self._configure_window_icon()

        ctk.set_appearance_mode(
            self.settings.get("general", "theme", "dark").title()
        )

        self.minsize(1080, 660)
        self.geometry("1400x800")
        self.title(self.app_info.get("name"))

        self.pm = ProjectManager()
        self.pm.complete_due_scheduled_projects()
        self.check_scheduled_projects_loop()

        if self.settings.get("general", "start_maximized", True):
            self.after(100, lambda: self.state("zoomed"))

        self._build_sidebar()
        self._build_content_area()

        self.show_dashboard()
        self.after(1500, self.check_updates_on_startup)

    def _configure_window_icon(self):
        icon_path = Path("assets") / "icons" / "app.ico"
        png_icon_path = Path("assets") / "icons" / "app.png"

        try:
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
                "mark.factvaultmanager.app"
            )
        except Exception as exc:
            print(f"Could not set AppUserModelID: {exc}")

        try:
            if png_icon_path.exists():
                icon_image = Image.open(png_icon_path)
                self.taskbar_icon = ImageTk.PhotoImage(icon_image)
                self.iconphoto(True, self.taskbar_icon)
        except Exception as exc:
            print(f"Could not set app icon: {exc}")

        if icon_path.exists():
            try:
                self.iconbitmap(icon_path)
            except Exception as exc:
                print(f"Could not set window icon: {exc}")

    def _brand_text(self):
        name = self.app_info.get("name", "Fact Vault Manager")
        if name.lower().endswith(" manager"):
            return name[:-8].strip(), "MANAGER"
        return name, "CONTENT WORKSPACE"

    def _build_sidebar(self):
        self.sidebar = ctk.CTkFrame(
            self,
            width=self.SIDEBAR_WIDTH,
            corner_radius=0,
            fg_color=("#F8F9FB", "#15171C"),
            border_width=0,
        )
        self.sidebar.pack(side="left", fill="y")
        self.sidebar.pack_propagate(False)

        brand = ctk.CTkFrame(self.sidebar, fg_color="transparent")
        brand.pack(fill="x", padx=14, pady=(16, 18))

        logo_path = Path("assets") / "icons" / "app.png"
        if logo_path.exists():
            try:
                logo_image = Image.open(logo_path)
                self.sidebar_logo = ctk.CTkImage(
                    light_image=logo_image,
                    dark_image=logo_image,
                    size=(28, 28),
                )
                ctk.CTkLabel(
                    brand,
                    text="",
                    image=self.sidebar_logo,
                    width=30,
                    height=30,
                ).pack(side="left", padx=(0, 9))
            except Exception as exc:
                print(f"Could not load sidebar logo: {exc}")

        brand_text = ctk.CTkFrame(brand, fg_color="transparent")
        brand_text.pack(side="left", fill="x", expand=True)

        primary, secondary = self._brand_text()
        self.app_title = ctk.CTkLabel(
            brand_text,
            text=primary,
            font=("Segoe UI", 15, "bold"),
            anchor="w",
        )
        self.app_title.pack(fill="x")

        self.app_subtitle = ctk.CTkLabel(
            brand_text,
            text=secondary,
            font=("Segoe UI", 9, "bold"),
            text_color=("#98A2B3", "#737A86"),
            anchor="w",
        )
        self.app_subtitle.pack(fill="x", pady=(1, 0))

        ctk.CTkFrame(
            self.sidebar,
            height=1,
            corner_radius=0,
            fg_color=("#E7EAF0", "#252930"),
        ).pack(fill="x", padx=12, pady=(0, 12))

        ctk.CTkLabel(
            self.sidebar,
            text="WORKSPACE",
            font=("Segoe UI", 9, "bold"),
            text_color=("#98A2B3", "#6F7681"),
            anchor="w",
        ).pack(fill="x", padx=15, pady=(0, 5))

        self.add_sidebar_button("🏠 Dashboard", self.show_dashboard)
        self.add_sidebar_button("➕ New Fact", self.show_new_fact)
        self.add_sidebar_button("📂 Projects", self.show_projects)
        self.add_sidebar_button("📝 Fact Notes", self.show_fact_notes)
        self.add_sidebar_button("🗂 Templates", self.show_templates)
        self.add_sidebar_button("📊 Statistics", self.show_statistics)
        self.add_sidebar_button("⚙ Settings", self.show_settings)

        version = self.app_info.get("version", "")
        build = self.app_info.get("build", "")
        version_text = f"v{version}" if version else ""
        if build not in (None, ""):
            version_text += f"  ·  {build}"

        self.sidebar_footer = ctk.CTkLabel(
            self.sidebar,
            text=version_text,
            font=("Segoe UI", 9),
            text_color=("#98A2B3", "#666D78"),
            anchor="w",
        )
        self.sidebar_footer.pack(
            side="bottom",
            fill="x",
            padx=15,
            pady=(8, 13),
        )

    def _build_content_area(self):
        shell = ctk.CTkFrame(
            self,
            corner_radius=0,
            fg_color=("#E6E9EF", "#252930"),
            width=1,
        )
        shell.pack(side="left", fill="y")

        self.content = ctk.CTkFrame(
            self,
            corner_radius=0,
            fg_color=("#F5F6F8", "#101216"),
        )
        self.content.pack(side="left", fill="both", expand=True)

    def add_sidebar_button(self, text, command):
        """Add a compact navigation row and preserve the public sidebar API."""

        def run_command():
            self._set_active_sidebar(text)
            command()

        button = ctk.CTkButton(
            self.sidebar,
            text=text,
            height=self.NAV_HEIGHT,
            corner_radius=6,
            border_width=0,
            fg_color="transparent",
            hover_color=("#EEF1F5", "#20242B"),
            text_color=("#475467", "#C8CDD5"),
            font=("Segoe UI Emoji", 12),
            anchor="w",
            command=run_command,
        )
        button.pack(fill="x", padx=8, pady=1)
        self.sidebar_buttons[text] = button
        return button

    def _set_active_sidebar(self, text):
        self.active_sidebar_text = text
        for label, button in self.sidebar_buttons.items():
            if label == text:
                button.configure(
                    fg_color=("#E9EFF8", "#202B3B"),
                    text_color=("#175CD3", "#B8D0FF"),
                    hover_color=("#E1E9F5", "#263348"),
                    font=("Segoe UI Emoji", 12, "bold"),
                )
            else:
                button.configure(
                    fg_color="transparent",
                    text_color=("#475467", "#C8CDD5"),
                    hover_color=("#EEF1F5", "#20242B"),
                    font=("Segoe UI Emoji", 12),
                )

    def show_edit_project(self, project_id):
        self._set_active_sidebar("📂 Projects")
        self.clear_page()
        self.current_page = EditProjectPage(
            self.content,
            self.pm,
            self,
            project_id,
        )
        self.current_page.pack(fill="both", expand=True)

    def show_project_viewer(self, project_id):
        self._set_active_sidebar("📂 Projects")
        self.clear_page()
        self.current_page = ProjectViewerPage(
            self.content,
            self.pm,
            self,
            project_id,
        )
        self.current_page.pack(fill="both", expand=True)

    def clear_page(self):
        if self.current_page:
            self.current_page.destroy()
            self.current_page = None

    def check_scheduled_projects_loop(self):
        try:
            changed_count = self.pm.complete_due_scheduled_projects()
            if changed_count > 0 and hasattr(self.current_page, "load_projects"):
                self.current_page.load_projects()
        except Exception as exc:
            print(f"Scheduled project check failed: {exc}")

        self.after(60000, self.check_scheduled_projects_loop)

    def load_page(self, page_class, *args):
        self.clear_page()
        self.current_page = page_class(self.content, self.pm, *args)
        self.current_page.pack(fill="both", expand=True)

    def show_dashboard(self):
        self._set_active_sidebar("🏠 Dashboard")
        self.load_page(DashboardPage, self)

    def show_new_fact(self):
        self._set_active_sidebar("➕ New Fact")
        self.load_page(NewFactPage, self)

    def show_projects(self):
        self._set_active_sidebar("📂 Projects")
        self.load_page(ProjectsPage, self)

    def show_fact_notes(self):
        self._set_active_sidebar("📝 Fact Notes")
        self.load_page(FactNotesPage, self)

    def show_statistics(self):
        self._set_active_sidebar("📊 Statistics")
        self.load_page(StatisticsPage, self)

    def show_settings(self):
        self._set_active_sidebar("⚙ Settings")
        self.load_page(SettingsPage, self)

    def show_edit_template(self, template_name):
        self._set_active_sidebar("🗂 Templates")
        self.load_page(EditTemplatePage, self, template_name)

    def show_templates(self):
        self._set_active_sidebar("🗂 Templates")
        self.load_page(TemplatesPage, self)

    def open_project_folder(self, project):
        try:
            folder = self.pm.get_project_folder(project)
            os.startfile(folder)
        except Exception as exc:
            messagebox.showerror("Error", str(exc))

    def open_project_viewer(self, project):
        project_id = project["id"]
        project = self.pm.db.get_project(project_id)

        if project is None:
            messagebox.showerror("Project", "Could not load this project.")
            return

        script = project["script"] or ""
        description = project["description"] or ""
        pinned_comment = project["pinned_comment"] or ""
        notes = project["notes"] or ""
        on_screen_text = project["on_screen_text"] or ""
        visual_plan = project["visual_plan"] or ""

        window = ctk.CTkToplevel(self)
        window.title(project["title"])
        window.geometry("980x720")
        window.transient(self)
        window.grab_set()
        window.lift()
        window.focus_force()

        header = ctk.CTkFrame(window, fg_color="transparent")
        header.pack(fill="x", padx=18, pady=(17, 9))

        ctk.CTkLabel(
            header,
            text=project["title"],
            font=("Segoe UI", 22, "bold"),
        ).pack(side="left")

        ctk.CTkButton(
            header,
            text="✏ Edit",
            width=84,
            height=34,
            corner_radius=6,
            command=lambda: self.open_viewer_edit_project(
                window,
                project["id"],
            ),
        ).pack(side="right", padx=(7, 0))

        ctk.CTkButton(
            header,
            text="Close",
            width=84,
            height=34,
            corner_radius=6,
            fg_color="transparent",
            border_width=1,
            command=window.destroy,
        ).pack(side="right")

        tabs = ctk.CTkTabview(window, corner_radius=8)
        tabs.pack(fill="both", expand=True, padx=18, pady=(0, 18))

        tab_data = [
            ("Script", script),
            ("On-Screen Text", on_screen_text),
            ("Visual Plan", visual_plan),
            ("Description", description),
            ("Pinned Comment", pinned_comment),
            ("Notes", notes),
        ]

        for tab_name, tab_text in tab_data:
            tabs.add(tab_name)
            self.add_project_viewer_tab(
                tabs.tab(tab_name),
                tab_name,
                tab_text,
            )

    def ensure_app_icon(self):
        png_icon_path = Path("assets") / "icons" / "app.png"
        ico_icon_path = Path("assets") / "icons" / "app.ico"

        if not png_icon_path.exists():
            print(f"Could not find icon PNG: {png_icon_path}")
            return

        try:
            from PIL import ImageFilter, ImageOps

            image = Image.open(png_icon_path).convert("RGBA")
            image = ImageOps.fit(
                image,
                (1024, 1024),
                method=Image.Resampling.LANCZOS,
                centering=(0.5, 0.45),
            )

            icon_sizes = [16, 24, 32, 48, 64, 128, 256]
            resized_icons = []

            for size in icon_sizes:
                resized = image.resize(
                    (size, size),
                    Image.Resampling.LANCZOS,
                )
                resized = resized.filter(
                    ImageFilter.UnsharpMask(
                        radius=1,
                        percent=180,
                        threshold=2,
                    )
                )
                resized_icons.append(resized)

            resized_icons[0].save(
                ico_icon_path,
                format="ICO",
                sizes=[(size, size) for size in icon_sizes],
                append_images=resized_icons[1:],
            )
            print(f"Created sharp icon: {ico_icon_path}")
        except Exception as exc:
            print(f"Could not create ICO icon: {exc}")

    def add_project_viewer_tab(self, parent, label, text):
        top = ctk.CTkFrame(parent, fg_color="transparent")
        top.pack(fill="x", padx=8, pady=(8, 5))

        ctk.CTkLabel(
            top,
            text=label,
            font=("Segoe UI", 16, "bold"),
        ).pack(side="left")

        ctk.CTkButton(
            top,
            text="📋 Copy",
            width=82,
            height=32,
            corner_radius=6,
            command=lambda: self.copy_project_viewer_text(text),
        ).pack(side="right")

        box = ctk.CTkTextbox(
            parent,
            font=("Segoe UI Emoji", 13),
            wrap="word",
            corner_radius=6,
        )
        box.pack(fill="both", expand=True, padx=8, pady=(0, 8))
        box.insert("1.0", text if text.strip() else "Nothing added yet.")
        box.configure(state="disabled")

    def copy_project_viewer_text(self, text):
        self.clipboard_clear()
        self.clipboard_append(text)
        self.update()
        messagebox.showinfo("Copied", "Copied to clipboard.")

    def open_viewer_edit_project(self, window, project_id):
        window.destroy()
        self.show_edit_project(project_id)

    def delete_project(self, project):
        answer = messagebox.askyesnocancel(
            "Delete Project",
            "Delete the project folder as well?\n\n"
            "Yes = Delete project and folder\n"
            "No = Remove from Fact Vault but keep the folder\n"
            "Cancel = Do nothing",
            parent=self,
        )

        if answer is None:
            return

        try:
            deleted = self.pm.delete_project(
                project["id"],
                delete_folder=bool(answer),
            )
        except Exception as exc:
            messagebox.showerror("Delete Project", str(exc), parent=self)
            self.show_projects()
            return

        if not deleted:
            messagebox.showwarning(
                "Delete Project",
                "The project no longer exists in the database.",
                parent=self,
            )

        self.show_projects()

    def refresh_app_info(self):
        self.app_info = AppInfo()
        name = self.app_info.get("name", "Fact Vault Manager")
        self.title(name)

        primary, secondary = self._brand_text()
        self.app_title.configure(text=primary)
        if hasattr(self, "app_subtitle"):
            self.app_subtitle.configure(text=secondary)

        if hasattr(self, "sidebar_footer"):
            version = self.app_info.get("version", "")
            build = self.app_info.get("build", "")
            version_text = f"v{version}" if version else ""
            if build not in (None, ""):
                version_text += f"  ·  {build}"
            self.sidebar_footer.configure(text=version_text)

    def check_updates_on_startup(self):
        check_enabled = self.settings.get("general", "check_updates", True)
        if not check_enabled:
            return

        try:
            updater = UpdateManager()
            info = updater.check_for_updates()
            if not info["update_available"]:
                return

            UpdateDialog(
                self,
                self.app_info.get("name", "Fact Vault Manager"),
                info,
                lambda: updater.open_download_page(
                    info.get("download_url", "")
                ),
            )
        except Exception as exc:
            print(f"Startup update check failed: {exc}")
