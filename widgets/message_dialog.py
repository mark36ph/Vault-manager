import customtkinter as ctk


class MessageDialog(ctk.CTkToplevel):
    """Small app-styled modal message dialog for application feedback."""

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


class ConfirmDialog(ctk.CTkToplevel):
    """App-styled yes/no confirmation dialog."""

    def __init__(
        self,
        parent,
        title,
        message,
        *,
        confirm_text="Continue",
        cancel_text="Cancel",
        kind="warning",
    ):
        super().__init__(parent)
        self.result = False

        self.title(title)
        self.geometry("520x280")
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
            wraplength=450,
        ).pack(fill="both", expand=True, padx=20, pady=(0, 14))

        buttons = ctk.CTkFrame(shell, fg_color="transparent")
        buttons.pack(fill="x", padx=20, pady=(0, 18))

        ctk.CTkButton(
            buttons,
            text=cancel_text,
            width=96,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            command=lambda: self._finish(False),
        ).pack(side="left")

        confirm_kwargs = {}
        if kind == "error":
            confirm_kwargs.update(fg_color="#B42318", hover_color="#912018")

        ctk.CTkButton(
            buttons,
            text=confirm_text,
            width=112,
            height=34,
            corner_radius=7,
            command=lambda: self._finish(True),
            **confirm_kwargs,
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", lambda: self._finish(False))
        self.bind("<Escape>", lambda _event: self._finish(False))
        self.bind("<Return>", lambda _event: self._finish(True))

    def _finish(self, result):
        self.result = bool(result)
        self.destroy()


class ThreeChoiceDialog(ctk.CTkToplevel):
    """App-styled modal dialog that returns one of three named choices."""

    def __init__(
        self,
        parent,
        title,
        message,
        *,
        primary_text="Save",
        secondary_text="Discard",
        cancel_text="Cancel",
    ):
        super().__init__(parent)
        self.result = "cancel"

        self.title(title)
        self.geometry("540x290")
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

        ctk.CTkLabel(
            shell,
            text=title,
            font=("Segoe UI", 20, "bold"),
            text_color=("#B54708", "#FEC84B"),
            anchor="w",
        ).pack(fill="x", padx=20, pady=(20, 8))

        ctk.CTkLabel(
            shell,
            text=message,
            font=("Segoe UI", 12),
            text_color=("#475467", "#B7BDC8"),
            justify="left",
            anchor="w",
            wraplength=470,
        ).pack(fill="both", expand=True, padx=20, pady=(0, 14))

        buttons = ctk.CTkFrame(shell, fg_color="transparent")
        buttons.pack(fill="x", padx=20, pady=(0, 18))

        ctk.CTkButton(
            buttons,
            text=cancel_text,
            width=92,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            command=lambda: self._finish("cancel"),
        ).pack(side="left")

        ctk.CTkButton(
            buttons,
            text=secondary_text,
            width=100,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            command=lambda: self._finish("secondary"),
        ).pack(side="right", padx=(8, 0))

        ctk.CTkButton(
            buttons,
            text=primary_text,
            width=100,
            height=34,
            corner_radius=7,
            command=lambda: self._finish("primary"),
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", lambda: self._finish("cancel"))
        self.bind("<Escape>", lambda _event: self._finish("cancel"))
        self.bind("<Return>", lambda _event: self._finish("primary"))

    def _finish(self, result):
        self.result = result
        self.destroy()


def show_message(parent, title, message, *, kind="info", button_text="OK"):
    """Open a consistent app-styled modal message dialog."""
    return MessageDialog(
        parent,
        title,
        message,
        kind=kind,
        button_text=button_text,
    )


def ask_confirmation(
    parent,
    title,
    message,
    *,
    confirm_text="Continue",
    cancel_text="Cancel",
    kind="warning",
):
    """Show a modal confirmation and return True only when confirmed."""
    dialog = ConfirmDialog(
        parent,
        title,
        message,
        confirm_text=confirm_text,
        cancel_text=cancel_text,
        kind=kind,
    )
    parent.wait_window(dialog)
    return dialog.result


def ask_three_choice(
    parent,
    title,
    message,
    *,
    primary_text="Save",
    secondary_text="Discard",
    cancel_text="Cancel",
):
    """Return 'primary', 'secondary', or 'cancel' from an app-styled dialog."""
    dialog = ThreeChoiceDialog(
        parent,
        title,
        message,
        primary_text=primary_text,
        secondary_text=secondary_text,
        cancel_text=cancel_text,
    )
    parent.wait_window(dialog)
    return dialog.result
