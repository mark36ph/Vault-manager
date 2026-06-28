import customtkinter as ctk

from pages.base_page import BasePage


class DashboardPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Dashboard")
        
        self.app = app
        self.pm = pm

        self.build()

    def build(self):

        stats = ctk.CTkFrame(self.content)
        stats.pack(fill="x", pady=10)

        cards = [
            ("Projects", self.pm.project_count()),
            ("In Progress", self.pm.project_count()),
            ("Completed", 0),
            ("Scheduled", 0)
        ]

        for title, value in cards:

            card = ctk.CTkFrame(
                stats,
                width=180,
                height=120
            )

            card.pack(side="left", padx=10)

            ctk.CTkLabel(
                card,
                text=str(value),
                font=("Segoe UI", 34, "bold")
            ).pack(pady=(20, 5))

            ctk.CTkLabel(
                card,
                text=title
            ).pack()

        self.add_section_title("Quick Actions")

        buttons = ctk.CTkFrame(self.content)
        buttons.pack(anchor="w")

        ctk.CTkButton(
            buttons,
            text="➕ New Fact",
            command=self.app.show_new_fact
        ).pack(side="left", padx=5)

        ctk.CTkButton(
            buttons,
            text="📂 Projects",
            command=self.app.show_projects
        ).pack(side="left", padx=5)

        self.add_section_title("Recent Projects")

        projects = self.pm.get_all_projects()

        if not projects:

            ctk.CTkLabel(
                self.content,
                text="No projects yet."
            ).pack(anchor="w")

        else:

            for project in projects[:5]:

                ctk.CTkLabel(
                    self.content,
                    text=f"• {project['title']} ({project['category']})"
                ).pack(anchor="w", pady=2)