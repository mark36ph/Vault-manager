import customtkinter as ctk


class InputDialog(ctk.CTkToplevel):

    def __init__(self, parent, title, prompt):

        super().__init__(parent)

        self.result = None

        self.title(title)

        self.geometry("420x220")

        self.resizable(False, False)

        self.grab_set()

        self.focus_force()

        ctk.CTkLabel(
            self,
            text=title,
            font=("Segoe UI", 24, "bold")
        ).pack(
            pady=(20, 10)
        )

        ctk.CTkLabel(
            self,
            text=prompt
        ).pack()

        self.entry = ctk.CTkEntry(
            self,
            width=320,
            height=36
        )

        self.entry.pack(
            pady=15
        )

        self.entry.focus()

        buttons = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        buttons.pack(
            pady=15
        )

        ctk.CTkButton(
            buttons,
            text="Create",
            width=110,
            command=self.ok
        ).pack(
            side="left",
            padx=10
        )

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=110,
            command=self.cancel
        ).pack(
            side="left",
            padx=10
        )

        self.bind("<Return>", lambda e: self.ok())

        self.bind("<Escape>", lambda e: self.cancel())

    def ok(self):

        self.result = self.entry.get().strip()

        self.destroy()

    def cancel(self):

        self.result = None

        self.destroy()