import customtkinter as ctk
import os
from pathlib import Path
from tkinter import messagebox
from pages.dashboard_page import DashboardPage
from pages.new_fact_page import NewFactPage
from pages.projects_page import ProjectsPage
from pages.settings_page import SettingsPage
from pages.statistics_page import StatisticsPage
from pages.edit_project_page import EditProjectPage
from project_manager import ProjectManager
from pages.templates_page import TemplatesPage
from pages.edit_template_page import EditTemplatePage
from common.settings_manager import SettingsManager
from common.app_info import AppInfo
from widgets.update_dialog import UpdateDialog
from common.update_manager import UpdateManager
from pages.fact_notes_page import FactNotesPage
import ctypes
from PIL import Image, ImageTk
from pages.project_viewer_page import ProjectViewerPage

class Dashboard(ctk.CTk):

    def __init__(self):
        super().__init__()
        self.app_info = AppInfo()
        self.settings = SettingsManager()        
        self.ensure_app_icon()

        icon_path = Path("assets") / "icons" / "app.ico"
        # Set Windows taskbar/app icon
        try:

            app_id = "mark.factvaultmanager.app"
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
                app_id
            )

        except Exception as e:

            print(
                f"Could not set AppUserModelID: {e}"
            )

        png_icon_path = Path("assets") / "icons" / "app.png"

        try:

            if png_icon_path.exists():

                icon_image = Image.open(
                    png_icon_path
                )

                self.taskbar_icon = ImageTk.PhotoImage(
                    icon_image
                )

                self.iconphoto(
                    True,
                    self.taskbar_icon
                )

        except Exception as e:

            print(
                f"Could not set app icon: {e}"
            )
            
        if icon_path.exists():

            self.iconbitmap(
                icon_path
            )

        ctk.set_appearance_mode(
            self.settings.get(
                "general",
                "theme",
                "dark"
            ).title()
        )
        self.minsize(1200, 700)
        self.pm = ProjectManager()
        self.pm.complete_due_scheduled_projects()
        self.check_scheduled_projects_loop()
        
        if self.settings.get(
            "general",
            "start_maximized",
            True
        ):
            self.after(
                100,
                lambda: self.state("zoomed")
            )

        self.title(
            self.app_info.get("name")
        )

        self.geometry("1400x800")

        # ==========================
        # Sidebar
        # ==========================

        self.sidebar = ctk.CTkFrame(
            self,
            width=220,
            corner_radius=0
        )

        self.sidebar.pack(
            side="left",
            fill="y"
        )

        self.app_title = ctk.CTkLabel(
            self.sidebar,
            text=self.app_info.get(
                "name"
            ),
            font=("Segoe UI", 28, "bold")
        )

        self.app_title.pack(
            pady=30
        )

        self.add_sidebar_button(
            "🏠 Dashboard",
            self.show_dashboard
        )

        self.add_sidebar_button(
            "➕ New Fact",
            self.show_new_fact
        )

        self.add_sidebar_button(
            "📂 Projects",
            self.show_projects
        )

        self.add_sidebar_button(
            "📝 Fact Notes",
            self.show_fact_notes
        )

        self.add_sidebar_button(
            "🗂 Templates",
            self.show_templates
        )

        self.add_sidebar_button(
            "📊 Statistics",
            self.show_statistics
        )

        self.add_sidebar_button(
            "⚙ Settings",
            self.show_settings
        )

        # ==========================
        # Main Content
        # ==========================

        self.content = ctk.CTkFrame(
            self,
            corner_radius=0
        )

        self.content.pack(
            side="left",
            fill="both",
            expand=True
        )

        self.current_page = None

        self.show_dashboard()
        self.after(
            1500,
            self.check_updates_on_startup
        )
    # ==================================

    def add_sidebar_button(self, text, command):

        ctk.CTkButton(
            self.sidebar,
            text=text,
            height=45,
            command=command
        ).pack(
            fill="x",
            padx=20,
            pady=6
        )

    # ==================================


    def show_edit_project(self, project_id):

        self.clear_page()

        self.current_page = EditProjectPage(
            self.content,
            self.pm,
            self,
            project_id
        )

        self.current_page.pack(
            fill="both",
            expand=True
        )

    def show_project_viewer(self, project_id):

        self.clear_page()

        self.current_page = ProjectViewerPage(
            self.content,
            self.pm,
            self,
            project_id
        )

        self.current_page.pack(
            fill="both",
            expand=True
        )
    # ==================================

    def clear_page(self):

        if self.current_page:

            self.current_page.destroy()

            self.current_page = None

    def check_scheduled_projects_loop(self):

        try:

            changed_count = self.pm.complete_due_scheduled_projects()

            if changed_count > 0:

                if hasattr(self.current_page, "load_projects"):

                    self.current_page.load_projects()

        except Exception as e:

            print(
                f"Scheduled project check failed: {e}"
            )

        self.after(
            60000,
            self.check_scheduled_projects_loop
        )
        
    # ==================================

    def load_page(self, page_class, *args):

        self.clear_page()

        self.current_page = page_class(
            self.content,
            self.pm,
            *args
        )

        self.current_page.pack(
            fill="both",
            expand=True
        )

    # ==================================

    def show_dashboard(self):

        self.load_page(DashboardPage, self)

    def show_new_fact(self):

        self.load_page(NewFactPage, self)

    def show_projects(self):

        self.load_page(ProjectsPage, self)

    def show_fact_notes(self):

        self.load_page(FactNotesPage, self)
        
    def show_statistics(self):

        self.load_page(StatisticsPage, self)

    def show_settings(self):

        self.load_page(SettingsPage, self)
    
    def show_edit_template(self, template_name):

        self.load_page(
            EditTemplatePage,
            self,
            template_name
        )

    def show_templates(self):

        self.load_page(
            TemplatesPage,
            self
        )

    def open_project_folder(self, project):

        try:

            folder = self.pm.get_project_folder(project)

            os.startfile(folder)

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )

    def open_project_viewer(self, project):

        # ======================================
        # Load latest project data
        # ======================================

        project_id = project["id"]

        project = self.pm.db.get_project(
            project_id
        )

        if project is None:

            messagebox.showerror(
                "Project",
                "Could not load this project."
            )

            return

        folder = Path(
            project["folder"]
        )


        script = project["script"] or ""
        description = project["description"] or ""
        pinned_comment = project["pinned_comment"] or ""
        notes = project["notes"] or ""
        on_screen_text = project["on_screen_text"] or ""
        visual_plan = project["visual_plan"] or ""

        # ======================================
        # Window
        # ======================================

        window = ctk.CTkToplevel(self)
        window.title(project["title"])
        window.geometry("1000x750")
        window.transient(self)
        window.grab_set()

        window.lift()
        window.focus_force()

        # ======================================
        # Header
        # ======================================

        header = ctk.CTkFrame(
            window,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10)
        )

        ctk.CTkLabel(
            header,
            text=project["title"],
            font=("Segoe UI", 28, "bold")
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            header,
            text="✏ Edit",
            width=100,
            command=lambda: self.open_viewer_edit_project(
                window,
                project["id"]
            )
        ).pack(
            side="right",
            padx=(8, 0)
        )

        ctk.CTkButton(
            header,
            text="Close",
            width=100,
            command=window.destroy
        ).pack(
            side="right"
        )

        # ======================================
        # Tabs
        # ======================================

        tabs = ctk.CTkTabview(
            window
        )

        tabs.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 20)
        )

        tab_data = [
            (
                "Script",
                script
            ),
            (
                "On-Screen Text",
                on_screen_text
            ),
            (
                "Visual Plan",
                visual_plan
            ),
            (
                "Description",
                description
            ),
            (
                "Pinned Comment",
                pinned_comment
            ),
            (
                "Notes",
                notes
            )
        ]

        for tab_name, tab_text in tab_data:

            tabs.add(
                tab_name
            )

            self.add_project_viewer_tab(
                tabs.tab(tab_name),
                tab_name,
                tab_text
            )

    def ensure_app_icon(self):

        png_icon_path = Path("assets") / "icons" / "app.png"
        ico_icon_path = Path("assets") / "icons" / "app.ico"

        if not png_icon_path.exists():

            print(
                f"Could not find icon PNG: {png_icon_path}"
            )

            return

        try:

            from PIL import ImageFilter, ImageOps

            image = Image.open(
                png_icon_path
            ).convert(
                "RGBA"
            )

            # Crop/fit the image into a perfect square.
            # This helps Windows stop squeezing it weirdly.
            image = ImageOps.fit(
                image,
                (
                    1024,
                    1024
                ),
                method=Image.Resampling.LANCZOS,
                centering=(
                    0.5,
                    0.45
                )
            )

            icon_sizes = [
                16,
                24,
                32,
                48,
                64,
                128,
                256
            ]

            resized_icons = []

            for size in icon_sizes:

                resized = image.resize(
                    (
                        size,
                        size
                    ),
                    Image.Resampling.LANCZOS
                )

                resized = resized.filter(
                    ImageFilter.UnsharpMask(
                        radius=1,
                        percent=180,
                        threshold=2
                    )
                )

                resized_icons.append(
                    resized
                )

            resized_icons[0].save(
                ico_icon_path,
                format="ICO",
                sizes=[
                    (
                        size,
                        size
                    )
                    for size in icon_sizes
                ],
                append_images=resized_icons[1:]
            )

            print(
                f"Created sharp icon: {ico_icon_path}"
            )

        except Exception as e:

            print(
                f"Could not create ICO icon: {e}"
            )
            
    def add_project_viewer_tab(self, parent, label, text):

        top = ctk.CTkFrame(
            parent,
            fg_color="transparent"
        )

        top.pack(
            fill="x",
            padx=10,
            pady=(10, 5)
        )

        ctk.CTkLabel(
            top,
            text=label,
            font=("Segoe UI", 22, "bold")
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            top,
            text="📋 Copy",
            width=100,
            command=lambda: self.copy_project_viewer_text(
                text
            )
        ).pack(
            side="right"
        )

        box = ctk.CTkTextbox(
            parent,
            font=("Segoe UI Emoji", 15),
            wrap="word"
        )

        box.pack(
            fill="both",
            expand=True,
            padx=10,
            pady=(0, 10)
        )

        if text.strip():

            box.insert(
                "1.0",
                text
            )

        else:

            box.insert(
                "1.0",
                "Nothing added yet."
            )

        box.configure(
            state="disabled"
        )

    def copy_project_viewer_text(self, text):

        self.clipboard_clear()

        self.clipboard_append(
            text
        )

        self.update()

        messagebox.showinfo(
            "Copied",
            "Copied to clipboard."
        )

    def open_viewer_edit_project(self, window, project_id):

        window.destroy()

        self.show_edit_project(
            project_id
        )
 
    def delete_project(self, project):

        import shutil

        answer = messagebox.askyesnocancel(
            "Delete Project",
            "Delete the project folder as well?\n\n"
            "Yes = Delete project and folder\n"
            "No = Delete project only\n"
            "Cancel = Do nothing"
        )

        if answer is None:
            return

        if answer:

            try:

                folder = self.pm.get_project_folder(project)

                if folder.exists():
                    shutil.rmtree(folder)

            except Exception as e:

                messagebox.showerror(
                    "Error",
                    str(e)
                )

                return

        self.pm.delete_project(project["id"])

        self.show_projects()
        
    def refresh_app_info(self):

        self.app_info = AppInfo()

        name = self.app_info.get(
            "name",
            "Fact Vault Manager"
        )

        self.title(name)

        self.app_title.configure(
            text=name
        )

    def check_updates_on_startup(self):

        check_enabled = self.settings.get(
            "general",
            "check_updates",
            True
        )

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
                )
            )

        except Exception as e:

            print(
                f"Startup update check failed: {e}"
            )