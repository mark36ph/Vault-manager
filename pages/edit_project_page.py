from tkinter import messagebox
import os
import customtkinter as ctk
from pages.base_page import BasePage
import shutil
from pathlib import Path
from services.voice.voice_service import VoiceService
from services.voice.piper_engine import PiperEngine
from common.settings_manager import SettingsManager


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

    def build(self):
        self.form = ctk.CTkScrollableFrame(self.content)
        self.form.pack(fill="both", expand=True)

        ctk.CTkLabel(self.form,text="Project Title").pack(anchor="w",padx=15,pady=(15,5))
        self.title_entry=ctk.CTkEntry(self.form,width=500)
        self.title_entry.pack(anchor="w",padx=15)

        ctk.CTkLabel(self.form,text="Category").pack(anchor="w",padx=15,pady=(15,5))
        self.category=ctk.CTkOptionMenu(self.form,values=self.pm.db.get_categories() or ["Misc"],width=220)
        self.category.pack(anchor="w",padx=15)

        ctk.CTkLabel(self.form,text="Status").pack(anchor="w",padx=15,pady=(15,5))
        self.status=ctk.CTkOptionMenu(self.form,values=["In Progress","Completed","Scheduled"],width=220)
        self.status.pack(anchor="w",padx=15)

        btns=ctk.CTkFrame(self.form,fg_color="transparent")
        btns.pack(anchor="w",padx=15,pady=20)
        ctk.CTkButton(btns,text="📂 Open Folder",command=self.open_folder).pack(side="left",padx=5)
        ctk.CTkButton(btns,text="💾 Save",command=self.save_project).pack(side="left",padx=5)
        ctk.CTkButton(btns,text="← Back",command=self.app.show_projects).pack(side="left",padx=5)

        for label,attr,h in [("Script","script",250),("Description","description",120),("Pinned Comment","pinned_comment",120),("Notes","notes",150)]:
            ctk.CTkLabel(self.form,text=label,font=("Segoe UI",18,"bold")).pack(anchor="w",padx=15,pady=(20,5))
            box=ctk.CTkTextbox(self.form,width=900,height=h)
            box.pack(fill="x",padx=15)
            setattr(self,attr,box)

        # =====================================
        # Voice Generation
        # =====================================

        ctk.CTkLabel(
            self.form,
            text="Voice Generation",
            font=("Segoe UI", 18, "bold")
        ).pack(
            anchor="w",
            padx=15,
            pady=(25, 10)
        )

        voice_frame = ctk.CTkFrame(self.form)

        voice_frame.pack(
            fill="x",
            padx=15,
            pady=(0, 20)
        )

        default_voice = self.voice_service.get_default_voice()

        voice_name = "None"

        if default_voice:
            voice_name = default_voice.display_name

        ctk.CTkLabel(
            voice_frame,
            text=f"Default Voice: {voice_name}"
        ).pack(
            anchor="w",
            padx=15,
            pady=(15, 5)
        )

        ctk.CTkButton(
            voice_frame,
            text="🎙 Generate Narration",
            height=40,
            command=self.generate_narration
        ).pack(
            padx=15,
            pady=15,
            anchor="w"
        )

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
        self.notes.insert("1.0",self.project["notes"] or "")

    def save_project(self):

        try:

            # Current folder

            old_folder = None

            settings = self.settings.section("general")
            root = Path(settings["projects_folder"])

            # Look for the project in every status folder
            for status in ["In Progress", "Scheduled", "Completed"]:
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

            # Save changes to the database
            self.pm.db.update_project(
                self.project_id,
                self.title_entry.get().strip(),
                self.category.get(),
                self.status.get(),
                str(new_folder),   # <-- add this back
                self.script.get("1.0", "end").strip(),
                self.description.get("1.0", "end").strip(),
                self.pinned_comment.get("1.0", "end").strip(),
                self.notes.get("1.0", "end").strip()
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