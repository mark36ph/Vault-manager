import customtkinter as ctk
import os
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
from pages.voice_studio_page import VoiceStudioPage
from common.settings_manager import SettingsManager

class Dashboard(ctk.CTk):

    def __init__(self):
        super().__init__()
        self.settings = SettingsManager()
        ctk.set_appearance_mode(
            self.settings.get(
                "general",
                "theme",
                "dark"
            ).title()
        )
        self.minsize(1200, 700)
        self.pm = ProjectManager()

        if self.settings.get(
            "general",
            "start_maximized",
            True
        ):
            self.after(
                100,
                lambda: self.state("zoomed")
            )

        self.title("Fact Vault Manager")
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

        ctk.CTkLabel(
            self.sidebar,
            text="FACT VAULT\nMANAGER",
            font=("Segoe UI", 28, "bold")
        ).pack(pady=30)

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
            "🗂 Templates",
            self.show_templates
        )

        self.add_sidebar_button(
            "📊 Statistics",
            self.show_statistics
        )

        self.add_sidebar_button(
            "🎤 Voice Studio",
            self.show_voice_studio
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

    # ==================================

    def clear_page(self):

        if self.current_page:

            self.current_page.destroy()

            self.current_page = None

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

    def show_statistics(self):

        self.load_page(StatisticsPage, self)

    def show_settings(self):

        self.load_page(SettingsPage, self)

    def show_voice_studio(self):

        self.load_page(
            VoiceStudioPage,
            self
        )
    
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