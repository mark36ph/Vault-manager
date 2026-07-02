import customtkinter as ctk
from common.app_info import AppInfo
from common.update_manager import UpdateManager
from tkinter import messagebox
from widgets.update_dialog import UpdateDialog

class AboutPage(ctk.CTkFrame):

    def __init__(self, parent, pm, app):
        super().__init__(parent)
        self.app_info = AppInfo()
        self.updater = UpdateManager()
        self.pm = pm
        self.app = app
        self.build()

    def build(self):

        ctk.CTkLabel(
            self,
            text="About",
            font=("Segoe UI", 28, "bold")
        ).pack(anchor="w", padx=20, pady=(20, 15))

        frame = ctk.CTkFrame(self, fg_color="transparent")
        frame.pack(fill="both", expand=True, padx=20)

        info = self.app_info.all()

        details = ctk.CTkFrame(
            frame,
            fg_color="transparent"
        )

        details.pack(anchor="w", padx=20)

        fields = [
            ("Developer", "developer"),
            ("Company", "company"),
            ("Website", "website"),
            ("Support Email", "support_email"),
        ]

        for label, key in fields:

            row = ctk.CTkFrame(
                details,
                fg_color="transparent"
            )

            row.pack(fill="x", pady=4)

            ctk.CTkLabel(
                row,
                text=f"{label}:",
                width=130,
                anchor="w",
                font=("Segoe UI", 13, "bold")
            ).pack(side="left")

            ctk.CTkLabel(
                row,
                text=str(info.get(key, "")),
                anchor="w"
            ).pack(side="left")

        name = self.app_info.get("name", "Fact Vault Manager")
        version = self.app_info.get("version", "1.0.0")
        build = self.app_info.get("build", 1)
        developer = self.app_info.get("developer", "Mark")

        ctk.CTkLabel(
            frame,
            text=f"Version {version}  •  Build {build}",
            font=("Segoe UI", 14)
        ).pack(anchor="w", padx=20, pady=(0, 20))

        ctk.CTkButton(
            frame,
            text="🔄 Check for Updates",
            width=220,
            command=self.check_for_updates
        ).pack(
            anchor="w",
            padx=20,
            pady=(25, 10)
        )

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
                    )
                )

            else:

                messagebox.showinfo(
                    "Up to Date",
                    f"You are running the latest version.\n\n"
                    f"Version {info['current_version']}"
                )

        except Exception as e:

            messagebox.showerror(
                "Update Check Failed",
                str(e)
            )