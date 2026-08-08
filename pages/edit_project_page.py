from tkinter import messagebox
import os
import shutil
from datetime import datetime

import customtkinter as ctk

from pages.base_page import BasePage
from common.ui_fonts import EMOJI_FONT, EMOJI_FONT_BOLD


class EditProjectPage(BasePage):
    """Edit project content, publishing metadata, and high-level workflow state.

    Production itself is handled from the Production page. This page intentionally
    avoids duplicate voice, asset, caption, or Resolve-generation controls.
    """

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
        self.form = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent",
        )
        self.form.pack(
            fill="both",
            expand=True,
            padx=15,
            pady=15,
        )

        # ==============================
        # Project details
        # ==============================
        details = ctk.CTkFrame(self.form)
        details.pack(fill="x", padx=10, pady=(0, 15))

        ctk.CTkLabel(
            details,
            text="Project Details",
            font=("Segoe UI", 22, "bold"),
        ).pack(anchor="w", padx=20, pady=(15, 10))

        ctk.CTkLabel(
            details,
            text=(
                "Edit the project content and publishing information here. "
                "Run assets, voice, captions, and the Resolve package from the Production page."
            ),
            justify="left",
            wraplength=980,
            text_color="gray",
        ).pack(anchor="w", padx=20, pady=(0, 12))

        row = ctk.CTkFrame(details, fg_color="transparent")
        row.pack(fill="x", padx=20, pady=(0, 15))

        self.title_entry = ctk.CTkEntry(
            row,
            width=420,
            placeholder_text="Project title...",
        )
        self.title_entry.pack(side="left", padx=(0, 10))

        self.category = ctk.CTkOptionMenu(
            row,
            values=self.pm.db.get_categories() or ["Misc"],
            width=180,
        )
        self.category.pack(side="left", padx=10)

        self.status = ctk.CTkOptionMenu(
            row,
            values=[
                "In Progress",
                "Scheduled",
                "Completed",
                "Published",
            ],
            width=180,
            command=self.on_status_changed,
        )
        self.status.pack(side="left", padx=10)

        ctk.CTkButton(
            row,
            text="💾 Save",
            command=self.save_project,
        ).pack(side="right", padx=5)

        ctk.CTkButton(
            row,
            text="📂 Open Folder",
            command=self.open_folder,
        ).pack(side="right", padx=5)

        ctk.CTkButton(
            row,
            text="← Back",
            command=self.app.show_projects,
        ).pack(side="right", padx=5)

        # ==============================
        # Main two-column layout
        # ==============================
        columns = ctk.CTkFrame(self.form, fg_color="transparent")
        columns.pack(fill="both", expand=True, padx=10)

        left = ctk.CTkFrame(columns)
        left.pack(side="left", fill="both", expand=True, padx=(0, 10))

        right = ctk.CTkFrame(columns, width=400)
        right.pack(side="right", fill="y", padx=(10, 0))
        right.pack_propagate(False)

        # Left: content used by the current production pipeline.
        ctk.CTkLabel(
            left,
            text="Production Content",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(15, 0))

        self.add_textbox(left, "Script", "script", 320)
        self.add_textbox(left, "On-Screen Text", "on_screen_text", 260)

        # Right: publishing metadata.
        ctk.CTkLabel(
            right,
            text="Publishing",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(15, 0))

        self.add_textbox(right, "Description", "description", 130)
        self.add_textbox(right, "Pinned Comment", "pinned_comment", 110)
        self.add_textbox(right, "Tags", "tags", 90)
        self.add_textbox(right, "Sources", "sources", 120)
        self.add_textbox(right, "Thumbnail Prompt", "thumbnail_prompt", 110)
        self.add_textbox(right, "Notes", "notes", 130)

        duration_row = ctk.CTkFrame(right, fg_color="transparent")
        duration_row.pack(fill="x", padx=15, pady=(10, 5))

        ctk.CTkLabel(
            duration_row,
            text="Narration Duration (seconds)",
            font=EMOJI_FONT_BOLD,
        ).pack(anchor="w")

        self.narration_duration = ctk.CTkEntry(
            duration_row,
            placeholder_text="e.g. 44.4",
        )
        self.narration_duration.pack(fill="x", pady=(5, 0))

        # ==============================
        # Current production milestones
        # ==============================
        pipeline_frame = ctk.CTkFrame(right)
        pipeline_frame.pack(fill="x", padx=15, pady=(15, 15))

        ctk.CTkLabel(
            pipeline_frame,
            text="Production Status",
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=15, pady=(12, 5))

        ctk.CTkLabel(
            pipeline_frame,
            text=(
                "Milestones only. Production tasks are run from the Production page."
            ),
            justify="left",
            wraplength=330,
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

        ctk.CTkFrame(
            pipeline_frame,
            height=8,
            fg_color="transparent",
        ).pack()

    def add_textbox(self, parent, label, attr, height):
        ctk.CTkLabel(
            parent,
            text=label,
            font=EMOJI_FONT_BOLD,
        ).pack(anchor="w", padx=15, pady=(15, 5))

        box = ctk.CTkTextbox(
            parent,
            height=height,
            font=EMOJI_FONT,
        )
        box.pack(fill="x", padx=15, pady=(0, 5))
        setattr(self, attr, box)

    def open_folder(self):
        try:
            folder = self.pm.resolve_project_folder(self.project)

            if not folder.exists():
                raise FileNotFoundError(
                    f"Project folder could not be found:\n{folder}"
                )

            os.startfile(str(folder))

        except Exception as error:
            messagebox.showerror("Error", str(error))

    def load_project(self):
        self.title_entry.insert(0, self.project["title"] or "")
        self.category.set(self.project["category"] or "Misc")
        self.status.set(self.project["status"] or "In Progress")

        self.script.insert("1.0", self.project["script"] or "")
        self.on_screen_text.insert(
            "1.0",
            self.project["on_screen_text"] or "",
        )
        self.description.insert("1.0", self.project["description"] or "")
        self.pinned_comment.insert(
            "1.0",
            self.project["pinned_comment"] or "",
        )
        self.tags.insert("1.0", self.project["tags"] or "")
        self.sources.insert("1.0", self.project["sources"] or "")
        self.thumbnail_prompt.insert(
            "1.0",
            self.project["thumbnail_prompt"] or "",
        )
        self.notes.insert("1.0", self.project["notes"] or "")

        narration_duration = self.project["narration_duration"] or ""
        self.narration_duration.insert(0, str(narration_duration))

        for key, var in self.pipeline_vars.items():
            try:
                var.set(int(self.project[key] or 0))
            except Exception:
                var.set(0)

    def _pipeline_values_for_save(self):
        """Return current milestones while preserving legacy hidden flags."""
        pipeline = {
            key: var.get()
            for key, var in self.pipeline_vars.items()
        }

        # These fields remain in the database for backwards compatibility, but
        # are no longer presented as separate workflow steps on this page.
        for legacy_key in (
            "subtitles_complete",
            "capcut_complete",
        ):
            try:
                pipeline[legacy_key] = int(self.project[legacy_key] or 0)
            except Exception:
                pipeline[legacy_key] = 0

        return pipeline

    def save_project(self):
        try:
            old_folder = self.pm.resolve_project_folder(self.project)

            if not old_folder.exists():
                raise FileNotFoundError(
                    f"Project folder could not be found:\n{old_folder}"
                )

            new_title = self.title_entry.get().strip()
            if not new_title:
                raise ValueError("Project title cannot be empty.")

            new_status = self.status.get()
            new_project = {
                "title": new_title,
                "status": new_status,
            }
            new_folder = self.pm.get_project_folder(new_project)

            old_resolved = old_folder.resolve()
            new_resolved = new_folder.resolve()

            if old_resolved != new_resolved:
                new_folder.parent.mkdir(parents=True, exist_ok=True)

                if new_folder.exists():
                    raise FileExistsError(
                        "A project folder already exists at the destination:\n"
                        f"{new_folder}"
                    )

                shutil.move(str(old_folder), str(new_folder))

            # Keep older planning fields unchanged. They remain in the database
            # for compatibility, but the current Resolve production workflow no
            # longer requires editing them on this page.
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
                thumbnail_prompt=self.thumbnail_prompt.get(
                    "1.0",
                    "end",
                ).strip(),
                tags=self.tags.get("1.0", "end").strip(),
                sources=self.sources.get("1.0", "end").strip(),
                subtitle_text=self.project["subtitle_text"] or "",
                narration_duration=(
                    self.narration_duration.get().strip() or 0
                ),
                pipeline=self._pipeline_values_for_save(),
            )

            self.project = self.pm.db.get_project(self.project_id)

            if new_status == "Scheduled":
                self.pm.db.update_project_schedule(
                    self.project["id"],
                    self.scheduled_for,
                )
            else:
                self.pm.db.update_project_schedule(
                    self.project["id"],
                    "",
                )

            messagebox.showinfo(
                "Saved",
                "Project updated successfully.",
            )
            self.app.show_projects()

        except Exception as error:
            messagebox.showerror("Error", str(error))

    def on_status_changed(self, value):
        if value != "Scheduled":
            return

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
            self.status.set(self.project["status"])
            return

        scheduled_value = self.parse_schedule_date(scheduled_text)

        if scheduled_value is None:
            messagebox.showerror(
                "Invalid Date",
                "Please enter the date like this:\n\n25/07/2026 18:00",
            )
            self.status.set(self.project["status"])
            return

        self.scheduled_for = scheduled_value

    @staticmethod
    def parse_schedule_date(value):
        value = str(value or "").strip()

        try:
            date = datetime.strptime(value, "%d/%m/%Y %H:%M")
            return date.strftime("%Y-%m-%d %H:%M")
        except Exception:
            return None
