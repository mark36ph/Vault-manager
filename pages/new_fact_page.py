from tkinter import messagebox
import customtkinter as ctk

from pages.base_page import BasePage


class NewFactPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "New Fact")

        self.app = app

        self.build()

    def build(self):

        # ==========================================
        # Main container
        # ==========================================

        main = ctk.CTkFrame(
            self.content,
            fg_color="transparent"
        )

        main.pack(
            fill="both",
            expand=True,
            padx=10,
            pady=10
        )

        # ==========================================
        # Left Panel
        # ==========================================

        self.left_panel = ctk.CTkFrame(
            main,
            corner_radius=12
        )

        self.left_panel.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0,10)
        )

        # ==========================================
        # Right Panel
        # ==========================================

        self.right_panel = ctk.CTkFrame(
            main,
            width=320,
            corner_radius=12
        )

        self.right_panel.pack(
            side="right",
            fill="y"
        )

        self.right_panel.pack_propagate(False)

        # ==========================================
        # Build Sections
        # ==========================================

        self.build_left_panel()
        self.build_right_panel()

    def build_left_panel(self):

        ctk.CTkLabel(
            self.left_panel,
            text="Project Details",
            font=("Segoe UI", 26, "bold")
        ).pack(
            anchor="w",
            padx=25,
            pady=(20,10)
        )

        self.form = ctk.CTkFrame(
            self.left_panel,
            fg_color="transparent"
        )

        self.form.pack(
            fill="both",
            expand=True,
            padx=25,
            pady=(0,20)
        )

    def build_right_panel(self):

        ctk.CTkLabel(
            self.right_panel,
            text="Project Preview",
            font=("Segoe UI",24,"bold")
        ).pack(
            pady=(20,15)
        )

        self.preview = ctk.CTkTextbox(
            self.right_panel,
            width=280
        )

        self.preview.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0,20)
        )

        self.preview.insert(
            "1.0",
            """📁 New Project

    Category:
    -

    Status:
    -

    Template:
    -

    --------------------------

    Files

    ✔ Script.txt

    ✔ Description.txt

    ✔ Notes.txt

    ✔ project.json

    --------------------------

    Folders

    ✔ Assets

    ✔ Images

    ✔ Videos

    ✔ Music

    ✔ Export
    """
        )

        self.preview.configure(
            state="disabled"
        )

    def create_project(self):

        title = self.title_entry.get().strip()

        if not title:

            messagebox.showerror(
                "Missing Title",
                "Please enter a title."
            )

            return

        try:

            folder = self.pm.create_project(
                title,
                self.category.get()
            )

            self.status.configure(
                text="✔ Project created successfully!",
                text_color="lightgreen"
            )

            messagebox.showinfo(
                "Success",
                f"Project created.\n\n{folder}"
            )

            self.app.show_dashboard()

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )