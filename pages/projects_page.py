from pages.base_page import BasePage
import customtkinter as ctk
import os
import shutil
from tkinter import messagebox


class ProjectsPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Projects")

        self.app = app

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

        self.project_list = ctk.CTkScrollableFrame(self.content)
        self.project_list.pack(fill="both", expand=True)

        self.load_projects()

    def delete_project(self, project):

        answer = messagebox.askyesno(
            "Delete Project",
            f"Are you sure you want to permanently delete:\n\n"
            f"{project['title']}?\n\n"
            "This will delete the project folder and remove it from the database."
        )

        if not answer:
            return

        try:

            folder = project["folder"]

            if os.path.exists(folder):
                shutil.rmtree(folder)

            self.pm.delete_project(project["id"])

            messagebox.showinfo(
                "Deleted",
                "Project deleted successfully."
            )

            self.load_projects()

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )
        

    def load_projects(self):

        for widget in self.project_list.winfo_children():
            widget.destroy()

        search = self.search.get().lower()

        projects = self.pm.get_all_projects()

        for project in projects:

            title = project["title"]

            if search not in title.lower():
                continue

            card = ctk.CTkFrame(self.project_list)
            card.pack(fill="x", padx=10, pady=8)

            ctk.CTkLabel(
                card,
                text=title,
                font=("Segoe UI",20,"bold")
            ).pack(anchor="w", padx=15, pady=(10,0))

            ctk.CTkLabel(
                card,
                text=f"{project['category']} • {project['status']}"
            ).pack(anchor="w", padx=15)

            buttons = ctk.CTkFrame(card)
            buttons.pack(anchor="w", padx=10, pady=10)

            ctk.CTkButton(
                buttons,
                text="📂 Open Folder",
                command=lambda f=project["folder"]: os.startfile(f)
            ).pack(side="left", padx=5)
 
            ctk.CTkButton(
                buttons,
                text="✏ Edit",
                command=lambda p=project: self.app.show_edit_project(p["id"])
            ).pack(side="left", padx=5)

            ctk.CTkButton(
               buttons,
               text="🗑 Delete",
               fg_color="#b71c1c",
               hover_color="#8b0000",
               command=lambda p=project: self.delete_project(p)
           ).pack(side="left", padx=5)