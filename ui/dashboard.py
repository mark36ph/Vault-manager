import customtkinter as ctk

from pages.dashboard_page import DashboardPage
from pages.new_fact_page import NewFactPage
from pages.projects_page import ProjectsPage
from pages.settings_page import SettingsPage
from pages.statistics_page import StatisticsPage
from pages.edit_project_page import EditProjectPage
from project_manager import ProjectManager


class Dashboard(ctk.CTk):

    def __init__(self):
        super().__init__()

        self.pm = ProjectManager()

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