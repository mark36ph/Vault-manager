import threading
from tkinter import filedialog

import customtkinter as ctk

from common.resolve_integration import inspect_resolve
from common.settings_manager import SettingsManager


class ResolvePage(ctk.CTkFrame):
    def __init__(self, parent, pm, app):
        super().__init__(parent)
        self.pm = pm
        self.app = app
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="DaVinci Resolve",
            font=("Segoe UI", 28, "bold"),
        ).pack(anchor="w", padx=20, pady=(20, 5))

        ctk.CTkLabel(
            self,
            text=(
                "Configure the local Resolve installation and test the Python "
                "scripting connection. Resolve does not require an API key."
            ),
            text_color="gray",
            wraplength=760,
            justify="left",
        ).pack(anchor="w", padx=20, pady=(0, 20))

        container = ctk.CTkFrame(self)
        container.pack(fill="x", padx=20, pady=(0, 20))
        container.grid_columnconfigure(0, weight=1)

        self.application_entry = self._path_row(
            container,
            row=0,
            title="Resolve application",
            placeholder="Leave blank to detect Resolve automatically",
            setting_key="application_path",
            browse_command=self.browse_application,
        )
        self.module_entry = self._path_row(
            container,
            row=2,
            title="Scripting Modules folder",
            placeholder="Leave blank to detect DaVinciResolveScript automatically",
            setting_key="scripting_module_path",
            browse_command=self.browse_module_folder,
        )

        ctk.CTkLabel(
            container,
            text="Integration mode",
            font=("Segoe UI", 16, "bold"),
        ).grid(row=4, column=0, columnspan=2, padx=20, pady=(12, 8), sticky="w")
        self.mode = ctk.StringVar(
            value=self.settings.get("resolve", "integration_mode", "external")
        )
        ctk.CTkOptionMenu(
            container,
            variable=self.mode,
            values=["external", "internal-script"],
            width=220,
        ).grid(row=5, column=0, padx=20, pady=(0, 18), sticky="w")

        dimensions = ctk.CTkFrame(container, fg_color="transparent")
        dimensions.grid(row=6, column=0, columnspan=2, padx=20, pady=(0, 18), sticky="ew")
        for column in range(3):
            dimensions.grid_columnconfigure(column, weight=1)
        self.width_entry = self._number_field(dimensions, 0, "Width", "timeline_width", 1080)
        self.height_entry = self._number_field(dimensions, 1, "Height", "timeline_height", 1920)
        self.frame_rate_entry = self._number_field(dimensions, 2, "Frame rate", "frame_rate", 30)

        actions = ctk.CTkFrame(container, fg_color="transparent")
        actions.grid(row=7, column=0, columnspan=2, padx=20, pady=(0, 12), sticky="ew")
        self.save_button = ctk.CTkButton(actions, text="Save Settings", command=self.save_settings)
        self.save_button.pack(side="left")
        self.test_button = ctk.CTkButton(
            actions,
            text="Test Resolve Connection",
            command=self.start_test,
        )
        self.test_button.pack(side="left", padx=(10, 0))

        self.status_label = ctk.CTkLabel(
            container,
            text="Save the settings, start Resolve, then test the connection.",
            justify="left",
            anchor="w",
            wraplength=760,
        )
        self.status_label.grid(row=8, column=0, columnspan=2, padx=20, pady=(0, 20), sticky="ew")

    def _path_row(self, parent, row, title, placeholder, setting_key, browse_command):
        ctk.CTkLabel(parent, text=title, font=("Segoe UI", 16, "bold")).grid(
            row=row, column=0, columnspan=2, padx=20, pady=(20 if row == 0 else 12, 8), sticky="w"
        )
        entry = ctk.CTkEntry(parent, placeholder_text=placeholder)
        entry.grid(row=row + 1, column=0, padx=(20, 8), pady=(0, 8), sticky="ew")
        saved = self.settings.get("resolve", setting_key, "")
        if saved:
            entry.insert(0, str(saved))
        ctk.CTkButton(parent, text="Browse", width=90, command=browse_command).grid(
            row=row + 1, column=1, padx=(0, 20), pady=(0, 8)
        )
        return entry

    def _number_field(self, parent, column, title, setting_key, default):
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.grid(row=0, column=column, padx=(0, 10), sticky="ew")
        ctk.CTkLabel(frame, text=title).pack(anchor="w")
        entry = ctk.CTkEntry(frame)
        entry.pack(fill="x", pady=(4, 0))
        entry.insert(0, str(self.settings.get("resolve", setting_key, default)))
        return entry

    def browse_application(self):
        path = filedialog.askopenfilename(title="Select DaVinci Resolve application")
        if path:
            self.application_entry.delete(0, "end")
            self.application_entry.insert(0, path)

    def browse_module_folder(self):
        path = filedialog.askdirectory(title="Select Resolve Scripting Modules folder")
        if path:
            self.module_entry.delete(0, "end")
            self.module_entry.insert(0, path)

    def save_settings(self):
        try:
            width = int(self.width_entry.get())
            height = int(self.height_entry.get())
            frame_rate = int(self.frame_rate_entry.get())
            if min(width, height, frame_rate) <= 0:
                raise ValueError
        except ValueError:
            self.status_label.configure(text="Width, height, and frame rate must be positive whole numbers.")
            return

        self.settings.update_section(
            "resolve",
            {
                "application_path": self.application_entry.get().strip(),
                "scripting_module_path": self.module_entry.get().strip(),
                "integration_mode": self.mode.get(),
                "timeline_width": width,
                "timeline_height": height,
                "frame_rate": frame_rate,
            },
        )
        self.status_label.configure(text="Resolve settings saved.")

    def start_test(self):
        self.save_settings()
        self.test_button.configure(state="disabled", text="Testing...")
        threading.Thread(target=self._test_connection, daemon=True).start()

    def _test_connection(self):
        status = inspect_resolve(
            self.application_entry.get().strip(),
            self.module_entry.get().strip(),
        )
        details = [status.message]
        if status.application_path:
            details.append(f"Application: {status.application_path}")
        if status.product_name or status.version:
            details.append(f"Product: {status.product_name} {status.version}".strip())
        self.after(0, lambda: self._show_test_result("\n".join(details)))

    def _show_test_result(self, message):
        self.test_button.configure(state="normal", text="Test Resolve Connection")
        self.status_label.configure(text=message)
