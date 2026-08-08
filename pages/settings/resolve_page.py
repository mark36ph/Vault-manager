import threading
from tkinter import filedialog

import customtkinter as ctk

from common.resolve_integration import inspect_resolve
from common.settings_manager import SettingsManager


class ResolvePage(ctk.CTkFrame):
    def __init__(self, parent, pm, app):
        super().__init__(parent, fg_color="transparent")
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="DaVinci Resolve",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text=(
                "Configure the Resolve Free export format and optional scripting connection. "
                "Normal exports use FCPXML for manual import."
            ),
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            wraplength=760,
            justify="left",
        ).pack(anchor="w", padx=4, pady=(0, 16))

        export_card = self._section("Export settings")
        dimensions = ctk.CTkFrame(export_card, fg_color="transparent")
        dimensions.pack(fill="x", padx=14, pady=(8, 14))
        for column in range(3):
            dimensions.grid_columnconfigure(column, weight=1)

        self.width_entry = self._number_field(
            dimensions, 0, "Width", "timeline_width", 1080
        )
        self.height_entry = self._number_field(
            dimensions, 1, "Height", "timeline_height", 1920
        )
        self.frame_rate_entry = self._number_field(
            dimensions, 2, "Frame rate", "frame_rate", 30
        )

        scripting_card = self._section("Optional scripting")
        self.application_entry = self._path_row(
            scripting_card,
            title="Resolve application",
            placeholder="Leave blank to detect Resolve automatically",
            setting_key="application_path",
            browse_command=self.browse_application,
        )
        self.module_entry = self._path_row(
            scripting_card,
            title="Scripting Modules folder",
            placeholder="Leave blank unless using Resolve scripting",
            setting_key="scripting_module_path",
            browse_command=self.browse_module_folder,
        )

        mode_row = ctk.CTkFrame(scripting_card, fg_color="transparent")
        mode_row.pack(fill="x", padx=14, pady=(2, 14))
        ctk.CTkLabel(
            mode_row,
            text="Integration mode",
            font=("Segoe UI", 13, "bold"),
        ).pack(side="left")

        self.mode = ctk.StringVar(
            value=self.settings.get("resolve", "integration_mode", "external")
        )
        ctk.CTkOptionMenu(
            mode_row,
            variable=self.mode,
            values=["external", "internal-script"],
            width=180,
            height=34,
        ).pack(side="right")

        footer = ctk.CTkFrame(self, fg_color="transparent")
        footer.pack(fill="x", padx=4, pady=(2, 0))

        self.status_label = ctk.CTkLabel(
            footer,
            text="Save export settings here. Test scripting only if you intend to use it.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            justify="left",
            anchor="w",
            wraplength=520,
        )
        self.status_label.pack(side="left", fill="x", expand=True)

        self.save_button = ctk.CTkButton(
            footer,
            text="Save settings",
            width=126,
            height=36,
            corner_radius=7,
            command=self.save_settings,
        )
        self.save_button.pack(side="right")

        self.test_button = ctk.CTkButton(
            footer,
            text="Test scripting",
            width=126,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.start_test,
        )
        self.test_button.pack(side="right", padx=(0, 8))

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

    def _path_row(self, parent, title, placeholder, setting_key, browse_command):
        ctk.CTkLabel(
            parent,
            text=title,
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(8, 5))

        row = ctk.CTkFrame(parent, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=(0, 8))

        entry = ctk.CTkEntry(row, placeholder_text=placeholder, height=36)
        entry.pack(side="left", fill="x", expand=True)
        saved = self.settings.get("resolve", setting_key, "")
        if saved:
            entry.insert(0, str(saved))

        ctk.CTkButton(
            row,
            text="Browse",
            width=88,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=browse_command,
        ).pack(side="left", padx=(8, 0))
        return entry

    def _number_field(self, parent, column, title, setting_key, default):
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.grid(row=0, column=column, padx=(0, 8), sticky="ew")
        ctk.CTkLabel(
            frame,
            text=title,
            font=("Segoe UI", 12, "bold"),
        ).pack(anchor="w")
        entry = ctk.CTkEntry(frame, height=34)
        entry.pack(fill="x", pady=(5, 0))
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
            self.status_label.configure(
                text="Width, height, and frame rate must be positive whole numbers."
            )
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
        self.status_label.configure(text="Resolve export settings saved.")

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
        self.test_button.configure(state="normal", text="Test scripting")
        self.status_label.configure(text=message)
