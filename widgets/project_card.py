import customtkinter as ctk


class ProjectCard(ctk.CTkFrame):

    def __init__(self, parent, project, app):
        super().__init__(parent)

        self.project = project
        self.app = app

        self.configure(
            corner_radius=10,
            border_width=1
        )

        # ==========================
        # Title
        # ==========================

        ctk.CTkLabel(
            self,
            text=f"📁 {project['title']}",
            font=("Segoe UI", 22, "bold")
        ).pack(anchor="w", padx=20, pady=(15, 5))

        # ==========================
        # Details
        # ==========================

        details = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        details.pack(
            fill="x",
            padx=20
        )

        ctk.CTkLabel(
            details,
            text=f"📂 Category: {project['category']}"
        ).pack(anchor="w")

        ctk.CTkLabel(
            details,
            text=f"📌 Status: {project['status']}"
        ).pack(anchor="w")

        ctk.CTkLabel(
            details,
            text=f"📅 Created: {project['created']}"
        ).pack(anchor="w")

        # ==========================
        # Buttons
        # ==========================

        buttons = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=20,
            pady=15
        )

        ctk.CTkButton(
            buttons,
            text="✏ Edit",
            width=100,
            command=lambda: self.app.show_edit_project(project["id"])
        ).pack(side="left", padx=5)

        ctk.CTkButton(
            buttons,
            text="📂 Open",
            width=100,
            command=lambda: self.app.open_project_folder(project)
        ).pack(side="left", padx=5)

        ctk.CTkButton(
            buttons,
            text="🗑 Delete",
            width=100,
            fg_color="#B22222",
            hover_color="#8B0000",
            command=lambda: self.app.delete_project(project)
        ).pack(side="right", padx=5)