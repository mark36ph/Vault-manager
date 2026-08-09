import customtkinter as ctk


class UpdateDialog(ctk.CTkToplevel):
    def __init__(self, parent, app_name, info, on_download):
        super().__init__(parent)
        self.on_download = on_download

        self.title("Update Available")
        self.geometry("560x470")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.focus_force()

        shell = ctk.CTkFrame(
            self,
            corner_radius=10,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        shell.pack(fill="both", expand=True, padx=14, pady=14)

        ctk.CTkLabel(
            shell,
            text="Update available",
            font=("Segoe UI", 21, "bold"),
            anchor="w",
        ).pack(fill="x", padx=18, pady=(18, 4))

        ctk.CTkLabel(
            shell,
            text=f"{app_name} {info['latest_version']} is ready to download.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        ).pack(fill="x", padx=18, pady=(0, 14))

        version_card = ctk.CTkFrame(
            shell,
            corner_radius=8,
            fg_color=("#F8FAFC", "#14171C"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        version_card.pack(fill="x", padx=18, pady=(0, 12))

        ctk.CTkLabel(
            version_card,
            text=(
                f"Current  {info['current_version']}\n"
                f"Latest   {info['latest_version']}"
            ),
            font=("Segoe UI", 12),
            justify="left",
            anchor="w",
        ).pack(fill="x", padx=14, pady=12)

        ctk.CTkLabel(
            shell,
            text="Release notes",
            font=("Segoe UI", 13, "bold"),
            anchor="w",
        ).pack(fill="x", padx=18, pady=(0, 6))

        notes = ctk.CTkTextbox(
            shell,
            height=170,
            font=("Segoe UI", 12),
            corner_radius=7,
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        notes.pack(fill="both", expand=True, padx=18, pady=(0, 16))

        release_notes = info.get("release_notes", "No release notes available.")
        if isinstance(release_notes, list):
            release_notes = "\n".join(f"• {note}" for note in release_notes)

        notes.insert("1.0", release_notes)
        notes.configure(state="disabled")

        buttons = ctk.CTkFrame(shell, fg_color="transparent", height=42)
        buttons.pack(fill="x", padx=18, pady=(0, 18))
        buttons.pack_propagate(False)

        ctk.CTkButton(
            buttons,
            text="Later",
            width=108,
            height=38,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.destroy,
        ).pack(side="right")

        ctk.CTkButton(
            buttons,
            text="Download Update",
            width=166,
            height=38,
            corner_radius=7,
            command=self.download,
        ).pack(side="right", padx=(0, 10))

        self.bind("<Escape>", lambda _event: self.destroy())

    def download(self):
        self.destroy()
        self.on_download()


class UpToDateDialog(ctk.CTkToplevel):
    def __init__(self, parent, app_name, version):
        super().__init__(parent)

        self.title("Up to Date")
        self.geometry("500x270")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.focus_force()

        shell = ctk.CTkFrame(
            self,
            corner_radius=10,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        shell.pack(fill="both", expand=True, padx=14, pady=14)

        ctk.CTkLabel(
            shell,
            text="You're up to date",
            font=("Segoe UI", 21, "bold"),
            anchor="w",
        ).pack(fill="x", padx=18, pady=(18, 4))

        ctk.CTkLabel(
            shell,
            text=f"{app_name} is running the latest available version.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
            justify="left",
        ).pack(fill="x", padx=18, pady=(0, 14))

        version_card = ctk.CTkFrame(
            shell,
            corner_radius=8,
            fg_color=("#F8FAFC", "#14171C"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        version_card.pack(fill="x", padx=18, pady=(0, 16))

        ctk.CTkLabel(
            version_card,
            text=f"Current version  {version}",
            font=("Segoe UI", 12),
            anchor="w",
        ).pack(fill="x", padx=14, pady=12)

        buttons = ctk.CTkFrame(shell, fg_color="transparent")
        buttons.pack(fill="x", padx=18, pady=(0, 18))

        ctk.CTkButton(
            buttons,
            text="OK",
            width=96,
            height=34,
            command=self.destroy,
        ).pack(side="right")

        self.bind("<Escape>", lambda _event: self.destroy())
        self.bind("<Return>", lambda _event: self.destroy())
