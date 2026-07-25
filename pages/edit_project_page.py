from tkinter import messagebox
import os
import customtkinter as ctk
from pages.base_page import BasePage
import shutil
from pathlib import Path
from services.voice.voice_service import VoiceService
from services.voice.piper_engine import PiperEngine
from common.settings_manager import SettingsManager
from common.ui_fonts import EMOJI_FONT, EMOJI_FONT_BOLD, EMOJI_BUTTON_FONT
from datetime import datetime

class EditProjectPage(BasePage):
    def __init__(self, parent, pm, app, project_id):
        super().__init__(parent, pm, "Edit Project")
        self.app = app
        self.project_id = project_id
        self.project = self.pm.db.get_project(project_id)
        self.settings = SettingsManager()
        self.voice_service = VoiceService()
        self.piper = PiperEngine()
        if not self.project:
            messagebox.showerror("Error","Project not found.")
            self.app.show_projects()
            return
        self.build()
        self.load_project()
        try:

            self.scheduled_for = self.project["scheduled_for"] or ""

        except Exception:

            self.scheduled_for = ""

    def build(self):

        self.form = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent"
        )

        self.form.pack(
            fill="both",
            expand=True,
            padx=15,
            pady=15
        )

        # ==============================
        # Top Details Card
        # ==============================

        details = ctk.CTkFrame(self.form)
        details.pack(fill="x", padx=10, pady=(0, 15))

        ctk.CTkLabel(
            details,
            text="Project Details",
            font=("Segoe UI", 22, "bold")
        ).pack(anchor="w", padx=20, pady=(15, 10))

        row = ctk.CTkFrame(details, fg_color="transparent")
        row.pack(fill="x", padx=20, pady=(0, 15))

        self.title_entry = ctk.CTkEntry(
            row,
            width=420,
            placeholder_text="Project title..."
        )
        self.title_entry.pack(side="left", padx=(0, 10))

        self.category = ctk.CTkOptionMenu(
            row,
            values=self.pm.db.get_categories() or ["Misc"],
            width=180
        )
        self.category.pack(side="left", padx=10)

        self.status = ctk.CTkOptionMenu(
            row,
            values=[
                "In Progress",
                "Scheduled",
                "Completed",
                "Published"
            ],
            width=180,
            command=self.on_status_changed
        )

        self.status.pack(side="left", padx=10)

        ctk.CTkButton(row, text="💾 Save", command=self.save_project).pack(side="right", padx=5)
        ctk.CTkButton(row, text="📂 Open Folder", command=self.open_folder).pack(side="right", padx=5)
        ctk.CTkButton(row, text="← Back", command=self.app.show_projects).pack(side="right", padx=5)

        # ==============================
        # Main Two Column Layout
        # ==============================

        columns = ctk.CTkFrame(self.form, fg_color="transparent")
        columns.pack(fill="both", expand=True, padx=10)

        left = ctk.CTkFrame(columns)
        left.pack(side="left", fill="both", expand=True, padx=(0, 10))

        right = ctk.CTkFrame(columns, width=380)
        right.pack(side="right", fill="y", padx=(10, 0))
        right.pack_propagate(False)

        # Left column: production writing
        self.add_textbox(left, "Script", "script", 260)
        self.add_textbox(left, "On-Screen Text", "on_screen_text", 190)
        self.add_textbox(left, "Visual Plan", "visual_plan", 190)
        self.add_textbox(left, "Search Terms", "search_terms", 120)
        self.add_textbox(left, "B-Roll Plan", "broll_plan", 160)
        self.add_textbox(left, "Subtitle / SRT Content", "subtitle_text", 160)

        # Right column: publishing metadata
        self.add_textbox(right, "Description", "description", 130)
        self.add_textbox(right, "Pinned Comment", "pinned_comment", 120)
        self.add_textbox(right, "Tags", "tags", 100)
        self.add_textbox(right, "Sources", "sources", 120)
        self.add_textbox(right, "Thumbnail Prompt", "thumbnail_prompt", 120)
        self.add_textbox(right, "Notes", "notes", 140)

        duration_row = ctk.CTkFrame(right, fg_color="transparent")
        duration_row.pack(fill="x", padx=15, pady=(10, 5))
        ctk.CTkLabel(duration_row, text="Narration Duration (seconds)", font=EMOJI_FONT_BOLD).pack(anchor="w")
        self.narration_duration = ctk.CTkEntry(duration_row, placeholder_text="e.g. 43")
        self.narration_duration.pack(fill="x", pady=(5, 0))

        pipeline_frame = ctk.CTkFrame(right)
        pipeline_frame.pack(fill="x", padx=15, pady=(15, 5))
        ctk.CTkLabel(pipeline_frame, text="Production Pipeline", font=("Segoe UI", 18, "bold")).pack(anchor="w", padx=15, pady=(12, 8))
        self.pipeline_vars = {}
        stages = [
            ("research_complete", "Research"),
            ("script_complete", "Script"),
            ("voice_complete", "Voice"),
            ("subtitles_complete", "Subtitles"),
            ("broll_complete", "B-Roll"),
            ("graphics_complete", "Graphics"),
            ("capcut_complete", "CapCut"),
            ("export_complete", "Export"),
            ("upload_complete", "Upload")
        ]
        for key, label in stages:
            var = ctk.IntVar(value=0)
            self.pipeline_vars[key] = var
            ctk.CTkCheckBox(pipeline_frame, text=label, variable=var).pack(anchor="w", padx=15, pady=4)
        ctk.CTkFrame(pipeline_frame, height=8, fg_color="transparent").pack()

        # ==============================
        # AI Assistant Card
        # ==============================

        ai_frame = ctk.CTkFrame(right)
        ai_frame.pack(
            fill="x",
            padx=15,
            pady=(20, 15)
        )

        ctk.CTkLabel(
            ai_frame,
            text="🤖 AI Assistant",
            font=("Segoe UI", 18, "bold")
        ).pack(
            anchor="w",
            padx=15,
            pady=(15, 5)
        )

        ctk.CTkLabel(
            ai_frame,
            text="Generate helper prompts for ChatGPT using your script.",
            justify="left",
            wraplength=320,
            text_color="gray"
        ).pack(
            anchor="w",
            padx=15,
            pady=(0, 15)
        )

        ctk.CTkButton(
            ai_frame,
            text="📝 Prompt: On-Screen Text",
            height=38,
            command=self.create_on_screen_text_prompt
        ).pack(
            fill="x",
            padx=15,
            pady=(0, 8)
        )

        ctk.CTkButton(
            ai_frame,
            text="🎬 Prompt: Visual Plan",
            height=38,
            command=self.create_visual_plan_prompt
        ).pack(
            fill="x",
            padx=15,
            pady=(0, 15)
        )

        # ==============================
        # Voice Generation Card
        # ==============================

        voice_frame = ctk.CTkFrame(right)
        voice_frame.pack(fill="x", padx=15, pady=(20, 15))

        ctk.CTkLabel(
            voice_frame,
            text="Voice Generation",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", padx=15, pady=(15, 10))

        default_voice = self.voice_service.get_default_voice()
        voice_name = default_voice.display_name if default_voice else "None"

        ctk.CTkLabel(
            voice_frame,
            text=f"Default Voice:\n{voice_name}",
            justify="left"
        ).pack(anchor="w", padx=15, pady=(0, 10))

        ctk.CTkLabel(
            voice_frame,
            text="Voice generation is being upgraded and will return in a future update.",
            justify="left",
            wraplength=320,
            text_color="gray"
        ).pack(
            anchor="w",
            padx=15,
            pady=(0, 15)
        )

    def add_textbox(self, parent, label, attr, height):

        ctk.CTkLabel(
            parent,
            text=label,
            font=EMOJI_FONT_BOLD
        ).pack(anchor="w", padx=15, pady=(15, 5))

        box = ctk.CTkTextbox(
            parent,
            height=height,
            font=EMOJI_FONT
        )

        box.pack(
            fill="x",
            padx=15,
            pady=(0, 5)
        )

        setattr(self, attr, box)

    def open_folder(self):
        try:
            folder = self.pm.get_project_folder(self.project)
            os.startfile(folder)
        except Exception as e:
            messagebox.showerror("Error",str(e))

    def load_project(self):
        self.title_entry.insert(0,self.project["title"])
        self.category.set(self.project["category"])
        self.status.set(self.project["status"])
        self.script.insert("1.0",self.project["script"] or "")
        self.description.insert("1.0",self.project["description"] or "")
        self.pinned_comment.insert("1.0",self.project["pinned_comment"] or "")
        self.notes.insert("1.0", self.project["notes"] or "")
        self.on_screen_text.insert("1.0", self.project["on_screen_text"] or "")
        self.visual_plan.insert("1.0", self.project["visual_plan"] or "")
        self.search_terms.insert("1.0", self.project["search_terms"] or "")
        self.broll_plan.insert("1.0", self.project["broll_plan"] or "")
        self.subtitle_text.insert("1.0", self.project["subtitle_text"] or "")
        self.tags.insert("1.0", self.project["tags"] or "")
        self.sources.insert("1.0", self.project["sources"] or "")
        self.thumbnail_prompt.insert("1.0", self.project["thumbnail_prompt"] or "")
        self.narration_duration.insert(0, str(self.project["narration_duration"] or ""))
        for key, var in self.pipeline_vars.items():
            var.set(int(self.project[key] or 0))

    def save_project(self):

        try:

            # Current folder

            old_folder = None

            settings = self.settings.section("general")
            root = Path(settings["projects_folder"])

            # Look for the project in every status folder
            for status in ["In Progress", "Scheduled", "Completed", "Published"]:
                candidate = root / status / self.project["title"]
                if candidate.exists():
                    old_folder = candidate
                    break

            if old_folder is None:
                raise Exception("Project folder could not be found.")

            # New project details
            new_project = {
                "title": self.title_entry.get().strip(),
                "status": self.status.get()
            }

            # Calculate where the folder should be
            new_folder = self.pm.get_project_folder(new_project)
            # Move folder if needed
            if old_folder != new_folder:

                new_folder.parent.mkdir(parents=True, exist_ok=True)

                shutil.move(
                    str(old_folder),
                    str(new_folder)
                )

            # Save all text and workflow state to the database.
            self.pm.db.update_project(
                self.project_id,
                self.title_entry.get().strip(),
                self.category.get(),
                self.status.get(),
                str(new_folder),
                self.script.get("1.0", "end").strip(),
                self.description.get("1.0", "end").strip(),
                self.pinned_comment.get("1.0", "end").strip(),
                self.notes.get("1.0", "end").strip(),
                on_screen_text=self.on_screen_text.get("1.0", "end").strip(),
                visual_plan=self.visual_plan.get("1.0", "end").strip(),
                search_terms=self.search_terms.get("1.0", "end").strip(),
                broll_plan=self.broll_plan.get("1.0", "end").strip(),
                thumbnail_prompt=self.thumbnail_prompt.get("1.0", "end").strip(),
                tags=self.tags.get("1.0", "end").strip(),
                sources=self.sources.get("1.0", "end").strip(),
                subtitle_text=self.subtitle_text.get("1.0", "end").strip(),
                narration_duration=self.narration_duration.get().strip() or 0,
                pipeline={key: var.get() for key, var in self.pipeline_vars.items()}
            )
            if self.status.get() == "Scheduled":

                self.pm.db.update_project_schedule(
                    self.project["id"],
                    self.scheduled_for
                )

            else:

                self.pm.db.update_project_schedule(
                    self.project["id"],
                    ""
                )

            messagebox.showinfo(
                "Saved",
                "Project updated successfully."
            )

            self.app.show_projects()

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )
          
    def on_status_changed(self, value):

        if value != "Scheduled":
            return

        dialog = ctk.CTkInputDialog(
            text="When is this scheduled for?\n\nUse: DD/MM/YYYY HH:MM\nExample: 25/07/2026 18:00",
            title="Schedule Project"
        )

        scheduled_text = dialog.get_input()

        if not scheduled_text:

            self.status.set(
                self.project["status"]
            )

            return

        scheduled_value = self.parse_schedule_date(
            scheduled_text
        )

        if scheduled_value is None:

            messagebox.showerror(
                "Invalid Date",
                "Please enter the date like this:\n\n25/07/2026 18:00"
            )

            self.status.set(
                self.project["status"]
            )

            return

        self.scheduled_for = scheduled_value

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

    def get_script_text(self):

        return self.script.get(
            "1.0",
            "end"
        ).strip()

    def create_on_screen_text_prompt(self):

        script = self.get_script_text()

        if not script:

            messagebox.showerror(
                "AI Assistant",
                "Please add a script first."
            )

            return

        dialog = ctk.CTkInputDialog(
            text="How long is the narration?\n\nExamples: 52, 0:52, 1:04",
            title="Narration Length"
        )

        duration_text = dialog.get_input()

        if not duration_text:
            return

        duration_seconds = self.parse_duration_to_seconds(
            duration_text
        )

        if duration_seconds is None:

            messagebox.showerror(
                "Invalid Length",
                "Please enter the narration length like 52, 0:52, or 1:04."
            )

            return

        duration_label = self.format_seconds(
            duration_seconds
        )

        caption_count = max(
            10,
            round(duration_seconds / 4)
        )

        prompt = f"""Create on-screen text for a YouTube Shorts fact video.

Narration length:
{duration_label}

Target number of captions:
About {caption_count}

Important rules:
- Match the narration from 0:00 to {duration_label}.
- Use short captions only.
- Maximum 4 words per caption.
- Each caption should stay on screen for 3–5 seconds.
- Do not copy full narration sentences.
- Do not write long subtitles.
- Do not display two captions at the same time.
- Do not include visual instructions.
- Do not include explanations.
- Do not include markdown tables.
- Use emojis only if they make the caption stronger.
- The first caption must hook the viewer.
- The last caption should feel like a strong ending.

Return the answer in this exact format only:

0:00 - 0:04
Caption text

0:04 - 0:08
Caption text

0:08 - 0:12
Caption text

Continue until {duration_label}.

Style examples:
0:00 - 0:04
This Changed Everything

0:04 - 0:08
Nobody Expected This

0:08 - 0:12
It Gets Stranger

Script:
{script}
"""

        self.show_prompt_window(
            "On-Screen Text Prompt",
            prompt
        )

    def create_visual_plan_prompt(self):

        script = self.get_script_text()

        if not script:

            messagebox.showerror(
                "AI Assistant",
                "Please add a script first."
            )

            return

        prompt = f"""Using the following script, create a visual timeline for a YouTube Shorts video.

For each section include:

Time:
Narration:
Search:
Show:
Effects:

Requirements:
- Use clear timestamps.
- Suggest visuals that are easy to find in stock footage or AI image/video tools.
- Keep each visual section matched to the narration.
- Return ONLY the visual timeline.

Script:

{script}
"""

        self.show_prompt_window(
            "Visual Plan Prompt",
            prompt
        )

    def show_prompt_window(self, title, prompt):

        window = ctk.CTkToplevel(self)
        window.title(title)
        window.geometry("800x600")
        window.transient(self)
        window.grab_set()

        ctk.CTkLabel(
            window,
            text=title,
            font=("Segoe UI", 22, "bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(20, 10)
        )

        box = ctk.CTkTextbox(
            window,
            wrap="word"
        )

        box.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 15)
        )

        box.insert(
            "1.0",
            prompt
        )

        buttons = ctk.CTkFrame(
            window,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=20,
            pady=(0, 20)
        )

    def copy_prompt():

        window.clipboard_clear()

        window.clipboard_append(
            box.get("1.0", "end").strip()
        )

        messagebox.showinfo(
            "Copied",
            "Prompt copied to clipboard."
        )

        ctk.CTkButton(
            buttons,
            text="📋 Copy Prompt",
            height=38,
            command=copy_prompt
        ).pack(
            side="right",
            padx=5
        )

        ctk.CTkButton(
            buttons,
            text="Close",
            height=38,
            command=window.destroy
        ).pack(
            side="right",
            padx=5
        )
        
    def generate_narration(self):

        try:

            # Get the default voice
            voice = self.voice_service.get_default_voice()

            if voice is None:

                messagebox.showerror(
                    "Voice",
                    "No default voice has been selected.\n\n"
                    "Please configure a default voice first."
                )

                return

            # Get the script
            script = self.script.get(
                "1.0",
                "end"
            ).strip()

            if not script:

                messagebox.showerror(
                    "Voice",
                    "The script is empty."
                )

                return

            # Get the Voice folder
            voice_folder = self.pm.get_voice_folder(
                self.project
            )

            output_file = voice_folder / "narration.wav"

            # Generate narration
            self.voice_service.generate_voice(
                voice.id,
                script,
                output_file
            )

            messagebox.showinfo(
                "Voice",
                f"Narration created successfully!\n\n{output_file}"
            )

        except Exception as e:

            messagebox.showerror(
                "Voice Generation Failed",
                str(e)
            )

    def parse_duration_to_seconds(self, value):

        value = value.strip()

        try:

            if ":" in value:

                parts = value.split(":")

                if len(parts) != 2:
                    return None

                minutes = int(parts[0])
                seconds = int(parts[1])

                return minutes * 60 + seconds

            return int(value)

        except Exception:

            return None

    def format_seconds(self, total_seconds):

        minutes = total_seconds // 60
        seconds = total_seconds % 60

        return f"{minutes}:{seconds:02d}"