import customtkinter as ctk
from datetime import datetime, timedelta
from tkinter import messagebox


class ScheduleDialog(ctk.CTkToplevel):
    """Small date/time picker used when moving a project to Scheduled."""

    def __init__(self, parent):
        super().__init__(parent)
        self.result = None

        self.title("Schedule Project")
        self.geometry("430x315")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()

        tomorrow = datetime.now() + timedelta(days=1)

        ctk.CTkLabel(
            self,
            text="Schedule Project",
            font=("Segoe UI", 20, "bold"),
            anchor="w",
        ).pack(fill="x", padx=22, pady=(20, 4))

        ctk.CTkLabel(
            self,
            text="Choose the date and time separately.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#98A2B3"),
            anchor="w",
        ).pack(fill="x", padx=22, pady=(0, 16))

        fields = ctk.CTkFrame(self, fg_color="transparent")
        fields.pack(fill="x", padx=22)
        fields.grid_columnconfigure((0, 1), weight=1)

        ctk.CTkLabel(
            fields,
            text="Date",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).grid(row=0, column=0, sticky="w", padx=(0, 6), pady=(0, 5))

        ctk.CTkLabel(
            fields,
            text="Time",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).grid(row=0, column=1, sticky="w", padx=(6, 0), pady=(0, 5))

        self.date_entry = ctk.CTkEntry(
            fields,
            height=36,
            placeholder_text="DD/MM/YYYY",
        )
        self.date_entry.grid(row=1, column=0, sticky="ew", padx=(0, 6))
        self.date_entry.insert(0, tomorrow.strftime("%d/%m/%Y"))

        times = [f"{hour:02d}:{minute:02d}" for hour in range(0, 24) for minute in (0, 30)]
        self.time_menu = ctk.CTkOptionMenu(
            fields,
            values=times,
            height=36,
        )
        self.time_menu.grid(row=1, column=1, sticky="ew", padx=(6, 0))
        self.time_menu.set("18:00")

        quick = ctk.CTkFrame(self, fg_color="transparent")
        quick.pack(fill="x", padx=22, pady=(14, 0))

        ctk.CTkLabel(
            quick,
            text="Quick date:",
            font=("Segoe UI", 11),
            text_color=("#667085", "#98A2B3"),
        ).pack(side="left", padx=(0, 8))

        for label, days in (("Tomorrow", 1), ("+3 days", 3), ("+7 days", 7)):
            ctk.CTkButton(
                quick,
                text=label,
                width=78,
                height=28,
                fg_color="transparent",
                border_width=1,
                border_color=("#D0D5DD", "#3A404B"),
                text_color=("#344054", "#D0D5DD"),
                command=lambda offset=days: self._set_date(offset),
            ).pack(side="left", padx=3)

        self.error_label = ctk.CTkLabel(
            self,
            text="",
            font=("Segoe UI", 11),
            text_color=("#B42318", "#FDA29B"),
            anchor="w",
        )
        self.error_label.pack(fill="x", padx=22, pady=(12, 0))

        buttons = ctk.CTkFrame(self, fg_color="transparent")
        buttons.pack(side="bottom", fill="x", padx=22, pady=20)

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=96,
            height=34,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            command=self._cancel,
        ).pack(side="right", padx=(8, 0))

        ctk.CTkButton(
            buttons,
            text="Schedule",
            width=104,
            height=34,
            command=self._confirm,
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", self._cancel)
        self.bind("<Escape>", lambda _event: self._cancel())
        self.bind("<Return>", lambda _event: self._confirm())
        self.date_entry.focus_set()

    def _set_date(self, days):
        value = datetime.now() + timedelta(days=days)
        self.date_entry.delete(0, "end")
        self.date_entry.insert(0, value.strftime("%d/%m/%Y"))

    def _confirm(self):
        raw = f"{self.date_entry.get().strip()} {self.time_menu.get().strip()}"
        try:
            value = datetime.strptime(raw, "%d/%m/%Y %H:%M")
        except ValueError:
            self.error_label.configure(text="Enter a valid date in DD/MM/YYYY format.")
            return

        if value <= datetime.now():
            self.error_label.configure(text="Choose a future date and time.")
            return

        self.result = value
        self.destroy()

    def _cancel(self):
        self.result = None
        self.destroy()


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
            return datetime.strptime(value, "%d/%m/%Y %H:%M")
        except Exception:
            return None

    def change_status(self, new_status):
        current_status = str(self._value("status", "In Progress") or "In Progress")
        if new_status == current_status:
            return

        scheduled_value = ""

        if new_status == "Scheduled":
            dialog = ScheduleDialog(self)
            self.wait_window(dialog)
            scheduled_date = dialog.result

            if scheduled_date is None:
                if self.refresh_callback:
                    self.refresh_callback()
                return

            scheduled_value = scheduled_date.strftime("%Y-%m-%d %H:%M")

        try:
            self.project = self.app.pm.change_project_status(
                project_id=self.project["id"],
                new_status=new_status,
                scheduled_for=scheduled_value,
            )
        except Exception as error:
            messagebox.showerror("Status Change Failed", str(error), parent=self)
            if self.refresh_callback:
                self.refresh_callback()
            return

        if self.refresh_callback:
            self.refresh_callback()

    def toggle_pin(self):
        try:
            self.app.pm.db.toggle_project_pinned(self.project["id"])
        except Exception as error:
            messagebox.showerror("Pin Project", str(error), parent=self)
            return

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
