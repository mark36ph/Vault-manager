import customtkinter as ctk


class BaseDialog(ctk.CTkToplevel):

    def __init__(
        self,
        parent,
        title,
        width=500,
        height=350
    ):

        super().__init__(parent)
        self.title(title)
        self.geometry(f"{width}x{height}")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.focus_force()

        self.container = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        self.container.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=20
        )