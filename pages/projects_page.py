import customtkinter as ctk

from pages.base_page import BasePage
from widgets.project_card import ProjectCard


class ProjectsPage(BasePage):
    """Browse, filter, and manage projects."""

    STATUSES = ["All", "In Progress", "Scheduled", "Completed", "Published"]

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Projects")

        self.app = app
        saved_status = getattr(self.app, "_projects_status_filter", "All")
        self.current_status = saved_status if saved_status in self.STATUSES else "All"
        self.saved_search = str(getattr(self.app, "_projects_search_text", "") or "")
        self.filter_buttons = {}

        # Replace the large BasePage heading with a compact page header.
        self.header.pack_forget()
        self.content.pack_forget()
        self.content.pack(
            fill="both",
            expand=True,
            padx=24,
            pady=(20, 20),
        )

        self.build()

    def build(self):
        header = ctk.CTkFrame(self.content, fg_color="transparent")
        header.pack(fill="x", pady=(0, 16))

        title_area = ctk.CTkFrame(header, fg_color="transparent")
        title_area.pack(side="left", fill="x", expand=True)

        ctk.CTkLabel(
            title_area,
            text="Projects",
            font=("Segoe UI", 23, "bold"),
            anchor="w",
        ).pack(fill="x")

        ctk.CTkLabel(
            title_area,
            text="Find, review, and manage your content projects.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#98A2B3"),
            anchor="w",
        ).pack(fill="x", pady=(2, 0))

        ctk.CTkButton(
            header,
            text="New Fact",
            width=88,
            height=34,
            corner_radius=7,
            font=("Segoe UI", 12),
            command=self.app.show_new_fact,
        ).pack(side="right")

        toolbar = ctk.CTkFrame(
            self.content,
            corner_radius=10,
            border_width=1,
            border_color=("#E4E7EC", "#2B303A"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        toolbar.pack(fill="x", pady=(0, 10))

        search_row = ctk.CTkFrame(toolbar, fg_color="transparent")
        search_row.pack(fill="x", padx=12, pady=(11, 8))

        self.search = ctk.CTkEntry(
            search_row,
            placeholder_text="Search by title or category",
            height=34,
            corner_radius=7,
            border_width=1,
            font=("Segoe UI", 12),
        )
        self.search.pack(side="left", fill="x", expand=True, padx=(0, 8))
        if self.saved_search:
            self.search.insert(0, self.saved_search)
        self.search.bind("<KeyRelease>", self._search_changed)

        ctk.CTkButton(
            search_row,
            text="Refresh",
            width=74,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            hover_color=("#F2F4F7", "#252A33"),
            font=("Segoe UI", 11),
            command=self.load_projects,
        ).pack(side="left")

        filters = ctk.CTkFrame(toolbar, fg_color="transparent")
        filters.pack(fill="x", padx=12, pady=(0, 11))

        for status in self.STATUSES:
            button = ctk.CTkButton(
                filters,
                text=status,
                width=94,
                height=29,
                corner_radius=7,
                border_width=0,
                fg_color="transparent",
                text_color=("#475467", "#B7BDC8"),
                hover_color=("#F2F4F7", "#252A33"),
                font=("Segoe UI", 11),
                command=lambda value=status: self.set_status_filter(value),
            )
            button.pack(side="left", padx=(0, 4))
            self.filter_buttons[status] = button

        self.result_label = ctk.CTkLabel(
            filters,
            text="",
            font=("Segoe UI", 10),
            text_color=("#98A2B3", "#7F8794"),
        )
        self.result_label.pack(side="right")

        self.project_list = ctk.CTkScrollableFrame(
            self.content,
            corner_radius=0,
            fg_color="transparent",
        )
        self.project_list.pack(fill="both", expand=True)
        self.project_list.grid_columnconfigure(0, weight=1, uniform="projects")
        self.project_list.grid_columnconfigure(1, weight=1, uniform="projects")

        self._update_filter_styles()
        self.load_projects()

    def _search_changed(self, _event=None):
        self.app._projects_search_text = self.search.get()
        self.load_projects()

    def set_status_filter(self, status):
        if status not in self.STATUSES:
            status = "All"
        self.current_status = status
        self.app._projects_status_filter = status
        self._update_filter_styles()
        self.load_projects()

    def _update_filter_styles(self):
        for status, button in self.filter_buttons.items():
            if status == self.current_status:
                button.configure(
                    fg_color=("#EAF2FF", "#22344D"),
                    text_color=("#175CD3", "#B2CCFF"),
                    hover_color=("#DCE9FF", "#2A4160"),
                )
            else:
                button.configure(
                    fg_color="transparent",
                    text_color=("#475467", "#B7BDC8"),
                    hover_color=("#F2F4F7", "#252A33"),
                )

    def load_projects(self):
        for widget in self.project_list.winfo_children():
            widget.destroy()

        search_text = self.search.get().lower().strip()
        projects = self.pm.get_all_projects()
        filtered_projects = []

        for project in projects:
            title = str(project["title"] or "").lower()
            category = str(project["category"] or "").lower()
            status = project["status"]

            matches_search = search_text in title or search_text in category
            matches_status = self.current_status == "All" or status == self.current_status

            if matches_search and matches_status:
                filtered_projects.append(project)

        count = len(filtered_projects)
        self.result_label.configure(
            text=f"{count} project" if count == 1 else f"{count} projects"
        )

        if not filtered_projects:
            empty = ctk.CTkFrame(
                self.project_list,
                corner_radius=10,
                border_width=1,
                border_color=("#E4E7EC", "#2B303A"),
                fg_color=("#FFFFFF", "#181B21"),
            )
            empty.grid(
                row=0,
                column=0,
                columnspan=2,
                sticky="ew",
                padx=2,
                pady=2,
            )

            ctk.CTkLabel(
                empty,
                text="No projects found",
                font=("Segoe UI", 14, "bold"),
            ).pack(pady=(18, 3))

            ctk.CTkLabel(
                empty,
                text="Try another search or status filter.",
                font=("Segoe UI", 11),
                text_color=("#667085", "#98A2B3"),
            ).pack(pady=(0, 18))
            return

        for index, project in enumerate(filtered_projects):
            row = index // 2
            column = index % 2

            card = ProjectCard(
                self.project_list,
                project,
                self.app,
                self.load_projects,
            )
            card.grid(
                row=row,
                column=column,
                sticky="nsew",
                padx=5,
                pady=5,
            )
