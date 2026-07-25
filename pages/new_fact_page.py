from tkinter import messagebox
import customtkinter as ctk
from common.chatgpt_import_parser import ChatGPTImportParser
from pages.base_page import BasePage
from common.ui_fonts import EMOJI_FONT, EMOJI_FONT_BOLD, EMOJI_BUTTON_FONT

class NewFactPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "New Fact")
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

        # ======================================
        # Fixed Header
        # ======================================

        header = ctk.CTkFrame(
            self.left_panel,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10)
        )

        ctk.CTkLabel(
            header,
            text="New Fact",
            font=("Segoe UI", 26, "bold")
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            header,
            text="☑ Create Fact",
            height=42,
            width=170,
            font=EMOJI_BUTTON_FONT,
            command=self.create_project
        ).pack(
            side="right"
        )

        # ======================================
        # Scrollable Form
        # ======================================

        self.form = ctk.CTkScrollableFrame(
            self.left_panel,
            fg_color="transparent"
        )

        self.form.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 20)
        )

        # ======================================
        # Details
        # ======================================

        ctk.CTkLabel(
            self.form,
            text="New Fact Details",
            font=("Segoe UI", 22, "bold")
        ).pack(
            anchor="w",
            padx=15,
            pady=(10, 10)
        )

        details_row = ctk.CTkFrame(
            self.form,
            fg_color="transparent"
        )

        details_row.pack(
            fill="x",
            padx=15,
            pady=(0, 15)
        )

        self.title_entry = ctk.CTkEntry(
            details_row,
            width=420,
            placeholder_text="Project title..."
        )

        self.title_entry.pack(
            side="left",
            padx=(0, 10)
        )

        self.title_entry.bind(
            "<KeyRelease>",
            self.update_preview
        )

        self.category = ctk.CTkOptionMenu(
            details_row,
            values=self.pm.db.get_categories() or ["Misc"],
            width=180,
            command=lambda _: self.update_preview()
        )

        self.category.pack(
            side="left",
            padx=10
        )

        self.status = ctk.CTkOptionMenu(
            details_row,
            values=[
                "In Progress",
                "Scheduled",
                "Completed"
            ],
            width=180,
            command=lambda _: self.update_preview()
        )

        self.status.set("In Progress")

        self.status.pack(
            side="left",
            padx=10
        )

        templates = self.pm.get_templates()

        self.template = ctk.CTkOptionMenu(
            details_row,
            values=templates,
            width=180,
            command=lambda _: self.update_preview()
        )

        if templates:
            self.template.set(templates[0])

        self.template.pack(
            side="left",
            padx=10
        )

        # ======================================
        # Content Columns
        # ======================================

        columns = ctk.CTkFrame(
            self.form,
            fg_color="transparent"
        )

        columns.pack(
            fill="x",
            padx=0,
            pady=(0, 10)
        )

        left = ctk.CTkFrame(columns)
        left.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0, 10),
            anchor="n"
        )

        right = ctk.CTkFrame(columns)
        right.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(10, 0),
            anchor="n"
        )

        # Left column
        self.add_textbox(
            left,
            "Paste From ChatGPT",
            "import_box",
            180
        )

        ctk.CTkButton(
            left,
            text="Import ChatGPT Text",
            height=38,
            font=EMOJI_BUTTON_FONT,
            command=self.import_chatgpt_text
        ).pack(
            fill="x",
            padx=15,
            pady=(5, 15)
        )

        self.add_textbox(
            left,
            "Script",
            "script",
            260
        )

        # Right column
        self.add_textbox(
            right,
            "Description",
            "description",
            130
        )

        self.add_textbox(
            right,
            "Pinned Comment",
            "pinned_comment",
            120
        )

        self.add_textbox(
            right,
            "Notes / Tags / Sources",
            "notes",
            180
        )

        self.open_after = ctk.CTkCheckBox(
            right,
            text="Open project after creating"
        )

        self.open_after.select()

        self.open_after.pack(
            anchor="w",
            padx=15,
            pady=(20, 10)
        )

        self.update_preview()

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
                
            messagebox.showinfo(
                "Success",
                "Project created successfully!"
            )

            if self.open_after.get():

                project = self.pm.db.get_latest_project()

                if project:

                    self.app.show_edit_project(
                        project["id"]
                    )

                else:

                    self.app.show_projects()

            else:

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
            "notes",
            "on_screen_text",
            "visual_plan"
        ]:

            if not hasattr(
                self,
                key
            ):

                continue

            box = getattr(
                self,
                key
            )

            box.delete(
                "1.0",
                "end"
            )

            box.insert(
                "1.0",
                data[key]
            )