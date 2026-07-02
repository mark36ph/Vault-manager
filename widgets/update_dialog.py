import customtkinter as ctk


class UpdateDialog(ctk.CTkToplevel):

    def __init__(self, parent, app_name, info, on_download):

        super().__init__(parent)

        self.on_download = on_download

        self.title("Update Available")
        self.geometry("540x430")
        self.resizable(False, False)

        self.transient(parent)
        self.grab_set()
        self.focus_force()

        ctk.CTkLabel(
            self,
            text="Update Available",
            font=("Segoe UI", 28, "bold")
        ).pack(pady=(25, 10))

        ctk.CTkLabel(
            self,
            text=f"{app_name} {info['latest_version']}",
            font=("Segoe UI", 18, "bold")
        ).pack(pady=(0, 10))

        ctk.CTkLabel(
            self,
            text=(
                f"Current Version: {info['current_version']}\n"
                f"Latest Version: {info['latest_version']}"
            ),
            font=("Segoe UI", 14)
        ).pack(pady=(0, 15))

        notes = ctk.CTkTextbox(
            self,
            width=460,
            height=130
        )
        notes.pack(padx=30, pady=(0, 20))

        notes.insert(
            "1.0",
            info.get("release_notes", "No release notes available.")
        )

        notes.configure(state="disabled")

        buttons = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )
        buttons.pack(pady=10)

        ctk.CTkButton(
            buttons,
            text="Later",
            width=140,
            fg_color="gray",
            command=self.destroy
        ).pack(side="left", padx=10)

        ctk.CTkButton(
            buttons,
            text="Download Update",
            width=180,
            command=self.download
        ).pack(side="left", padx=10)

        self.bind("<Escape>", lambda e: self.destroy())

    def download(self):

        self.destroy()

        self.on_download()