from tkinter import messagebox
import customtkinter as ctk

from pages.base_page import BasePage


class NewFactPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "New Fact")

        self.app = app

        self.build()

    def build(self):

        form = ctk.CTkFrame(self.content)
        form.pack(anchor="nw", padx=10, pady=10)

        ctk.CTkLabel(
            form,
            text="Fact Title"
        ).grid(row=0, column=0, sticky="w", pady=(15, 5))

        self.title_entry = ctk.CTkEntry(
            form,
            width=400,
            placeholder_text="Enter a title..."
        )

        self.title_entry.grid(
            row=1,
            column=0,
            padx=15,
            pady=(0, 15)
        )

        ctk.CTkLabel(
            form,
            text="Category"
        ).grid(row=2, column=0, sticky="w", padx=15)

        categories = self.pm.db.get_categories()

        if not categories:
            categories = ["Misc"]

        self.category = ctk.CTkOptionMenu(
            form,
            values=categories,
            width=220
        )

        self.category.grid(
            row=3,
            column=0,
            padx=15,
            pady=(5, 20),
            sticky="w"
        )

        self.status = ctk.CTkLabel(
            form,
            text=""
        )

        self.status.grid(
            row=4,
            column=0,
            padx=15,
            pady=(0, 15),
            sticky="w"
        )

        ctk.CTkButton(
            form,
            text="Create Project",
            width=200,
            command=self.create_project
        ).grid(
            row=5,
            column=0,
            padx=15,
            pady=(0, 20),
            sticky="w"
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