import customtkinter as ctk
from datetime import datetime


class ProjectCard(ctk.CTkFrame):

    def __init__(self, parent, project, app, refresh_callback=None):
        super().__init__(parent)

        self.project = project
        self.app = app
        self.refresh_callback = refresh_callback

        self.configure(
            corner_radius=8,
            border_width=1
        )

        is_pinned = False

        try:
            is_pinned = project["pinned"] == 1
        except Exception:
            is_pinned = False

        title_icon = "📌" if is_pinned else "📁"

        # ==========================
        # Header row
        # ==========================

        header = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=12,
            pady=(10, 4)
        )

        ctk.CTkLabel(
            header,
            text=f"{title_icon} {project['title']}",
            font=("Segoe UI", 17, "bold"),
            wraplength=520,
            justify="left"
        ).pack(
            side="left",
            anchor="w",
            fill="x",
            expand=True
        )

        # ==========================
        # Details
        # ==========================

        details = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        details.pack(
            fill="x",
            padx=12
        )

        ctk.CTkLabel(
            details,
            text=f"📂 {project['category']}",
            font=("Segoe UI", 13),
            text_color="gray"
        ).pack(
            anchor="w"
        )

        status_row = ctk.CTkFrame(
            details,
            fg_color="transparent"
        )

        status_row.pack(
            anchor="w",
            pady=(5, 3)
        )

        status_style = self.get_status_style(
            project["status"]
        )

        status_menu = ctk.CTkOptionMenu(
            status_row,
            values=[
                "In Progress",
                "Scheduled",
                "Completed",
                "Published"
            ],
            width=130,
            height=30,
            fg_color=status_style["fg_color"],
            button_color=status_style["fg_color"],
            button_hover_color=status_style["fg_color"],
            text_color=status_style["text_color"],
            command=self.change_status
        )

        status_menu.pack(
            side="left"
        )

        status_menu.set(
            project["status"]
        )

        scheduled_for = ""

        try:
            scheduled_for = project["scheduled_for"]
        except Exception:
            scheduled_for = ""

        if scheduled_for and project["status"] == "Scheduled":

            scheduled_display = self.format_date(
                scheduled_for
            )

            ctk.CTkLabel(
                status_row,
                text=f"🗓 {scheduled_display}",
                font=("Segoe UI", 12),
                fg_color="#3b3b3b",
                text_color="white",
                corner_radius=8,
                padx=8,
                pady=3
            ).pack(
                side="left",
                padx=(6, 0)
            )

        created = self.format_date(
            project["created"]
        )

        updated = ""

        try:
            updated = project["updated"]
        except Exception:
            updated = ""

        if not updated:
            updated = project["created"]

        updated = self.format_date(
            updated
        )

        ctk.CTkLabel(
            details,
            text=f"📅 {created}    🕒 {updated}",
            font=("Segoe UI", 12),
            text_color="gray"
        ).pack(
            anchor="w",
            pady=(2, 0)
        )

        if is_pinned:

            ctk.CTkLabel(
                details,
                text="📌 Pinned",
                font=("Segoe UI", 12),
                fg_color="#5b3f8c",
                text_color="white",
                corner_radius=8,
                padx=8,
                pady=3
            ).pack(
                anchor="w",
                pady=(6, 0)
            )

        # ==========================
        # Buttons
        # ==========================

        buttons = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=12,
            pady=(8, 10)
        )

        ctk.CTkButton(
            buttons,
            text="✏ Edit",
            width=75,
            height=30,
            command=lambda: self.app.show_edit_project(
                project["id"]
            )
        ).pack(
            side="left",
            padx=(0, 5)
        )

        ctk.CTkButton(
            buttons,
            text="👁 Open",
            width=75,
            height=30,
            command=lambda: self.app.show_project_viewer(
                project["id"]
            )
        ).pack(
            side="left",
            padx=(0, 5)
        )

        pin_text = "📌 Unpin" if is_pinned else "📌 Pin"

        ctk.CTkButton(
            buttons,
            text=pin_text,
            width=80,
            height=30,
            command=self.toggle_pin
        ).pack(
            side="left",
            padx=(0, 5)
        )

        ctk.CTkButton(
            buttons,
            text="🗑",
            width=45,
            height=30,
            fg_color="#B22222",
            hover_color="#8B0000",
            command=lambda: self.app.delete_project(
                project
            )
        ).pack(
            side="right"
        )

    def parse_schedule_date(self, value):

        value = value.strip()

        try:

            date = datetime.strptime(
                value,
                "%d/%m/%Y %H:%M"
            )

            return date.strftime(
                "%Y-%m-%d %H:%M"
            )

        except Exception:

            return None
            
    def change_status(self, new_status):

        if new_status == "Scheduled":

            dialog = ctk.CTkInputDialog(
                text="When is this scheduled for?\n\nUse: DD/MM/YYYY HH:MM\nExample: 25/07/2026 18:00",
                title="Schedule Project"
            )

            scheduled_text = dialog.get_input()

            if not scheduled_text:

                if self.refresh_callback:

                    self.refresh_callback()

                return

            scheduled_value = self.parse_schedule_date(
                scheduled_text
            )

            if scheduled_value is None:

                from tkinter import messagebox

                messagebox.showerror(
                    "Invalid Date",
                    "Please enter the date like this:\n\n25/07/2026 18:00"
                )

                if self.refresh_callback:

                    self.refresh_callback()

                return

            self.app.pm.db.update_project_status(
                self.project["id"],
                new_status
            )

            self.app.pm.db.update_project_schedule(
                self.project["id"],
                scheduled_value
            )

        else:

            self.app.pm.db.update_project_status(
                self.project["id"],
                new_status
            )

        if self.refresh_callback:

            self.refresh_callback()
            
    def toggle_pin(self):

        self.app.pm.db.toggle_project_pinned(
            self.project["id"]
        )

        if self.refresh_callback:

            self.refresh_callback()

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

    def format_date(self, value):

        if not value:
            return ""

        try:

            date = datetime.strptime(
                value,
                "%Y-%m-%d %H:%M"
            )

            return date.strftime(
                "%d %b %Y %H:%M"
            )

        except Exception:

            return value