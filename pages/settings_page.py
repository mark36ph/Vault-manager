import customtkinter as ctk
from tkinter import filedialog, messagebox
from pages.base_page import BasePage
from common.settings_manager import SettingsManager

class SettingsPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Settings")
        self.app = app
        self.settings = SettingsManager()
        self.build()

    # ==========================================
    # UI
    # ==========================================

    def build(self):

        self.add_section_title("General Settings")

        frame = ctk.CTkFrame(self)
        frame.pack(fill="both", expand=True, padx=20, pady=20)

        # ==========================================
        # Projects Folder
        # ==========================================

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
            fill="x",
            padx=20
        )

        self.projects_folder.insert(
            0,
            self.settings.get(
                "general",
                "projects_folder",
                ""
            )
        )

        ctk.CTkButton(
            frame,
            text="Browse...",
            command=self.browse_projects_folder
        ).pack(
            anchor="w",
            padx=20,
            pady=(10, 20)
        )

        # ==========================================
        # Startup Options
        # ==========================================

        ctk.CTkLabel(
            frame,
            text="Startup",
            font=("Segoe UI", 16, "bold")
        ).pack(anchor="w", padx=20)

        self.start_maximized = ctk.BooleanVar(
            value=self.settings.get("general", "start_maximized", True)
        )

        ctk.CTkCheckBox(
            frame,
            text="Open maximized",
            variable=self.start_maximized
        ).pack(anchor="w", padx=25, pady=5)

        self.remember_project = ctk.BooleanVar(
            value=self.settings.get("general", "remember_last_project", True)
        )

        ctk.CTkCheckBox(
            frame,
            text="Remember last opened project",
            variable=self.remember_project
        ).pack(anchor="w", padx=25, pady=5)

        self.check_updates = ctk.BooleanVar(
            value=self.settings.get("general", "check_updates", True)
        )

        ctk.CTkCheckBox(
            frame,
            text="Check for updates on startup",
            variable=self.check_updates
        ).pack(anchor="w", padx=25, pady=5)

        # ==========================================
        # Theme
        # ==========================================

        ctk.CTkLabel(
            frame,
            text="Appearance",
            font=("Segoe UI", 16, "bold")
        ).pack(anchor="w", padx=20, pady=(25, 5))

        self.theme = ctk.CTkOptionMenu(
            frame,
            values=[
                "Dark",
                "Light",
                "System"
            ]
        )

        self.theme.pack(
            anchor="w",
            padx=20
        )

        self.theme.set(
            self.settings.get(
                "general",
                "theme",
                "dark"
            ).title()
        )

        # ==========================================
        # Save
        # ==========================================

        ctk.CTkButton(
            frame,
            text="💾 Save Settings",
            height=40,
            command=self.save_settings
        ).pack(
            pady=30
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

        self.settings.set(
            "general",
            "projects_folder",
            self.projects_folder.get().strip()
        )

        self.settings.set(
            "general",
            "start_maximized",
            self.start_maximized.get()
        )

        self.settings.set(
            "general",
            "remember_last_project",
            self.remember_project.get()
        )

        self.settings.set(
            "general",
            "check_updates",
            self.check_updates.get()
        )

        self.settings.set(
            "general",
            "theme",
            self.theme.get().lower()
        )

        ctk.set_appearance_mode(
            self.theme.get()
        )

        messagebox.showinfo(
            "Settings",
            "Settings saved successfully."
        )