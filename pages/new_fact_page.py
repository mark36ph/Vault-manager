from tkinter import messagebox
import customtkinter as ctk
from common.chatgpt_import_parser import ChatGPTImportParser
from pages.base_page import BasePage
from services.voice.voice_service import VoiceService

class NewFactPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "New Fact")
        self.voice_service = VoiceService()
        self.app = app

        self.build()

    def build(self):

        # ==========================================
        # Main container
        # ==========================================

        main = ctk.CTkFrame(
            self.content,
            fg_color="transparent"
        )

        main.pack(
            fill="both",
            expand=True,
            padx=10,
            pady=10
        )

        # ==========================================
        # Left Panel
        # ==========================================

        self.left_panel = ctk.CTkFrame(
            main,
            corner_radius=12
        )

        self.left_panel.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0,10)
        )

        # ==========================================
        # Right Panel
        # ==========================================

        self.right_panel = ctk.CTkFrame(
            main,
            width=320,
            corner_radius=12
        )

        self.right_panel.pack(
            side="right",
            fill="y"
        )

        self.right_panel.pack_propagate(False)

        # ==========================================
        # Build Sections
        # ==========================================

        self.build_right_panel()
        self.build_left_panel()

    def build_left_panel(self):

        ctk.CTkLabel(
            self.left_panel,
            text="Project Details",
            font=("Segoe UI", 26, "bold")
        ).pack(
            anchor="w",
            padx=25,
            pady=(20, 20)
        )

        self.form = ctk.CTkScrollableFrame(
            self.left_panel,
            fg_color="transparent"
        )

        self.form.pack(
            fill="both",
            expand=True,
            padx=25
        )

        # ======================================
        # Title
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Project Title"
        ).pack(anchor="w")

        self.title_entry = ctk.CTkEntry(
            self.form,
            height=38,
            placeholder_text="Enter project title..."
        )

        self.title_entry.pack(
            fill="x",
            pady=(5, 20)
        )

        self.title_entry.bind(
            "<KeyRelease>",
            self.update_preview
        )

        # ======================================
        # Category
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Category"
        ).pack(anchor="w")

        categories = self.pm.db.get_categories()

        if not categories:
            categories = ["Misc"]

        self.category = ctk.CTkOptionMenu(
            self.form,
            values=categories,
            height=38,
            command=lambda _: self.update_preview()
        )

        self.category.pack(
            fill="x",
            pady=(5, 20)
        )

        # ======================================
        # Status
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Status"
        ).pack(anchor="w")

        self.status = ctk.CTkOptionMenu(
            self.form,
            values=[
                "In Progress",
                "Scheduled",
                "Completed"
            ],
            height=38,
            command=lambda _: self.update_preview()
        )

        self.status.set("In Progress")

        self.status.pack(
            fill="x",
            pady=(5, 20)
        )

        # ======================================
        # Template
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Project Template"
        ).pack(anchor="w")

        templates = self.pm.get_templates()

        self.template = ctk.CTkOptionMenu(
            self.form,
            values=templates,
            height=38,
            command=lambda _: self.update_preview()
        )

        if templates:
            self.template.set(templates[0])
    
        self.template.pack(
            fill="x",
            pady=(5, 20)
        )

        # ======================================
        # Voice
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Narration Voice"
        ).pack(anchor="w")

        installed_voices = self.voice_service.get_installed_voices()

        self.voice_lookup = {
            voice.display_name: voice
            for voice in installed_voices
        }

        voice_names = list(self.voice_lookup.keys())

        self.voice_dropdown = ctk.CTkOptionMenu(
            self.form,
            values=voice_names if voice_names else ["No installed voices"],
            height=38
        )

        self.voice_dropdown.pack(
            fill="x",
            pady=(5, 20)
        )

        default_voice = self.voice_service.get_default_voice()

        if default_voice:
            self.voice_dropdown.set(default_voice.display_name)
        elif voice_names:
            self.voice_dropdown.set(voice_names[0])
            
        # ======================================
        # ChatGPT Import
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="Paste From ChatGPT",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", pady=(10, 5))

        self.import_box = ctk.CTkTextbox(
            self.form,
            height=160
        )

        self.import_box.pack(
            fill="x",
            pady=(5, 10)
        )

        ctk.CTkButton(
            self.form,
            text="Import ChatGPT Text",
            height=38,
            command=self.import_chatgpt_text
        ).pack(
            fill="x",
            pady=(0, 20)
        )

        for label, attr, height in [
            ("Script", "script", 180),
            ("Description", "description", 100),
            ("Pinned Comment", "pinned_comment", 100),
            ("Notes / Tags", "notes", 100)
        ]:
            ctk.CTkLabel(
                self.form,
                text=label,
                font=("Segoe UI", 16, "bold")
            ).pack(anchor="w", pady=(10, 5))

            box = ctk.CTkTextbox(
                self.form,
                height=height
            )

            box.pack(
                fill="x",
                pady=(0, 10)
            )

            setattr(self, attr, box)
            
        # ======================================
        # Options
        # ======================================

        self.open_after = ctk.CTkCheckBox(
            self.form,
            text="Open project after creating"
        )

        self.open_after.select()

        self.open_after.pack(
            anchor="w",
            pady=(5, 25)
        )

        # ======================================
        # Create Button
        # ======================================

        ctk.CTkButton(
            self.form,
            text="Create Project",
            height=42,
            command=self.create_project
        ).pack(
            fill="x",
            pady=(10, 10)
        )
        self.update_preview()

    def build_right_panel(self):

        ctk.CTkLabel(
            self.right_panel,
            text="Project Preview",
            font=("Segoe UI",24,"bold")
        ).pack(
            pady=(20,15)
        )

        self.preview = ctk.CTkTextbox(
            self.right_panel,
            width=280
        )

        self.preview.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0,20)
        )

        self.preview.insert(
            "1.0",
            """📁 New Project

    Category:
    -

    Status:
    -

    Template:
    -

    --------------------------

    Files

    ✔ Script.txt

    ✔ Description.txt

    ✔ Notes.txt

    ✔ project.json

    --------------------------

    Folders

    ✔ Assets

    ✔ Images

    ✔ Videos

    ✔ Music

    ✔ Export
    """
        )

        self.preview.configure(
            state="disabled"
        )

    def update_preview(self, *_):

        title = self.title_entry.get().strip()

        if not title:
            title = "New Project"

        preview = f"""📁 {title}

    Category:
    {self.category.get()}

    Status:
    {self.status.get()}

    Template:
    {self.template.get()}

    --------------------------

    Files

    ✔ Script.txt

    ✔ Description.txt

    ✔ Notes.txt

    ✔ project.json

    --------------------------

    Folders

    ✔ Assets

    ✔ Images

    ✔ Videos

    ✔ Music

    ✔ Export
    """

        self.preview.configure(state="normal")
        self.preview.delete("1.0", "end")
        self.preview.insert("1.0", preview)
        self.preview.configure(state="disabled")

    def create_project(self):

        from tkinter import messagebox

        title = self.title_entry.get().strip()

        if not title:

            messagebox.showerror(
                "Missing Title",
                "Please enter a project title."
            )

            return

        try:

            folder = self.pm.create_project(
                title,
                self.category.get(),
                self.status.get(),
                self.script.get("1.0", "end").strip(),
                self.description.get("1.0", "end").strip(),
                self.pinned_comment.get("1.0", "end").strip(),
                self.notes.get("1.0", "end").strip()
            )

            self.pm.apply_template(
                folder,
                self.template.get()
            )

            script_text = self.script.get(
                "1.0",
                "end"
            ).strip()

            selected_voice_name = self.voice_dropdown.get()
            selected_voice = self.voice_lookup.get(selected_voice_name)

            if script_text and selected_voice:

                output_file = folder / "Voice" / "narration.wav"

                self.voice_service.generate_voice(
                    selected_voice.id,
                    script_text,
                    output_file
                )
                
            messagebox.showinfo(
                "Success",
                "Project created successfully!"
            )

            self.app.show_projects()

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )

    def import_chatgpt_text(self):

        raw_text = self.import_box.get(
            "1.0",
            "end"
        ).strip()

        if not raw_text:

            messagebox.showerror(
                "Import",
                "Please paste text from ChatGPT first."
            )

            return

        data = ChatGPTImportParser.parse(raw_text)

        if data["title"]:

            self.title_entry.delete(0, "end")
            self.title_entry.insert(0, data["title"])
        if data["category"]:
            self.category.set(data["category"])

        if data["template"]:
            self.template.set(data["template"])

        for key in [
            "script",
            "description",
            "pinned_comment",
            "notes"
        ]:

            box = getattr(self, key)

            box.delete("1.0", "end")
            box.insert("1.0", data[key])

        self.update_preview()