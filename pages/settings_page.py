import customtkinter as ctk
from tkinter import filedialog, messagebox
from pages.base_page import BasePage


class SettingsPage(BasePage):

    def __init__(self, parent, pm, app):

        super().__init__(parent, pm, "Settings")

        self.app = app

        self.settings = self.pm.load_settings()

        self.build()

    # ==========================================
    # UI
    # ==========================================

    def build(self):

        self.add_section_title("Application Settings")

        frame = ctk.CTkFrame(self)
        frame.pack(fill="x", padx=20, pady=20)

        # --------------------------
        # Projects Folder
        # --------------------------

        ctk.CTkLabel(
            frame,
            text="Projects Folder",
            font=("Segoe UI", 16, "bold")
        ).pack(anchor="w", padx=20, pady=(20, 5))

        self.projects_folder = ctk.CTkEntry(
            frame,
            width=700
        )

        self.projects_folder.pack(
            padx=20,
            fill="x"
        )

        self.projects_folder.insert(
            0,
            self.settings.get("projects_folder", "")
        )

        ctk.CTkButton(
            frame,
            text="Browse...",
            command=self.browse_projects_folder
        ).pack(
            anchor="w",
            padx=20,
            pady=10
        )

        # --------------------------
        # Save Button
        # --------------------------

        ctk.CTkButton(
            frame,
            text="💾 Save Settings",
            height=40,
            command=self.save_settings
        ).pack(
            pady=25
        )

    # ==========================================
    # Browse
    # ==========================================

    def browse_projects_folder(self):

        folder = filedialog.askdirectory()

        if folder:

            self.projects_folder.delete(0, "end")

            self.projects_folder.insert(0, folder)

    # ==========================================
    # Save
    # ==========================================

    def save_settings(self):

        settings = {

            "projects_folder": self.projects_folder.get().strip()

        }

        self.pm.save_settings(settings)

        messagebox.showinfo(
            "Settings",
            "Settings saved successfully."
        )