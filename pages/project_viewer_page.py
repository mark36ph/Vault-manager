from pathlib import Path
from tkinter import messagebox
import customtkinter as ctk

from pages.base_page import BasePage
from common.ui_fonts import EMOJI_FONT, EMOJI_BUTTON_FONT


class ProjectViewerPage(BasePage):

    def __init__(self, parent, pm, app, project_id):
        super().__init__(parent, pm, "Project Viewer")

        self.app = app
        self.project_id = project_id
        self.project = self.pm.db.get_project(project_id)

        if self.project is None:

            ctk.CTkLabel(
                self.content,
                text="Project could not be found.",
                font=("Segoe UI", 24, "bold")
            ).pack(
                pady=40
            )

            return

        self.build()

    def build(self):

        # ======================================
        # Header
        # ======================================

        header = ctk.CTkFrame(
            self.content,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10)
        )

        title_frame = ctk.CTkFrame(
            header,
            fg_color="transparent"
        )

        title_frame.pack(
            side="left",
            fill="x",
            expand=True
        )

        ctk.CTkLabel(
            title_frame,
            text=self.project["title"],
            font=("Segoe UI", 28, "bold")
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            title_frame,
            text="📋 Copy Title",
            width=120,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.copy_text(
                self.project["title"]
            )
        ).pack(
            side="left",
            padx=(12, 0)
        )

        ctk.CTkButton(
            header,
            text="✏ Edit",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.app.show_edit_project(
                self.project_id
            )
        ).pack(
            side="right",
            padx=(8, 0)
        )

        ctk.CTkButton(
            header,
            text="📂 Folder",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.app.open_project_folder(
                self.project
            )
        ).pack(
            side="right",
            padx=(8, 0)
        )

        ctk.CTkButton(
            header,
            text="← Back",
            width=100,
            command=self.app.show_projects
        ).pack(
            side="right"
        )

        # ======================================
        # Project details
        # ======================================

        details = ctk.CTkFrame(
            self.content
        )

        details.pack(
            fill="x",
            padx=20,
            pady=(0, 15)
        )

        detail_text = (
            f"Status: {self.project['status']}    "
            f"Category: {self.project['category']}    "
            f"Created: {self.project['created']}"
        )

        ctk.CTkLabel(
            details,
            text=detail_text,
            font=EMOJI_FONT,
            text_color="gray"
        ).pack(
            anchor="w",
            padx=15,
            pady=12
        )

        # ======================================
        # Tabs
        # ======================================

        tabs = ctk.CTkTabview(
            self.content
        )

        tabs.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 20)
        )

        tab_data = self.get_tab_data()

        for tab_name, tab_text in tab_data:

            tabs.add(
                tab_name
            )

            self.add_viewer_tab(
                tabs.tab(tab_name),
                tab_name,
                tab_text
            )

    def get_tab_data(self):

        return [
            (
                "Script",
                self.project["script"] or ""
            ),
            (
                "On-Screen Text",
                self.project["on_screen_text"] or ""
            ),
            (
                "Visual Plan",
                self.project["visual_plan"] or ""
            ),
            (
                "Description",
                self.project["description"] or ""
            ),
            (
                "Pinned Comment",
                self.project["pinned_comment"] or ""
            ),
            (
                "Notes",
                self.project["notes"] or ""
            )
        ]

    def add_viewer_tab(self, parent, label, text):

        top = ctk.CTkFrame(
            parent,
            fg_color="transparent"
        )

        top.pack(
            fill="x",
            padx=10,
            pady=(10, 5)
        )

        ctk.CTkLabel(
            top,
            text=label,
            font=("Segoe UI", 22, "bold")
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            top,
            text="📋 Copy",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.copy_text(
                text
            )
        ).pack(
            side="right"
        )

        box = ctk.CTkTextbox(
            parent,
            font=EMOJI_FONT,
            wrap="word"
        )

        box.pack(
            fill="both",
            expand=True,
            padx=10,
            pady=(0, 10)
        )

        if text.strip():

            box.insert(
                "1.0",
                text
            )

        else:

            box.insert(
                "1.0",
                "Nothing added yet."
            )

        box.configure(
            state="disabled"
        )

    def copy_text(self, text):

        self.clipboard_clear()

        self.clipboard_append(
            text
        )

        self.update()

        messagebox.showinfo(
            "Copied",
            "Copied to clipboard."
        )