import customtkinter as ctk
from tkinter import messagebox

from common.app_info import AppInfo
from common.update_manager import UpdateManager
from widgets.update_dialog import UpdateDialog


class AboutPage(ctk.CTkFrame):
    def __init__(self, parent, pm, app):
        super().__init__(parent, fg_color="transparent")
        self.app_info = AppInfo()
        self.updater = UpdateManager()
        self.build()

    def build(self):
        info = self.app_info.all()
        name = self.app_info.get("name", "Fact Vault Manager")
        version = self.app_info.get("version", "1.0.0")
        build = self.app_info.get("build", 1)

        ctk.CTkLabel(
            self,
            text="About",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Application information, support details, and update status.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=4, pady=(0, 16))

        app_card = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        app_card.pack(fill="x", padx=4, pady=(0, 10))

        ctk.CTkLabel(
            app_card,
            text=name,
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w", padx=14, pady=(14, 2))

        ctk.CTkLabel(
            app_card,
            text=f"Version {version}  •  Build {build}",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=14, pady=(0, 14))

        details = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        details.pack(fill="x", padx=4, pady=(0, 10))

        ctk.CTkLabel(
            details,
            text="Details",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 6))

        fields = [
            ("Developer", "developer"),
            ("Company", "company"),
            ("Website", "website"),
            ("Support email", "support_email"),
        ]

        for label, key in fields:
            row = ctk.CTkFrame(details, fg_color="transparent")
            row.pack(fill="x", padx=14, pady=4)

            ctk.CTkLabel(
                row,
                text=label,
                width=112,
                anchor="w",
                font=("Segoe UI", 12, "bold"),
            ).pack(side="left")

            ctk.CTkLabel(
                row,
                text=str(info.get(key, "")),
                anchor="w",
                font=("Segoe UI", 12),
                text_color=("#475467", "#B7BDC8"),
            ).pack(side="left", fill="x", expand=True)

        ctk.CTkFrame(details, height=6, fg_color="transparent").pack()

        updates = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        updates.pack(fill="x", padx=4, pady=(0, 10))

        copy = ctk.CTkFrame(updates, fg_color="transparent")
        copy.pack(side="left", fill="x", expand=True, padx=14, pady=12)

        ctk.CTkLabel(
            copy,
            text="Updates",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w")

        ctk.CTkLabel(
            copy,
            text="Check whether a newer version of the application is available.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", pady=(2, 0))

        ctk.CTkButton(
            updates,
            text="Check for updates",
            width=138,
            height=36,
            corner_radius=7,
            command=self.check_for_updates,
        ).pack(side="right", padx=14, pady=12)

    def check_for_updates(self):
        try:
            info = self.updater.check_for_updates()
            if info["update_available"]:
                UpdateDialog(
                    self,
                    self.app_info.get("name", "Fact Vault Manager"),
                    info,
                    lambda: self.updater.open_download_page(
                        info.get("download_url", "")
                    ),
                )
            else:
                messagebox.showinfo(
                    "Up to Date",
                    (
                        "You are running the latest version.\n\n"
                        f"Version {info['current_version']}"
                    ),
                )
        except Exception as exc:
            messagebox.showerror("Update Check Failed", str(exc))
