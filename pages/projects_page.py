from pages.base_page import BasePage
import customtkinter as ctk
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

        for status in ["All", "In Progress", "Scheduled", "Completed", "Published"]:

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

        search_text = self.search.get().lower().strip()

        projects = self.pm.get_all_projects()

        filtered_projects = []

        for project in projects:

            title = project["title"].lower()
            category = project["category"].lower()
            status = project["status"]

            matches_search = (
                search_text in title
                or search_text in category
            )

            matches_status = (
                self.current_status == "All"
                or status == self.current_status
            )

            if matches_search and matches_status:

                filtered_projects.append(
                    project
                )

        if not filtered_projects:

            ctk.CTkLabel(
                self.project_list,
                text="No projects found.",
                text_color="gray"
            ).grid(
                row=0,
                column=0,
                padx=20,
                pady=20,
                sticky="w"
            )

            return

        for index, project in enumerate(filtered_projects):

            row = index // 2
            column = index % 2

            card = ProjectCard(
                self.project_list,
                project,
                self.app,
                self.load_projects
            )

            card.grid(
                row=row,
                column=column,
                sticky="nsew",
                padx=8,
                pady=8
            )

        self.project_list.grid_columnconfigure(
            0,
            weight=1
        )

        self.project_list.grid_columnconfigure(
            1,
            weight=1
        )