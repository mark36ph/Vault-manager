import customtkinter as ctk

class Page(ctk.CTkFrame):

    def __init__(self,parent):
        super().__init__(parent)

        ctk.CTkLabel(
            self,
            text="pages\__init__.py",
            font=("Segoe UI",24,"bold")
        ).pack(pady=30)
