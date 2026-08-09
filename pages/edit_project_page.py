from tkinter import messagebox
import os
import shutil

import customtkinter as ctk

from pages.base_page import BasePage
from common.ui_fonts import EMOJI_FONT, EMOJI_FONT_BOLD
from widgets.project_card import ScheduleDialog


class EditProjectPage(BasePage):
    """Edit project content, publishing metadata, and high-level workflow state."""

    PIPELINE_STAGES = [
        ("research_complete", "Research"),
        ("script_complete", "Script"),
        ("broll_complete", "Assets"),
        ("voice_complete", "Voice"),
        ("graphics_complete", "Resolve Package"),
        ("export_complete", "Export"),
        ("upload_complete", "Upload / Published"),
    ]

    def __init__(self, parent, pm, app, project_id):
        super().__init__(parent, pm, "Edit Project")
        self.app = app
        self.project_id = project_id
        self.project = self.pm.db.get_project(project_id)
        self._saved_snapshot = None

        if not self.project:
            messagebox.showerror("Error", "Project not found.")
            self.app.show_projects()
            return

        try:
            self.scheduled_for = self.project["scheduled_for"] or ""
        except Exception:
            self.scheduled_for = ""

        self.build()
        self.load_project()

    def build(self):
        self.form = ctk.CTkScrollableFrame(self.content, fg_color="transparent")
        self.form.pack(fill="both", expand=True, padx=15, pady=15)

        details = ctk.CTkFrame(self.form)
        details.pack(fill="x", padx=10, pady=(0, 15))

        ctk.CTkLabel(
            details,
            text="Project Details",
            font=("Segoe UI", 22, "bold"),
        ).pack(anchor="w", padx=20, pady=(15, 6))

        ctk.CTkLabel(
            details,
            text=(
                "Edit the content and publishing information here. "
                "Run assets, voice, captions, and Resolve export from Production."
            ),
            justify="left",
            wraplength=1050,
            text_color="gray",
        ).pack(anchor="w", padx=20, pady=(0, 12))

        fields_row = ctk.CTkFrame(details, fg_color="transparent")
        fields_row.pack(fill="x", padx=20, pady=(0, 8))

        self.title_entry = ctk.CTkEntry(
            fields_row,
            placeholder_text="Project title...",
        )
        self.title_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))

        self.category = ctk.CTkOptionMenu(
            fields_row,
            values=self.pm.db.get_categories() or ["Misc"],
            width=190,
        )
        self.category.pack(side="left", padx=8)

        self.status = ctk.CTkOptionMenu(
            fields_row,
            values=["In Progress", "Scheduled", "Completed", "Published"],
            width=190,
            command=self.on_status_changed,
        )
        self.status.pack(side="left", padx=(8, 0))

        actions_row = ctk.CTkFrame(details, fg_color="transparent")
        actions_row.pack(fill="x", padx=20, pady=(0, 15))

        ctk.CTkButton(
            actions_row,
            text="← Back",
            command=self.go_back,
            width=100,
        ).pack(side="left")

        ctk.CTkButton(
            actions_row,
            text="🎬 Production",
            command=self.go_to_production,
            width=135,
        ).pack(side="right", padx=(5, 0))

        ctk.CTkButton(
            actions_row,
            text="📂 Folder",
            command=self.open_folder,
            width=110,
        ).pack(side="right", padx=5)

        self.save_button = ctk.CTkButton(
            actions_row,
            text="💾 Save",
            command=self.save_project,
            width=100,
        )
        self.save_button.pack(side="right", padx=5)

        columns = ctk.CTkFrame(self.form, fg_color="transparent")
        columns.pack(fill="both", expand=True, padx=10)

        left = ctk.CTkFrame(columns)
        left.pack(side="left", fill="both", expand=True, padx=(0, 10))

        right = ctk.CTkFrame(columns, width=410)
        right.pack(side="right", fill="y", padx=(10, 0))
        right.pack_propagate(False)

        ctk.CTkLabel(
            left,
            text="Production Content",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(15, 0))

        self.add_textbox(left, "Script", "script", 320, show_counter=True)
        self.add_textbox(
            left,
            "On-Screen Text",
            "on_screen_text",
            260,
            show_counter=True,
        )

        ctk.CTkLabel(
            right,
            text="Publishing",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(15, 0))

        self.add_textbox(right, "Description", "description", 125)
        self.add_textbox(right, "Pinned Comment", "pinned_comment", 105)
        self.add_textbox(right, "Tags", "tags", 85)
        self.add_textbox(right, "Sources", "sources", 110)
        self.add_textbox(right, "Thumbnail Prompt", "thumbnail_prompt", 105)
        self.add_textbox(right, "Notes", "notes", 120)

        info_frame = ctk.CTkFrame(right)
        info_frame.pack(fill="x", padx=15, pady=(15, 5))

        ctk.CTkLabel(
            info_frame,
            text="Project Info",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(12, 7))

        self.info_narration = ctk.CTkLabel(info_frame, text="Narration: —", anchor="w")
        self.info_narration.pack(fill="x", padx=15, pady=2)

        self.info_updated = ctk.CTkLabel(info_frame, text="Updated: —", anchor="w")
        self.info_updated.pack(fill="x", padx=15, pady=2)

        self.info_scheduled = ctk.CTkLabel(info_frame, text="Scheduled: —", anchor="w")
        self.info_scheduled.pack(fill="x", padx=15, pady=2)

        self.info_folder = ctk.CTkLabel(
            info_frame,
            text="Folder: —",
            anchor="w",
            justify="left",
            wraplength=340,
        )
        self.info_folder.pack(fill="x", padx=15, pady=(2, 12))

        pipeline_frame = ctk.CTkFrame(right)
        pipeline_frame.pack(fill="x", padx=15, pady=(15, 15))

        ctk.CTkLabel(
            pipeline_frame,
            text="Production Status",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(12, 5))

        ctk.CTkLabel(
            pipeline_frame,
            text="Milestones only. Production work is run from the Production page.",
            justify="left",
            wraplength=340,
            text_color="gray",
        ).pack(anchor="w", padx=15, pady=(0, 8))

        self.pipeline_vars = {}
        for key, label in self.PIPELINE_STAGES:
            var = ctk.IntVar(value=0)
            self.pipeline_vars[key] = var
            ctk.CTkCheckBox(
                pipeline_frame,
                text=label,
                variable=var,
            ).pack(anchor="w", padx=15, pady=4)

        ctk.CTkFrame(pipeline_frame, height=8, fg_color="transparent").pack()

    def add_textbox(self, parent, label, attr, height, show_counter=False):
        header = ctk.CTkFrame(parent, fg_color="transparent")
        header.pack(fill="x", padx=15, pady=(15, 5))

        ctk.CTkLabel(
            header,
            text=label,
            font=EMOJI_FONT_BOLD,
        ).pack(side="left")

        ctk.CTkButton(
            header,
            text="Copy",
            width=58,
            height=25,
            command=lambda name=attr: self.copy_textbox(name),
        ).pack(side="right")

        box = ctk.CTkTextbox(parent, height=height, font=EMOJI_FONT)
        box.pack(fill="x", padx=15, pady=(0, 3))
        setattr(self, attr, box)

        if show_counter:
            counter = ctk.CTkLabel(
                parent,
                text="Words: 0 | Characters: 0",
                text_color="gray",
                anchor="e",
            )
            counter.pack(fill="x", padx=15, pady=(0, 2))
            setattr(self, f"{attr}_counter", counter)
            box.bind(
                "<KeyRelease>",
                lambda _event, name=attr: self.update_text_counter(name),
            )

    def copy_textbox(self, attr):
        box = getattr(self, attr, None)
        if box is None:
            return
        text = box.get("1.0", "end").strip()
        self.clipboard_clear()
        self.clipboard_append(text)

    def update_text_counter(self, attr):
        box = getattr(self, attr, None)
        counter = getattr(self, f"{attr}_counter", None)
        if box is None or counter is None:
            return
        text = box.get("1.0", "end").strip()
        words = len(text.split()) if text else 0
        counter.configure(text=f"Words: {words} | Characters: {len(text)}")

    def open_folder(self):
        try:
            folder = self.pm.resolve_project_folder(self.project)
            if not folder.exists():
                raise FileNotFoundError(f"Project folder could not be found:\n{folder}")
            os.startfile(str(folder))
        except Exception as error:
            messagebox.showerror("Error", str(error), parent=self)

    def load_project(self):
        self.title_entry.insert(0, self.project["title"] or "")
        self.category.set(self.project["category"] or "Misc")
        self.status.set(self.project["status"] or "In Progress")

        self.script.insert("1.0", self.project["script"] or "")
        self.on_screen_text.insert("1.0", self.project["on_screen_text"] or "")
        self.description.insert("1.0", self.project["description"] or "")
        self.pinned_comment.insert("1.0", self.project["pinned_comment"] or "")
        self.tags.insert("1.0", self.project["tags"] or "")
        self.sources.insert("1.0", self.project["sources"] or "")
        self.thumbnail_prompt.insert("1.0", self.project["thumbnail_prompt"] or "")
        self.notes.insert("1.0", self.project["notes"] or "")

        for key, var in self.pipeline_vars.items():
            try:
                var.set(int(self.project[key] or 0))
            except Exception:
                var.set(0)

        self.update_text_counter("script")
        self.update_text_counter("on_screen_text")
        self.refresh_project_info()
        self._saved_snapshot = self._current_snapshot()

    def refresh_project_info(self):
        try:
            narration = float(self.project["narration_duration"] or 0)
        except Exception:
            narration = 0.0

        narration_text = f"{narration:g} sec" if narration else "Not recorded"
        self.info_narration.configure(text=f"Narration: {narration_text}")

        try:
            updated = self.project["updated"] or "—"
        except Exception:
            updated = "—"
        self.info_updated.configure(text=f"Updated: {updated}")

        scheduled = self.scheduled_for or "Not scheduled"
        self.info_scheduled.configure(text=f"Scheduled: {scheduled}")

        try:
            folder = self.pm.resolve_project_folder(self.project)
            folder_text = str(folder)
        except Exception:
            folder_text = str(self.project["folder"] or "—")
        self.info_folder.configure(text=f"Folder: {folder_text}")

    def _current_snapshot(self):
        return (
            self.title_entry.get().strip(),
            self.category.get(),
            self.status.get(),
            self.scheduled_for,
            self.script.get("1.0", "end").strip(),
            self.on_screen_text.get("1.0", "end").strip(),
            self.description.get("1.0", "end").strip(),
            self.pinned_comment.get("1.0", "end").strip(),
            self.tags.get("1.0", "end").strip(),
            self.sources.get("1.0", "end").strip(),
            self.thumbnail_prompt.get("1.0", "end").strip(),
            self.notes.get("1.0", "end").strip(),
            tuple((key, var.get()) for key, var in self.pipeline_vars.items()),
        )

    def has_unsaved_changes(self):
        return self._saved_snapshot is not None and self._current_snapshot() != self._saved_snapshot

    def confirm_leave_with_unsaved_changes(self):
        if not self.has_unsaved_changes():
            return True

        answer = messagebox.askyesnocancel(
            "Unsaved Changes",
            "This project has unsaved changes.\n\nSave them before leaving?",
            parent=self,
        )

        if answer is None:
            return False
        if answer:
            return self.save_project(show_message=False)
        return True

    def go_back(self):
        if self.confirm_leave_with_unsaved_changes():
            self.app.show_projects()

    def go_to_production(self):
        if not self.confirm_leave_with_unsaved_changes():
            return

        from pages.production_page import ProductionPage, project_choice

        self.app.load_page(ProductionPage, self.app)
        page = self.app.current_page

        for project in getattr(page, "projects", []):
            if int(project.get("id", -1)) != int(self.project_id):
                continue
            choice = project_choice(project)
            if choice in getattr(page, "project_lookup", {}):
                page.project_menu.set(choice)
                page._load_selected_project()
            break

    def _pipeline_values_for_save(self):
        pipeline = {key: var.get() for key, var in self.pipeline_vars.items()}

        for legacy_key in ("subtitles_complete", "capcut_complete"):
            try:
                pipeline[legacy_key] = int(self.project[legacy_key] or 0)
            except Exception:
                pipeline[legacy_key] = 0

        return pipeline

    def save_project(self, show_message=True):
        original_text = self.save_button.cget("text")
        self.save_button.configure(state="disabled", text="Saving...")
        self.update_idletasks()

        old_folder = None
        new_folder = None
        folder_moved = False
        database_updated = False

        try:
            old_folder = self.pm.resolve_project_folder(self.project)
            if not old_folder.exists():
                raise FileNotFoundError(f"Project folder could not be found:\n{old_folder}")

            new_title = self.title_entry.get().strip()
            if not new_title:
                raise ValueError("Project title cannot be empty.")

            new_status = self.status.get()
            if new_status == "Scheduled":
                self.scheduled_for = self.pm._validated_schedule(self.scheduled_for)
            else:
                self.scheduled_for = ""

            new_project = {"title": new_title, "status": new_status}
            new_folder = self.pm.get_project_folder(new_project)

            if old_folder.resolve() != new_folder.resolve():
                new_folder.parent.mkdir(parents=True, exist_ok=True)
                if new_folder.exists():
                    raise FileExistsError(
                        "A project folder already exists at the destination:\n"
                        f"{new_folder}"
                    )
                shutil.move(str(old_folder), str(new_folder))
                folder_moved = True

            try:
                narration_duration = self.project["narration_duration"] or 0
            except Exception:
                narration_duration = 0

            self.pm.db.update_project(
                self.project_id,
                new_title,
                self.category.get(),
                new_status,
                str(self.pm.get_relative_project_folder(new_folder)),
                self.script.get("1.0", "end").strip(),
                self.description.get("1.0", "end").strip(),
                self.pinned_comment.get("1.0", "end").strip(),
                self.notes.get("1.0", "end").strip(),
                on_screen_text=self.on_screen_text.get("1.0", "end").strip(),
                visual_plan=self.project["visual_plan"] or "",
                search_terms=self.project["search_terms"] or "",
                broll_plan=self.project["broll_plan"] or "",
                thumbnail_prompt=self.thumbnail_prompt.get("1.0", "end").strip(),
                tags=self.tags.get("1.0", "end").strip(),
                sources=self.sources.get("1.0", "end").strip(),
                subtitle_text=self.project["subtitle_text"] or "",
                narration_duration=narration_duration,
                pipeline=self._pipeline_values_for_save(),
            )
            database_updated = True

            self.pm.db.update_project_schedule(self.project_id, self.scheduled_for)

            self.project = self.pm.db.get_project(self.project_id)
            self.refresh_project_info()
            self._saved_snapshot = self._current_snapshot()

            if show_message:
                messagebox.showinfo("Saved", "Project updated successfully.", parent=self)
            return True

        except Exception as error:
            if (
                folder_moved
                and not database_updated
                and old_folder is not None
                and new_folder is not None
                and new_folder.exists()
                and not old_folder.exists()
            ):
                try:
                    old_folder.parent.mkdir(parents=True, exist_ok=True)
                    shutil.move(str(new_folder), str(old_folder))
                except Exception as rollback_error:
                    messagebox.showerror(
                        "Save Error",
                        (
                            f"{error}\n\n"
                            "The database was not updated and the project folder could not "
                            "be restored automatically.\n\n"
                            f"Folder recovery error: {rollback_error}"
                        ),
                        parent=self,
                    )
                    return False

            messagebox.showerror("Error", str(error), parent=self)
            return False
        finally:
            if self.winfo_exists():
                self.save_button.configure(state="normal", text=original_text)

    def on_status_changed(self, value):
        if value != "Scheduled":
            self.scheduled_for = ""
            self.info_scheduled.configure(text="Scheduled: Not scheduled")
            return

        dialog = ScheduleDialog(self)
        self.wait_window(dialog)

        if dialog.result is None:
            self.status.set(self.project["status"] or "In Progress")
            return

        self.scheduled_for = dialog.result.strftime("%Y-%m-%d %H:%M")
        self.info_scheduled.configure(text=f"Scheduled: {self.scheduled_for}")
