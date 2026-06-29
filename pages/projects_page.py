from pages.base_page import BasePage
import customtkinter as ctk
import os
import shutil
from tkinter import messagebox
from widgets.project_card import ProjectCard

class ProjectsPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Projects")

        self.app = app
        self.current_status = "All"
        self.build()

    def build(self):

        top = ctk.CTkFrame(self.content)
        top.pack(fill="x", pady=(0, 15))

        self.search = ctk.CTkEntry(
            top,
            placeholder_text="Search projects..."
        )

        self.search.pack(side="left", fill="x", expand=True, padx=(0,10))
        self.search.bind("<KeyRelease>", lambda e: self.load_projects())

        ctk.CTkButton(
            top,
            text="Refresh",
            command=self.load_projects
        ).pack(side="left")

        # Status Filters
        filters = ctk.CTkFrame(self.content)
        filters.pack(fill="x", pady=(0, 10))

        for status in ["All", "In Progress", "Scheduled", "Completed"]:

            ctk.CTkButton(
                filters,
                text=status,
                width=120,
                command=lambda s=status: self.set_status_filter(s)
            ).pack(side="left", padx=5)

        self.project_list = ctk.CTkScrollableFrame(self.content)
        self.project_list.pack(fill="both", expand=True)

        self.load_projects()

    def set_status_filter(self, status):

        self.current_status = status

        self.load_projects()

    def load_projects(self):

        for widget in self.project_list.winfo_children():
            widget.destroy()

        search = self.search.get().lower()

        projects = self.pm.get_all_projects()

        # Filter by status
        if self.current_status != "All":
            projects = [
                p for p in projects
                if p["status"] == self.current_status
            ]

        for project in projects:

            title = project["title"]

            if search not in title.lower():
                continue

            ProjectCard(
                self.project_list,
                project,
                self.app
            ).pack(
                fill="x",
                padx=10,
                pady=8
            )