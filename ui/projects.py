import customtkinter as ctk
import os

from project_manager import ProjectManager


class ProjectsWindow(ctk.CTkToplevel):

    def __init__(self, parent):
        super().__init__(parent)

        self.transient(parent)
        self.lift()
        self.focus_force()
        self.grab_set()
        
        self.pm = ProjectManager()

        self.title("Projects")
        self.geometry("900x600")

        ctk.CTkLabel(
            self,
            text="Projects",
            font=("Segoe UI", 28, "bold")
        ).pack(pady=15)

        self.search = ctk.CTkEntry(
            self,
            width=350,
            placeholder_text="Search projects..."
        )
        self.search.pack(pady=10)

        ctk.CTkButton(
            self,
            text="Refresh",
            command=self.load_projects
        ).pack(pady=(0, 10))

        self.scroll = ctk.CTkScrollableFrame(self)
        self.scroll.pack(fill="both", expand=True, padx=15, pady=10)

        self.load_projects()

    def load_projects(self):

        for widget in self.scroll.winfo_children():
            widget.destroy()

        projects = self.pm.get_all_projects()

        if len(projects) == 0:

            ctk.CTkLabel(
                self.scroll,
                text="No projects found.",
                font=("Segoe UI", 18)
            ).pack(pady=30)

            return

        for project in projects:

            card = ctk.CTkFrame(self.scroll)
            card.pack(fill="x", padx=10, pady=10)

            title = project["title"]
            category = project["category"]
            status = project["status"]
            created = project["created"]
            folder = project["folder"]

            ctk.CTkLabel(
                card,
                text=title,
                font=("Segoe UI", 20, "bold")
            ).pack(anchor="w", padx=15, pady=(12, 0))

            ctk.CTkLabel(
                card,
                text=f"Category: {category}"
            ).pack(anchor="w", padx=15)

            ctk.CTkLabel(
                card,
                text=f"Status: {status}"
            ).pack(anchor="w", padx=15)

            ctk.CTkLabel(
                card,
                text=f"Created: {created}"
            ).pack(anchor="w", padx=15, pady=(0, 10))

            buttons = ctk.CTkFrame(card)
            buttons.pack(fill="x", padx=10, pady=10)

            ctk.CTkButton(
                buttons,
                text="📂 Open Folder",
                command=lambda p=folder: os.startfile(p)
            ).pack(side="left", padx=5)