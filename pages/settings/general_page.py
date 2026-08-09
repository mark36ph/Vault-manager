import customtkinter as ctk
from tkinter import filedialog

from common.settings_manager import SettingsManager
from widgets.message_dialog import show_message


MUTED_TEXT = ("#667085", "#8F96A3")


class GeneralPage(ctk.CTkScrollableFrame):
    def __init__(self, parent, pm, app):
        super().__init__(
            parent,
            fg_color="transparent",
            scrollbar_button_color=("#D0D5DD", "#3A404B"),
            scrollbar_button_hover_color=("#98A2B3", "#596170"),
        )
        self.pm = pm
        self.app = app
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="General",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Choose where projects are stored and how the app behaves at startup.",
            font=("Segoe UI", 13),
            text_color=MUTED_TEXT,
        ).pack(anchor="w", padx=4, pady=(0, 16))

        storage = self._section("Project storage")
        ctk.CTkLabel(
            storage,
            text="Projects folder",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 6))

        folder_row = ctk.CTkFrame(storage, fg_color="transparent")
        folder_row.pack(fill="x", padx=14, pady=(0, 14))

        self.projects_folder = ctk.CTkEntry(folder_row, height=36)
        self.projects_folder.pack(side="left", fill="x", expand=True)
        self.projects_folder.insert(
            0,
            self.settings.get("general", "projects_folder", ""),
        )

        ctk.CTkButton(
            folder_row,
            text="Browse",
            width=88,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.browse_projects_folder,
        ).pack(side="left", padx=(8, 0))

        startup = self._section("Startup")

        self.start_maximized = ctk.BooleanVar(
            value=self.settings.get("general", "start_maximized", True)
        )
        self.remember_project = ctk.BooleanVar(
            value=self.settings.get("general", "remember_last_project", True)
        )
        self.check_updates = ctk.BooleanVar(
            value=self.settings.get("general", "check_updates", True)
        )

        for text, variable in (
            ("Open maximized", self.start_maximized),
            ("Remember last opened project", self.remember_project),
            ("Check for updates on startup", self.check_updates),
        ):
            ctk.CTkCheckBox(
                startup,
                text=text,
                variable=variable,
                font=("Segoe UI", 13),
            ).pack(anchor="w", padx=14, pady=6)

        appearance = self._section("Appearance")
        ctk.CTkLabel(
            appearance,
            text="Theme",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 6))

        self.theme = ctk.CTkOptionMenu(
            appearance,
            values=["Dark", "Light", "System"],
            width=180,
            height=34,
        )
        self.theme.pack(anchor="w", padx=14, pady=(0, 14))
        self.theme.set(self.settings.get("general", "theme", "dark").title())

        ctk.CTkButton(
            self,
            text="Save changes",
            height=36,
            width=130,
            corner_radius=7,
            command=self.save_settings,
        ).pack(anchor="e", padx=4, pady=(2, 4))

    def _section(self, title):
        frame = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        frame.pack(fill="x", padx=4, pady=(0, 10))
        ctk.CTkLabel(
            frame,
            text=title,
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 2))
        return frame

    def browse_projects_folder(self):
        folder = filedialog.askdirectory()
        if folder:
            self.projects_folder.delete(0, "end")
            self.projects_folder.insert(0, folder)

    def save_settings(self):
        self.settings.set(
            "general",
            "projects_folder",
            self.projects_folder.get().strip(),
        )
        self.settings.set(
            "general",
            "start_maximized",
            self.start_maximized.get(),
        )
        self.settings.set(
            "general",
            "remember_last_project",
            self.remember_project.get(),
        )
        self.settings.set(
            "general",
            "check_updates",
            self.check_updates.get(),
        )
        self.settings.set(
            "general",
            "theme",
            self.theme.get().lower(),
        )
        ctk.set_appearance_mode(self.theme.get())
        show_message(
            self,
            "Settings saved",
            "General settings were saved successfully.",
            kind="success",
        )
