from tkinter import messagebox

import customtkinter as ctk

from common.ui_fonts import EMOJI_BUTTON_FONT, EMOJI_FONT
from pages.base_page import BasePage
from widgets.media_search_panel import MediaSearchPanel
from widgets.project_assets_panel import ProjectAssetsPanel


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
                font=("Segoe UI", 24, "bold"),
            ).pack(pady=40)
            return
        self.build()

    def build(self):
        header = ctk.CTkFrame(self.content, fg_color="transparent")
        header.pack(fill="x", padx=20, pady=(20, 10))

        title_frame = ctk.CTkFrame(header, fg_color="transparent")
        title_frame.pack(side="left", fill="x", expand=True)
        ctk.CTkLabel(
            title_frame,
            text=self.project["title"],
            font=("Segoe UI", 28, "bold"),
        ).pack(side="left")
        ctk.CTkButton(
            title_frame,
            text="📋 Copy Title",
            width=120,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.copy_text(self.project["title"]),
        ).pack(side="left", padx=(12, 0))

        ctk.CTkButton(
            header,
            text="✏ Edit",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.app.show_edit_project(self.project_id),
        ).pack(side="right", padx=(8, 0))
        ctk.CTkButton(
            header,
            text="🗂 Assets",
            width=110,
            font=EMOJI_BUTTON_FONT,
            command=self.show_assets,
        ).pack(side="right", padx=(8, 0))
        ctk.CTkButton(
            header,
            text="🔍 Media",
            width=110,
            font=EMOJI_BUTTON_FONT,
            command=self.show_media_search,
        ).pack(side="right", padx=(8, 0))
        ctk.CTkButton(
            header,
            text="📂 Folder",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.app.open_project_folder(self.project),
        ).pack(side="right", padx=(8, 0))
        ctk.CTkButton(
            header,
            text="← Back",
            width=100,
            command=self.app.show_projects,
        ).pack(side="right")

        details = ctk.CTkFrame(self.content)
        details.pack(fill="x", padx=20, pady=(0, 15))
        detail_text = (
            f"Status: {self.project['status']}    "
            f"Category: {self.project['category']}    "
            f"Created: {self.project['created']}"
        )
        ctk.CTkLabel(
            details,
            text=detail_text,
            font=EMOJI_FONT,
            text_color="gray",
        ).pack(anchor="w", padx=15, pady=12)

        self.tabs = ctk.CTkTabview(self.content)
        self.tabs.pack(fill="both", expand=True, padx=20, pady=(0, 20))
        for tab_name, tab_text in self.get_tab_data():
            self.tabs.add(tab_name)
            self.add_viewer_tab(self.tabs.tab(tab_name), tab_name, tab_text)

        project_folder = self.pm.resolve_project_folder(self.project)

        self.tabs.add("Media")
        self.media_search_panel = MediaSearchPanel(
            self.tabs.tab("Media"),
            self.project,
            project_folder=project_folder,
        )
        self.media_search_panel.pack(fill="both", expand=True)

        self.tabs.add("Assets")
        self.assets_panel = ProjectAssetsPanel(
            self.tabs.tab("Assets"),
            project_folder=project_folder,
        )
        self.assets_panel.pack(fill="both", expand=True)

    def show_media_search(self):
        self.tabs.set("Media")
        self.media_search_panel.search_entry.focus_set()

    def show_assets(self):
        self.assets_panel.refresh_assets()
        self.tabs.set("Assets")

    def get_tab_data(self):
        return [
            ("Script", self.project["script"] or ""),
            ("On-Screen Text", self.project["on_screen_text"] or ""),
            ("Visual Plan", self.project["visual_plan"] or ""),
            ("Description", self.project["description"] or ""),
            ("Pinned Comment", self.project["pinned_comment"] or ""),
            ("Notes", self.project["notes"] or ""),
        ]

    def add_viewer_tab(self, parent, label, text):
        top = ctk.CTkFrame(parent, fg_color="transparent")
        top.pack(fill="x", padx=10, pady=(10, 5))
        ctk.CTkLabel(
            top,
            text=label,
            font=("Segoe UI", 22, "bold"),
        ).pack(side="left")
        ctk.CTkButton(
            top,
            text="📋 Copy",
            width=100,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.copy_text(text),
        ).pack(side="right")
        box = ctk.CTkTextbox(parent, font=EMOJI_FONT, wrap="word")
        box.pack(fill="both", expand=True, padx=10, pady=(0, 10))
        box.insert("1.0", text if text.strip() else "Nothing added yet.")
        box.configure(state="disabled")

    def copy_text(self, text):
        self.clipboard_clear()
        self.clipboard_append(text)
        self.update()
        messagebox.showinfo("Copied", "Copied to clipboard.")
