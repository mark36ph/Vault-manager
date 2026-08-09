import customtkinter as ctk


class MessageDialog(ctk.CTkToplevel):
    """Small app-styled modal message dialog for settings feedback."""

    def __init__(self, parent, title, message, *, kind="info", button_text="OK"):
        super().__init__(parent)

        self.title(title)
        self.geometry("500x250")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.lift()
        self.focus_force()

        shell = ctk.CTkFrame(
            self,
            corner_radius=10,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        shell.pack(fill="both", expand=True, padx=14, pady=14)

        title_color = {
            "success": ("#027A48", "#75E0A7"),
            "warning": ("#B54708", "#FEC84B"),
            "error": ("#B42318", "#FDA29B"),
        }.get(kind, ("#101828", "#F2F4F7"))

        ctk.CTkLabel(
            shell,
            text=title,
            font=("Segoe UI", 20, "bold"),
            text_color=title_color,
            anchor="w",
        ).pack(fill="x", padx=20, pady=(20, 8))

        ctk.CTkLabel(
            shell,
            text=message,
            font=("Segoe UI", 12),
            text_color=("#475467", "#B7BDC8"),
            justify="left",
            anchor="w",
            wraplength=430,
        ).pack(fill="both", expand=True, padx=20, pady=(0, 14))

        buttons = ctk.CTkFrame(shell, fg_color="transparent")
        buttons.pack(fill="x", padx=20, pady=(0, 18))

        ctk.CTkButton(
            buttons,
            text=button_text,
            width=96,
            height=34,
            corner_radius=7,
            command=self.destroy,
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", self.destroy)
        self.bind("<Escape>", lambda _event: self.destroy())
        self.bind("<Return>", lambda _event: self.destroy())


def show_message(parent, title, message, *, kind="info", button_text="OK"):
    """Open a consistent app-styled modal message dialog."""
    return MessageDialog(
        parent,
        title,
        message,
        kind=kind,
        button_text=button_text,
    )
