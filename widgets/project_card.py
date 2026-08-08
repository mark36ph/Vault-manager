import customtkinter as ctk
from datetime import datetime
from tkinter import messagebox


class ProjectCard(ctk.CTkFrame):
    """Compact project summary card used by the Projects page."""

    def __init__(self, parent, project, app, refresh_callback=None):
        super().__init__(
            parent,
            corner_radius=10,
            border_width=1,
            border_color=("#E4E7EC", "#2B303A"),
            fg_color=("#FFFFFF", "#181B21"),
        )

        self.project = project
        self.app = app
        self.refresh_callback = refresh_callback

        is_pinned = self._value("pinned", 0) == 1

        # Header
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.pack(fill="x", padx=14, pady=(12, 5))

        title_text = self._value("title", "Untitled project")
        if is_pinned:
            title_text = f"📌  {title_text}"

        ctk.CTkLabel(
            header,
            text=title_text,
            font=("Segoe UI Emoji", 15, "bold"),
            anchor="w",
            justify="left",
            wraplength=460,
        ).pack(side="left", fill="x", expand=True)

        # Metadata
        meta = ctk.CTkFrame(self, fg_color="transparent")
        meta.pack(fill="x", padx=14)

        category = self._value("category", "") or "Uncategorized"
        ctk.CTkLabel(
            meta,
            text=category,
            font=("Segoe UI", 12),
            text_color=("#667085", "#98A2B3"),
            anchor="w",
        ).pack(fill="x")

        status_row = ctk.CTkFrame(self, fg_color="transparent")
        status_row.pack(fill="x", padx=14, pady=(8, 4))

        status = self._value("status", "In Progress")
        style = self.get_status_style(status)
        self.status_menu = ctk.CTkOptionMenu(
            status_row,
            values=["In Progress", "Scheduled", "Completed", "Published"],
            width=122,
            height=28,
            corner_radius=7,
            fg_color=style["fg_color"],
            button_color=style["fg_color"],
            button_hover_color=style["hover_color"],
            text_color=style["text_color"],
            font=("Segoe UI", 11),
            dropdown_font=("Segoe UI", 11),
            command=self.change_status,
        )
        self.status_menu.pack(side="left")
        self.status_menu.set(status)

        scheduled_for = self._value("scheduled_for", "")
        if scheduled_for and status == "Scheduled":
            ctk.CTkLabel(
                status_row,
                text=f"{self.format_date(scheduled_for)}",
                font=("Segoe UI", 11),
                text_color=("#7A5D00", "#E7C968"),
                fg_color=("#FFF7D6", "#3A3217"),
                corner_radius=6,
                padx=7,
                pady=2,
            ).pack(side="left", padx=(7, 0))

        created = self.format_date(self._value("created", ""))
        updated_raw = self._value("updated", "") or self._value("created", "")
        updated = self.format_date(updated_raw)

        date_parts = []
        if created:
            date_parts.append(f"Created {created}")
        if updated:
            date_parts.append(f"Updated {updated}")

        if date_parts:
            ctk.CTkLabel(
                self,
                text="   •   ".join(date_parts),
                font=("Segoe UI", 10),
                text_color=("#98A2B3", "#7F8794"),
                anchor="w",
            ).pack(fill="x", padx=14, pady=(2, 0))

        # Actions
        actions = ctk.CTkFrame(self, fg_color="transparent")
        actions.pack(fill="x", padx=14, pady=(10, 12))

        secondary = {
            "height": 30,
            "corner_radius": 7,
            "fg_color": "transparent",
            "border_width": 1,
            "border_color": ("#D0D5DD", "#3A404B"),
            "text_color": ("#344054", "#D0D5DD"),
            "hover_color": ("#F2F4F7", "#252A33"),
            "font": ("Segoe UI Emoji", 11),
        }

        ctk.CTkButton(
            actions,
            text="Open",
            width=66,
            command=lambda: self.app.show_project_viewer(self.project["id"]),
            **secondary,
        ).pack(side="left", padx=(0, 5))

        ctk.CTkButton(
            actions,
            text="Edit",
            width=62,
            command=lambda: self.app.show_edit_project(self.project["id"]),
            **secondary,
        ).pack(side="left", padx=(0, 5))

        pin_text = "Unpin" if is_pinned else "Pin"
        ctk.CTkButton(
            actions,
            text=pin_text,
            width=62,
            command=self.toggle_pin,
            **secondary,
        ).pack(side="left")

        ctk.CTkButton(
            actions,
            text="Delete",
            width=64,
            height=30,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#FDA29B", "#7A3434"),
            text_color=("#B42318", "#FDA29B"),
            hover_color=("#FEF3F2", "#3A2020"),
            font=("Segoe UI", 11),
            command=lambda: self.app.delete_project(self.project),
        ).pack(side="right")

    def _value(self, key, default=""):
        try:
            value = self.project[key]
        except Exception:
            return default
        return default if value is None else value

    def parse_schedule_date(self, value):
        value = value.strip()
        try:
            date = datetime.strptime(value, "%d/%m/%Y %H:%M")
            return date.strftime("%Y-%m-%d %H:%M")
        except Exception:
            return None

    def change_status(self, new_status):
        scheduled_value = ""

        if new_status == "Scheduled":
            dialog = ctk.CTkInputDialog(
                text=(
                    "When is this scheduled for?\n\n"
                    "Use: DD/MM/YYYY HH:MM\n"
                    "Example: 25/07/2026 18:00"
                ),
                title="Schedule Project",
            )
            scheduled_text = dialog.get_input()

            if not scheduled_text:
                if self.refresh_callback:
                    self.refresh_callback()
                return

            scheduled_value = self.parse_schedule_date(scheduled_text)
            if scheduled_value is None:
                messagebox.showerror(
                    "Invalid Date",
                    "Please enter the date like this:\n\n25/07/2026 18:00",
                )
                if self.refresh_callback:
                    self.refresh_callback()
                return

        try:
            self.project = self.app.pm.change_project_status(
                project_id=self.project["id"],
                new_status=new_status,
                scheduled_for=scheduled_value,
            )
        except Exception as error:
            messagebox.showerror("Status Change Failed", str(error))
            if self.refresh_callback:
                self.refresh_callback()
            return

        if self.refresh_callback:
            self.refresh_callback()

    def toggle_pin(self):
        self.app.pm.db.toggle_project_pinned(self.project["id"])
        if self.refresh_callback:
            self.refresh_callback()

    def get_status_style(self, status):
        styles = {
            "In Progress": {
                "fg_color": ("#EAF2FF", "#22344D"),
                "hover_color": ("#DCE9FF", "#2A4160"),
                "text_color": ("#175CD3", "#B2CCFF"),
            },
            "Scheduled": {
                "fg_color": ("#FFF7D6", "#3A3217"),
                "hover_color": ("#FCEEBB", "#49401E"),
                "text_color": ("#7A5D00", "#E7C968"),
            },
            "Completed": {
                "fg_color": ("#ECFDF3", "#17372A"),
                "hover_color": ("#DDF8E8", "#1F4635"),
                "text_color": ("#027A48", "#75E0A7"),
            },
            "Published": {
                "fg_color": ("#F4EBFF", "#34244B"),
                "hover_color": ("#EAD9FF", "#42305D"),
                "text_color": ("#6941C6", "#D6BBFB"),
            },
        }
        return styles.get(
            status,
            {
                "fg_color": ("#F2F4F7", "#2A2E35"),
                "hover_color": ("#E4E7EC", "#343942"),
                "text_color": ("#475467", "#D0D5DD"),
            },
        )

    def format_date(self, value):
        if not value:
            return ""
        try:
            date = datetime.strptime(value, "%Y-%m-%d %H:%M")
            return date.strftime("%d %b %Y %H:%M")
        except Exception:
            return value
