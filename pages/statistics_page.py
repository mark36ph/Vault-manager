import customtkinter as ctk

from pages.base_page import BasePage


class StatisticsPage(BasePage):
    """Compact project statistics overview."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Statistics")
        self.app = app

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="A quick overview of your project library and publishing progress.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)
        self.content.pack_configure(padx=24, pady=(0, 20))

        self.build()

    def build(self):
        stats = [
            ("Total Projects", self.pm.project_count()),
            ("In Progress", self.pm.count_projects_by_status("In Progress")),
            ("Scheduled", self.pm.count_projects_by_status("Scheduled")),
            ("Completed", self.pm.count_projects_by_status("Completed")),
            ("Published", self.pm.count_projects_by_status("Published")),
        ]

        grid = ctk.CTkFrame(self.content, fg_color="transparent")
        grid.pack(fill="x")
        for column in range(3):
            grid.grid_columnconfigure(column, weight=1)

        for index, (title, value) in enumerate(stats):
            row = index // 3
            column = index % 3
            card = ctk.CTkFrame(
                grid,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            card.grid(
                row=row,
                column=column,
                sticky="nsew",
                padx=(0 if column == 0 else 5, 0 if column == 2 else 5),
                pady=(0, 10),
            )

            ctk.CTkLabel(
                card,
                text=str(value),
                font=("Segoe UI", 28, "bold"),
            ).pack(anchor="w", padx=16, pady=(15, 2))
            ctk.CTkLabel(
                card,
                text=title,
                font=("Segoe UI", 12),
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=16, pady=(0, 15))

        total = max(1, self.pm.project_count())
        completed = self.pm.count_projects_by_status("Completed")
        published = self.pm.count_projects_by_status("Published")
        finished = completed + published
        ratio = min(1.0, finished / total)

        summary = ctk.CTkFrame(
            self.content,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        summary.pack(fill="x", pady=(4, 0))

        ctk.CTkLabel(
            summary,
            text="Completion overview",
            font=("Segoe UI", 15, "bold"),
        ).pack(anchor="w", padx=16, pady=(15, 4))
        ctk.CTkLabel(
            summary,
            text=f"{finished} of {self.pm.project_count()} projects are completed or published.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=16, pady=(0, 10))

        progress = ctk.CTkProgressBar(summary, height=8)
        progress.set(ratio if self.pm.project_count() else 0)
        progress.pack(fill="x", padx=16, pady=(0, 16))
