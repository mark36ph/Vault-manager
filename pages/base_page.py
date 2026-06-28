import customtkinter as ctk


class BasePage(ctk.CTkFrame):

    def __init__(self, parent, pm, title):
        super().__init__(parent)

        self.pm = pm

        # ==========================
        # Page Title
        # ==========================

        self.header = ctk.CTkLabel(
            self,
            text=title,
            font=("Segoe UI", 30, "bold")
        )

        self.header.pack(
            anchor="w",
            padx=30,
            pady=(25, 15)
        )

        # ==========================
        # Main Content Area
        # ==========================

        self.content = ctk.CTkFrame(
            self,
            fg_color="transparent"
        )

        self.content.pack(
            fill="both",
            expand=True,
            padx=30,
            pady=(0, 20)
        )

    # =======================================
    # Utility Functions
    # =======================================

    def clear_content(self):
        """Remove all widgets from the page content."""
        for widget in self.content.winfo_children():
            widget.destroy()

    def refresh(self):
        """Override this in child pages if needed."""
        pass
 
    def add_section_title(self, text):

       label = ctk.CTkLabel(
           self.content,
           text=text,
           font=("Segoe UI", 22, "bold")
       )

       label.pack(
           anchor="w",
           pady=(20, 10)
       )

       return label