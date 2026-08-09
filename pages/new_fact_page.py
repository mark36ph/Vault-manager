from tkinter import messagebox

import customtkinter as ctk

from common.chatgpt_import_parser import ChatGPTImportParser
from common.ui_fonts import EMOJI_BUTTON_FONT, EMOJI_FONT
from pages.base_page import BasePage
from widgets.project_card import ScheduleDialog


class NewFactPage(BasePage):
    """Create a new fact project with a compact two-column editor."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "New Fact")
        self.app = app
        self.scheduled_for = ""
        self._last_non_scheduled_status = "In Progress"
        self._keyboard_bindings = []
        self.build()
        self._install_keyboard_scrolling()

    def build(self):
        self.header.configure(font=("Segoe UI", 24, "bold"))

        subtitle = ctk.CTkLabel(
            self,
            text="Create a project, add its core content, and choose how it enters the workflow.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        subtitle.pack(fill="x", padx=30, pady=(0, 8))

        main = ctk.CTkFrame(self.content, fg_color="transparent")
        main.pack(fill="both", expand=True)

        self.left_panel = ctk.CTkFrame(
            main,
            corner_radius=8,
            fg_color=("#FFFFFF", "#17191F"),
            border_width=1,
            border_color=("#E4E7EC", "#292D36"),
        )
        self.left_panel.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0, 10),
        )

        self.right_panel = ctk.CTkFrame(
            main,
            width=280,
            corner_radius=8,
            fg_color=("#FFFFFF", "#17191F"),
            border_width=1,
            border_color=("#E4E7EC", "#292D36"),
        )
        self.right_panel.pack(side="right", fill="y")
        self.right_panel.pack_propagate(False)

        self.build_right_panel()
        self.build_left_panel()
        self.after(50, self.title_entry.focus_set)

    def build_left_panel(self):
        header = ctk.CTkFrame(self.left_panel, fg_color="transparent")
        header.pack(fill="x", padx=16, pady=(14, 10))

        ctk.CTkLabel(
            header,
            text="Project details",
            font=("Segoe UI", 16, "bold"),
        ).pack(side="left")

        self.create_button = ctk.CTkButton(
            header,
            text="Create Fact",
            height=36,
            width=118,
            corner_radius=7,
            font=("Segoe UI", 13, "bold"),
            command=self.create_project,
        )
        self.create_button.pack(side="right")

        self.status_label = ctk.CTkLabel(
            self.left_panel,
            text="",
            font=("Segoe UI", 11),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.status_label.pack(fill="x", padx=16, pady=(0, 6))

        self.form = ctk.CTkScrollableFrame(
            self.left_panel,
            fg_color="transparent",
            corner_radius=0,
        )
        self.form.pack(
            fill="both",
            expand=True,
            padx=14,
            pady=(0, 14),
        )

        self._build_metadata_fields()
        self._build_content_columns()
        self.update_preview()

    def _build_metadata_fields(self):
        metadata = ctk.CTkFrame(self.form, fg_color="transparent")
        metadata.pack(fill="x", pady=(0, 12))

        metadata.grid_columnconfigure(0, weight=3)
        metadata.grid_columnconfigure(1, weight=1)
        metadata.grid_columnconfigure(2, weight=1)
        metadata.grid_columnconfigure(3, weight=1)

        self.title_entry = self._add_field_entry(
            metadata,
            0,
            "Title",
            "Project title...",
        )
        self.title_entry.bind("<KeyRelease>", self.update_preview)

        self.category = self._add_field_menu(
            metadata,
            1,
            "Category",
            self.pm.db.get_categories() or ["Misc"],
        )

        self.status = self._add_field_menu(
            metadata,
            2,
            "Status",
            ["In Progress", "Scheduled", "Completed"],
        )
        self.status.configure(command=self.on_status_changed)
        self.status.set("In Progress")

        templates = self.pm.get_templates()
        self.template = self._add_field_menu(
            metadata,
            3,
            "Template",
            templates or [""],
        )
        if templates:
            self.template.set(templates[0])

    def _add_field_entry(self, parent, column, label, placeholder):
        wrapper = ctk.CTkFrame(parent, fg_color="transparent")
        wrapper.grid(
            row=0,
            column=column,
            sticky="ew",
            padx=(0, 8) if column < 3 else 0,
        )

        ctk.CTkLabel(
            wrapper,
            text=label,
            font=("Segoe UI", 11, "bold"),
            text_color=("#475467", "#AEB4BF"),
            anchor="w",
        ).pack(fill="x", pady=(0, 4))

        entry = ctk.CTkEntry(
            wrapper,
            height=34,
            corner_radius=6,
            placeholder_text=placeholder,
        )
        entry.pack(fill="x")
        return entry

    def _add_field_menu(self, parent, column, label, values):
        wrapper = ctk.CTkFrame(parent, fg_color="transparent")
        wrapper.grid(
            row=0,
            column=column,
            sticky="ew",
            padx=(0, 8) if column < 3 else 0,
        )

        ctk.CTkLabel(
            wrapper,
            text=label,
            font=("Segoe UI", 11, "bold"),
            text_color=("#475467", "#AEB4BF"),
            anchor="w",
        ).pack(fill="x", pady=(0, 4))

        menu = ctk.CTkOptionMenu(
            wrapper,
            values=values,
            height=34,
            corner_radius=6,
            command=lambda _: self.update_preview(),
        )
        menu.pack(fill="x")
        return menu

    def _build_content_columns(self):
        columns = ctk.CTkFrame(self.form, fg_color="transparent")
        columns.pack(fill="both", expand=True)

        left = self._content_card(columns)
        left.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0, 6),
            anchor="n",
        )

        right = self._content_card(columns)
        right.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(6, 0),
            anchor="n",
        )

        self.add_textbox(left, "Paste from ChatGPT", "import_box", 140)

        ctk.CTkButton(
            left,
            text="Import ChatGPT Text",
            height=34,
            corner_radius=6,
            font=EMOJI_BUTTON_FONT,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            hover_color=("#F2F4F7", "#242831"),
            command=self.import_chatgpt_text,
        ).pack(fill="x", padx=12, pady=(4, 12))

        self.add_textbox(left, "Script", "script", 225)

        self.add_textbox(right, "Description", "description", 108)
        self.add_textbox(right, "Pinned Comment", "pinned_comment", 96)
        self.add_textbox(right, "Notes / Tags / Sources", "notes", 145)

        self.open_after = ctk.CTkCheckBox(
            right,
            text="Open project after creating",
            font=("Segoe UI", 12),
        )
        self.open_after.select()
        self.open_after.pack(anchor="w", padx=12, pady=(12, 14))

    def _content_card(self, parent):
        return ctk.CTkFrame(
            parent,
            corner_radius=7,
            fg_color=("#F9FAFB", "#1D2027"),
            border_width=1,
            border_color=("#EAECF0", "#303540"),
        )

    def add_textbox(self, parent, label, attr, height):
        ctk.CTkLabel(
            parent,
            text=label,
            font=("Segoe UI", 12, "bold"),
            anchor="w",
        ).pack(fill="x", padx=12, pady=(12, 5))

        box = ctk.CTkTextbox(
            parent,
            height=height,
            font=EMOJI_FONT,
            corner_radius=6,
            border_width=1,
            border_color=("#D0D5DD", "#343A46"),
        )
        box.pack(fill="x", padx=12, pady=(0, 4))
        setattr(self, attr, box)

    def build_right_panel(self):
        header = ctk.CTkFrame(self.right_panel, fg_color="transparent")
        header.pack(fill="x", padx=14, pady=(14, 8))

        ctk.CTkLabel(
            header,
            text="Project preview",
            font=("Segoe UI", 15, "bold"),
            anchor="w",
        ).pack(fill="x")

        ctk.CTkLabel(
            header,
            text="What will be created",
            font=("Segoe UI", 11),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        ).pack(fill="x", pady=(2, 0))

        self.preview = ctk.CTkTextbox(
            self.right_panel,
            width=250,
            corner_radius=6,
            border_width=1,
            border_color=("#EAECF0", "#303540"),
            fg_color=("#F9FAFB", "#1D2027"),
            font=("Segoe UI Emoji", 12),
        )
        self.preview.pack(
            fill="both",
            expand=True,
            padx=14,
            pady=(0, 14),
        )
        self.preview.configure(state="disabled")

    def update_preview(self, *_):
        title = self.title_entry.get().strip() or "New Project"
        schedule_text = (
            f"\nScheduled\n{self.scheduled_for}\n"
            if self.status.get() == "Scheduled" and self.scheduled_for
            else ""
        )

        preview = (
            f"📁  {title}\n\n"
            f"Category\n{self.category.get()}\n\n"
            f"Status\n{self.status.get()}\n"
            f"{schedule_text}\n"
            f"Template\n{self.template.get()}\n\n"
            "──────────────\n"
            "DATABASE\n\n"
            "✓ Script\n"
            "✓ Description\n"
            "✓ Pinned Comment\n"
            "✓ Notes\n\n"
            "──────────────\n"
            "PROJECT FOLDERS\n\n"
            "✓ Assets\n"
            "✓ Images\n"
            "✓ Videos\n"
            "✓ Music\n"
            "✓ Voice\n"
            "✓ Export"
        )

        self.preview.configure(state="normal")
        self.preview.delete("1.0", "end")
        self.preview.insert("1.0", preview)
        self.preview.configure(state="disabled")

    def on_status_changed(self, value):
        if value != "Scheduled":
            self._last_non_scheduled_status = value
            self.scheduled_for = ""
            self.status_label.configure(text="")
            self.update_preview()
            return

        dialog = ScheduleDialog(self)
        self.wait_window(dialog)

        if dialog.result is None:
            self.status.set(self._last_non_scheduled_status)
            self.scheduled_for = ""
            self.status_label.configure(text="Scheduling cancelled.")
            self.update_preview()
            return

        self.scheduled_for = dialog.result.strftime("%Y-%m-%d %H:%M")
        self.status_label.configure(text=f"Scheduled for {self.scheduled_for}.")
        self.update_preview()

    def _install_keyboard_scrolling(self):
        """Scroll the New Fact form with keyboard navigation keys."""
        toplevel = self.winfo_toplevel()
        bindings = (
            ("<Up>", lambda event: self._scroll_from_key(event, -3, "units")),
            ("<Down>", lambda event: self._scroll_from_key(event, 3, "units")),
            ("<Prior>", lambda event: self._scroll_from_key(event, -1, "pages")),
            ("<Next>", lambda event: self._scroll_from_key(event, 1, "pages")),
            ("<Home>", lambda event: self._scroll_to_from_key(event, 0.0)),
            ("<End>", lambda event: self._scroll_to_from_key(event, 1.0)),
        )
        for sequence, callback in bindings:
            funcid = toplevel.bind(sequence, callback, add="+")
            if funcid:
                self._keyboard_bindings.append((sequence, funcid))
        self.bind("<Destroy>", self._remove_keyboard_scrolling, add="+")

    @staticmethod
    def _focus_is_multiline_text(event):
        widget = getattr(event, "widget", None)
        return widget is not None and widget.winfo_class() == "Text"

    def _scroll_from_key(self, event, amount, mode):
        # Keep Up/Down available for moving the caret inside multiline editors.
        if mode == "units" and self._focus_is_multiline_text(event):
            return None
        canvas = getattr(self.form, "_parent_canvas", None)
        if canvas is not None:
            canvas.yview_scroll(amount, mode)
        return "break"

    def _scroll_to_from_key(self, event, position):
        # Home/End keep their normal caret behaviour inside editable fields.
        widget = getattr(event, "widget", None)
        if widget is not None and widget.winfo_class() in {"Entry", "Text"}:
            return None
        canvas = getattr(self.form, "_parent_canvas", None)
        if canvas is not None:
            canvas.yview_moveto(position)
        return "break"

    def _remove_keyboard_scrolling(self, event=None):
        if event is not None and event.widget is not self:
            return
        if not self._keyboard_bindings:
            return
        toplevel = self.winfo_toplevel()
        for sequence, funcid in self._keyboard_bindings:
            try:
                toplevel.unbind(sequence, funcid)
            except Exception:
                pass
        self._keyboard_bindings.clear()

    def create_project(self):
        title = self.title_entry.get().strip()

        if not title:
            self.status_label.configure(text="Enter a project title before creating the fact.")
            self.title_entry.focus_set()
            messagebox.showerror("Missing Title", "Please enter a project title.", parent=self)
            return

        try:
            self.pm.get_projects_root()
        except Exception as exc:
            self.status_label.configure(text="Projects Folder needs to be configured first.")
            messagebox.showerror("Projects Folder", str(exc), parent=self)
            return

        desired_status = self.status.get()
        scheduled_for = self.scheduled_for if desired_status == "Scheduled" else ""
        if desired_status == "Scheduled" and not scheduled_for:
            self.status_label.configure(text="Choose a schedule before creating the fact.")
            return

        self.create_button.configure(state="disabled", text="Creating...")
        self.status_label.configure(text="Creating project...")
        self.update_idletasks()

        create_status = "In Progress" if desired_status == "Scheduled" else desired_status

        try:
            folder = self.pm.create_project(
                title,
                self.category.get(),
                create_status,
                self.script.get("1.0", "end").strip(),
                self.description.get("1.0", "end").strip(),
                self.pinned_comment.get("1.0", "end").strip(),
                self.notes.get("1.0", "end").strip(),
            )
        except Exception as exc:
            self.create_button.configure(state="normal", text="Create Fact")
            self.status_label.configure(text="Project was not created.")
            messagebox.showerror("Create Project", str(exc), parent=self)
            return

        project = self.pm.db.get_latest_project()
        schedule_warning = ""
        if desired_status == "Scheduled":
            if project is None:
                schedule_warning = "The project was created, but it could not be found to apply its schedule."
            else:
                try:
                    project = self.pm.change_project_status(
                        project_id=project["id"],
                        new_status="Scheduled",
                        scheduled_for=scheduled_for,
                    )
                    folder = self.pm.resolve_project_folder(project)
                except Exception as exc:
                    schedule_warning = (
                        "The project was created in In Progress, but scheduling failed.\n\n"
                        f"{exc}"
                    )
                    project = self.pm.db.get_project(project["id"])
                    if project is not None:
                        folder = self.pm.resolve_project_folder(project)

        template_warning = ""
        try:
            self.pm.apply_template(folder, self.template.get())
        except Exception as exc:
            template_warning = str(exc)

        warnings = [warning for warning in (schedule_warning, template_warning) if warning]
        if warnings:
            self.status_label.configure(text="Project created with a warning.")
            messagebox.showwarning(
                "Project Created",
                "The project was created, but part of the setup needs attention.\n\n"
                + "\n\n".join(warnings),
                parent=self,
            )
        else:
            self.status_label.configure(text="Project created successfully.")
            messagebox.showinfo("Success", "Project created successfully!", parent=self)

        if self.open_after.get():
            if project:
                self.app.show_edit_project(project["id"])
            else:
                self.app.show_projects()
        else:
            self.app.show_projects()

    def import_chatgpt_text(self):
        raw_text = self.import_box.get("1.0", "end").strip()

        if not raw_text:
            self.status_label.configure(text="Paste ChatGPT text before importing.")
            self.import_box.focus_set()
            messagebox.showerror("Import", "Please paste text from ChatGPT first.", parent=self)
            return

        data = ChatGPTImportParser.parse(raw_text)

        if data["title"]:
            self.title_entry.delete(0, "end")
            self.title_entry.insert(0, data["title"])

        if data["category"]:
            self.category.set(data["category"])

        if data["template"]:
            self.template.set(data["template"])

        imported_fields = 0
        for key in [
            "script",
            "description",
            "pinned_comment",
            "notes",
            "on_screen_text",
            "visual_plan",
        ]:
            if not hasattr(self, key):
                continue

            value = data[key]
            box = getattr(self, key)
            box.delete("1.0", "end")
            box.insert("1.0", value)
            if str(value or "").strip():
                imported_fields += 1

        self.update_preview()
        self.status_label.configure(
            text=f"ChatGPT content imported ({imported_fields} content field{'s' if imported_fields != 1 else ''})."
        )

        if not self.title_entry.get().strip():
            self.title_entry.focus_set()
