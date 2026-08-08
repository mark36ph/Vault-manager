import customtkinter as ctk


class InputDialog(ctk.CTkToplevel):
    def __init__(
        self,
        parent,
        title,
        prompt,
        *,
        initial_value="",
        button_text="Continue",
    ):
        super().__init__(parent)
        self.result = None

        self.title(title)
        self.geometry("420x228")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.focus_force()

        shell = ctk.CTkFrame(
            self,
            fg_color=("#FFFFFF", "#181B21"),
            corner_radius=10,
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        shell.pack(fill="both", expand=True, padx=14, pady=14)

        ctk.CTkLabel(
            shell,
            text=title,
            font=("Segoe UI", 19, "bold"),
            anchor="w",
        ).pack(fill="x", padx=16, pady=(16, 4))

        ctk.CTkLabel(
            shell,
            text=prompt,
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
            justify="left",
            wraplength=360,
        ).pack(fill="x", padx=16)

        self.entry = ctk.CTkEntry(
            shell,
            height=36,
        )
        self.entry.pack(fill="x", padx=16, pady=(14, 6))
        if initial_value:
            self.entry.insert(0, str(initial_value))
            self.entry.select_range(0, "end")
        self.entry.focus()

        self.validation_label = ctk.CTkLabel(
            shell,
            text="",
            font=("Segoe UI", 11),
            text_color=("#B42318", "#FDA29B"),
            anchor="w",
        )
        self.validation_label.pack(fill="x", padx=16, pady=(0, 4))

        buttons = ctk.CTkFrame(shell, fg_color="transparent")
        buttons.pack(fill="x", padx=16, pady=(0, 16))

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=92,
            height=34,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.cancel,
        ).pack(side="right")

        ctk.CTkButton(
            buttons,
            text=button_text,
            width=96,
            height=34,
            command=self.ok,
        ).pack(side="right", padx=(0, 8))

        self.bind("<Return>", lambda _event: self.ok())
        self.bind("<Escape>", lambda _event: self.cancel())
        self.protocol("WM_DELETE_WINDOW", self.cancel)

    def ok(self):
        value = self.entry.get().strip()
        if not value:
            self.validation_label.configure(text="Please enter a value.")
            self.entry.focus()
            return

        self.result = value
        self.destroy()

    def cancel(self):
        self.result = None
        self.destroy()
