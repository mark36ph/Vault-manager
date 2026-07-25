import customtkinter as ctk
from pages.base_page import BasePage
from common.ui_fonts import EMOJI_BUTTON_FONT

class DashboardPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Dashboard")

        self.app = app
        self.pm = pm
        self.build()

    def build(self):

        stats = ctk.CTkFrame(
            self.content
        )

        stats.pack(
            fill="x",
            pady=10
        )

        cards = [
            ("Projects", self.pm.project_count()),
            ("In Progress", self.pm.count_projects_by_status("In Progress")),
            ("Completed", self.pm.count_projects_by_status("Completed")),
            ("Scheduled", self.pm.count_projects_by_status("Scheduled")),
            ("Published", self.pm.count_projects_by_status("Published"))
        ]

        for title, value in cards:

            card = ctk.CTkFrame(
                stats,
                width=180,
                height=120
            )

            card.pack(
                side="left",
                padx=10
            )

            ctk.CTkLabel(
                card,
                text=str(value),
                font=("Segoe UI", 34, "bold")
            ).pack(
                pady=(20, 5)
            )

            ctk.CTkLabel(
                card,
                text=title
            ).pack()

        self.add_section_title(
            "Quick Actions"
        )

        buttons = ctk.CTkFrame(
            self.content
        )

        buttons.pack(
            anchor="w"
        )

        ctk.CTkButton(
            buttons,
            text="➕ New Fact",
            font=EMOJI_BUTTON_FONT,
            command=self.app.show_new_fact
        ).pack(
            side="left",
            padx=5
        )

        ctk.CTkButton(
            buttons,
            text="📂 Projects",
            font=EMOJI_BUTTON_FONT,
            command=self.app.show_projects
        ).pack(
            side="left",
            padx=5
        )

        self.add_section_title(
            "Recent Projects"
        )

        recent_frame = ctk.CTkFrame(
            self.content
        )

        recent_frame.pack(
            fill="x",
            pady=(0, 10)
        )

        projects = self.pm.get_all_projects()

        if not projects:

            ctk.CTkLabel(
                recent_frame,
                text="No projects yet.",
                text_color="gray"
            ).pack(
                anchor="w",
                padx=15,
                pady=15
            )

            return

        for project in projects[:5]:

            self.add_recent_project(
                recent_frame,
                project
            )

        ctk.CTkButton(
            recent_frame,
            text="View all projects →",
            height=30,
            fg_color="transparent",
            text_color="#4da3ff",
            hover_color="#2b2b2b",
            command=self.app.show_projects
        ).pack(
            anchor="w",
            padx=15,
            pady=(6, 10)
        )
        
    def add_recent_project(self, parent, project):

        is_pinned = False

        try:

            is_pinned = project["pinned"] == 1

        except Exception:

            is_pinned = False

        row = ctk.CTkFrame(
            parent,
            fg_color="transparent",
            corner_radius=8
        )

        row.pack(
            fill="x",
            padx=10,
            pady=2
        )

        title = project["title"]
        category = project["category"]
        status = project["status"]

        title_text = f"📌 {title}" if is_pinned else f"• {title}"

        title_label = ctk.CTkLabel(
            row,
            text=title_text,
            font=("Segoe UI", 15, "bold" if is_pinned else "normal"),
            text_color="#c9b6ff" if is_pinned else "#d9d9d9",
            cursor="hand2"
        )

        title_label.pack(
            side="left",
            padx=(10, 8),
            pady=5
        )

        category_label = ctk.CTkLabel(
            row,
            text=category,
            font=("Segoe UI", 13),
            text_color="gray",
            cursor="hand2"
        )

        category_label.pack(
            side="left",
            padx=(0, 8),
            pady=5
        )

        status_style = self.get_status_style(
            status
        )

        meta_label = ctk.CTkLabel(
            row,
            text=status,
            font=("Segoe UI", 12),
            fg_color=status_style["fg_color"],
            text_color=status_style["text_color"],
            corner_radius=8,
            padx=8,
            pady=2,
            cursor="hand2"
        )

        meta_label.pack(
            side="left",
            padx=(0, 8),
            pady=5
        )

        scheduled_label = None

        scheduled_for = ""

        try:

            scheduled_for = project["scheduled_for"]

        except Exception:

            scheduled_for = ""

        if scheduled_for and status == "Scheduled":

            scheduled_label = ctk.CTkLabel(
                row,
                text=f"🗓 {scheduled_for}",
                font=("Segoe UI", 12),
                text_color="gray",
                cursor="hand2"
            )

            scheduled_label.pack(
                side="left",
                padx=(0, 8),
                pady=5
            )

        def open_project(event=None):

            self.app.show_project_viewer(
                project["id"]
            )

        def hover_on(event=None):

            row.configure(
                fg_color="#2b2b2b"
            )

            title_label.configure(
                text_color="#ffffff"
            )

        def hover_off(event=None):

            row.configure(
                fg_color="transparent"
            )

            title_label.configure(
                text_color="#c9b6ff" if is_pinned else "#d9d9d9"
            )

        clickable_widgets = [
            row,
            title_label,
            category_label,
            meta_label
        ]

        if scheduled_label is not None:

            clickable_widgets.append(
                scheduled_label
            )

        for widget in clickable_widgets:

            widget.bind(
                "<Button-1>",
                open_project
            )

            widget.bind(
                "<Enter>",
                hover_on
            )

            widget.bind(
                "<Leave>",
                hover_off
            )
            
        def open_project(event=None):

            self.app.show_project_viewer(
                project["id"]
            )

        def hover_on(event=None):

            row.configure(
                fg_color="#2b2b2b"
            )

            title_label.configure(
                text_color="#ffffff"
            )

        def hover_off(event=None):

            row.configure(
                fg_color="transparent"
            )

            title_label.configure(
                text_color="#c9b6ff" if is_pinned else "#d9d9d9"
            )

        row.bind(
            "<Button-1>",
            open_project
        )

        title_label.bind(
            "<Button-1>",
            open_project
        )

        meta_label.bind(
            "<Button-1>",
            open_project
        )

        row.bind(
            "<Enter>",
            hover_on
        )

        title_label.bind(
            "<Enter>",
            hover_on
        )

        meta_label.bind(
            "<Enter>",
            hover_on
        )

        row.bind(
            "<Leave>",
            hover_off
        )

        title_label.bind(
            "<Leave>",
            hover_off
        )

        meta_label.bind(
            "<Leave>",
            hover_off
        )

        category_label.bind(
            "<Button-1>",
            open_project
        )

        category_label.bind(
            "<Enter>",
            hover_on
        )

        category_label.bind(
            "<Leave>",
            hover_off
        )
        
    def get_status_style(self, status):

        if status == "In Progress":

            return {
                "fg_color": "#1f5f8b",
                "text_color": "white"
            }

        if status == "Scheduled":

            return {
                "fg_color": "#8a6f1f",
                "text_color": "white"
            }

        if status == "Completed":

            return {
                "fg_color": "#2f7d32",
                "text_color": "white"
            }
        if status == "Published":

            return {
                "fg_color": "#6f42c1",
                "text_color": "white"
            }
            
        return {
            "fg_color": "#444444",
            "text_color": "white"
        }