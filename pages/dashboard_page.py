import customtkinter as ctk

from pages.base_page import BasePage


class DashboardPage(BasePage):
    """Compact overview page for the main workspace."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Dashboard")
        self.app = app
        self.pm = pm

        # Tighten the inherited BasePage treatment for the modern shell.
        self.configure(fg_color="transparent")
        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=26, pady=(22, 4))
        self.content.pack_configure(padx=26, pady=(0, 22))

        self._build_subtitle()
        self.build()

    def _build_subtitle(self):
        self.subtitle = ctk.CTkLabel(
            self,
            text="Overview of your content workspace",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(
            fill="x",
            padx=26,
            pady=(0, 16),
            before=self.content,
        )

    def build(self):
        self._build_stats()
        self._build_actions()
        self._build_recent_projects()

    def _build_stats(self):
        stats = ctk.CTkFrame(self.content, fg_color="transparent")
        stats.pack(fill="x")

        cards = [
            ("Projects", self.pm.project_count()),
            ("In Progress", self.pm.count_projects_by_status("In Progress")),
            ("Completed", self.pm.count_projects_by_status("Completed")),
            ("Scheduled", self.pm.count_projects_by_status("Scheduled")),
            ("Published", self.pm.count_projects_by_status("Published")),
        ]

        for index in range(len(cards)):
            stats.grid_columnconfigure(index, weight=1, uniform="dashboard_stats")

        for index, (title, value) in enumerate(cards):
            card = ctk.CTkFrame(
                stats,
                height=88,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2E36"),
            )
            card.grid(
                row=0,
                column=index,
                sticky="nsew",
                padx=(0, 8) if index < len(cards) - 1 else 0,
            )
            card.grid_propagate(False)

            ctk.CTkLabel(
                card,
                text=str(value),
                font=("Segoe UI", 26, "bold"),
                anchor="w",
            ).pack(fill="x", padx=14, pady=(13, 0))

            ctk.CTkLabel(
                card,
                text=title,
                font=("Segoe UI", 11),
                text_color=("#667085", "#9298A4"),
                anchor="w",
            ).pack(fill="x", padx=14, pady=(0, 12))

    def _section_header(self, title, action_text=None, action_command=None):
        row = ctk.CTkFrame(self.content, fg_color="transparent")
        row.pack(fill="x", pady=(22, 8))

        ctk.CTkLabel(
            row,
            text=title,
            font=("Segoe UI", 15, "bold"),
            anchor="w",
        ).pack(side="left")

        if action_text and action_command:
            ctk.CTkButton(
                row,
                text=action_text,
                width=0,
                height=28,
                corner_radius=6,
                fg_color="transparent",
                hover_color=("#E9EDF3", "#242830"),
                text_color=("#475467", "#AAB1BC"),
                font=("Segoe UI", 11),
                command=action_command,
            ).pack(side="right")

    def _build_actions(self):
        self._section_header("Quick actions")

        actions = ctk.CTkFrame(self.content, fg_color="transparent")
        actions.pack(fill="x")

        ctk.CTkButton(
            actions,
            text="New Fact",
            width=112,
            height=36,
            corner_radius=7,
            font=("Segoe UI", 12, "bold"),
            command=self.app.show_new_fact,
        ).pack(side="left")

        ctk.CTkButton(
            actions,
            text="Projects",
            width=104,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A3F49"),
            hover_color=("#EAECF0", "#242830"),
            text_color=("#344054", "#D0D5DD"),
            font=("Segoe UI", 12),
            command=self.app.show_projects,
        ).pack(side="left", padx=(8, 0))

    def _build_recent_projects(self):
        self._section_header(
            "Recent projects",
            action_text="View all  →",
            action_command=self.app.show_projects,
        )

        recent_frame = ctk.CTkFrame(
            self.content,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2E36"),
        )
        recent_frame.pack(fill="x")

        projects = self.pm.get_all_projects()
        if not projects:
            ctk.CTkLabel(
                recent_frame,
                text="No projects yet. Create a fact to get started.",
                font=("Segoe UI", 12),
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=14, pady=16)
            return

        for index, project in enumerate(projects[:5]):
            if index:
                ctk.CTkFrame(
                    recent_frame,
                    height=1,
                    fg_color=("#EAECF0", "#292D35"),
                ).pack(fill="x", padx=12)
            self.add_recent_project(recent_frame, project)

    def add_recent_project(self, parent, project):
        try:
            is_pinned = project["pinned"] == 1
        except Exception:
            is_pinned = False

        title = project["title"] or "Untitled"
        category = project["category"] or "Uncategorised"
        status = project["status"] or ""

        try:
            scheduled_for = project["scheduled_for"] or ""
        except Exception:
            scheduled_for = ""

        row = ctk.CTkFrame(
            parent,
            height=46,
            fg_color="transparent",
            corner_radius=6,
            cursor="hand2",
        )
        row.pack(fill="x", padx=6, pady=3)
        row.pack_propagate(False)

        title_text = f"●  {title}" if is_pinned else title
        title_label = ctk.CTkLabel(
            row,
            text=title_text,
            font=("Segoe UI", 12, "bold" if is_pinned else "normal"),
            text_color=("#344054", "#E4E7EC"),
            anchor="w",
            cursor="hand2",
        )
        title_label.pack(side="left", padx=(10, 12))

        category_label = ctk.CTkLabel(
            row,
            text=category,
            font=("Segoe UI", 11),
            text_color=("#667085", "#8F96A3"),
            cursor="hand2",
        )
        category_label.pack(side="left", padx=(0, 12))

        status_style = self.get_status_style(status)
        status_label = ctk.CTkLabel(
            row,
            text=status,
            font=("Segoe UI", 10, "bold"),
            fg_color=status_style["fg_color"],
            text_color=status_style["text_color"],
            corner_radius=5,
            padx=7,
            pady=2,
            cursor="hand2",
        )
        status_label.pack(side="right", padx=(8, 10))

        scheduled_label = None
        if scheduled_for and status == "Scheduled":
            scheduled_label = ctk.CTkLabel(
                row,
                text=scheduled_for,
                font=("Segoe UI", 10),
                text_color=("#667085", "#8F96A3"),
                cursor="hand2",
            )
            scheduled_label.pack(side="right", padx=(8, 0))

        def open_project(event=None):
            self.app.show_project_viewer(project["id"])

        def hover_on(event=None):
            row.configure(fg_color=("#F2F4F7", "#22262D"))

        def hover_off(event=None):
            row.configure(fg_color="transparent")

        clickable_widgets = [row, title_label, category_label, status_label]
        if scheduled_label is not None:
            clickable_widgets.append(scheduled_label)

        for widget in clickable_widgets:
            widget.bind("<Button-1>", open_project)
            widget.bind("<Enter>", hover_on)
            widget.bind("<Leave>", hover_off)

    def get_status_style(self, status):
        styles = {
            "In Progress": {
                "fg_color": ("#EAF2FF", "#203451"),
                "text_color": ("#175CD3", "#B9D3FF"),
            },
            "Scheduled": {
                "fg_color": ("#FFF6E0", "#463A20"),
                "text_color": ("#9A6700", "#F4D47A"),
            },
            "Completed": {
                "fg_color": ("#EAF7EF", "#203C2A"),
                "text_color": ("#18794E", "#9DDBB3"),
            },
            "Published": {
                "fg_color": ("#F3ECFF", "#382B4A"),
                "text_color": ("#6941C6", "#D3BCFF"),
            },
        }
        return styles.get(
            status,
            {
                "fg_color": ("#F2F4F7", "#2A2E36"),
                "text_color": ("#475467", "#C4C9D1"),
            },
        )
