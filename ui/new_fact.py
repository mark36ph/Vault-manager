import customtkinter as ctk
from tkinter import messagebox

from project_manager import ProjectManager


class NewFactWindow(ctk.CTkToplevel):

    def __init__(self, parent):
        super().__init__(parent)
        self.transient(parent)
        self.lift()
        self.focus_force()
        self.grab_set()

        self.pm = ProjectManager()

        self.title("New Fact")
        self.geometry("520x430")
        self.resizable(False, False)

        self.grab_set()

        ctk.CTkLabel(
            self,
            text="Create New Fact",
            font=("Segoe UI", 26, "bold")
        ).pack(pady=20)

        ctk.CTkLabel(
            self,
            text="Fact Title"
        ).pack()

        self.title_entry = ctk.CTkEntry(
            self,
            width=360,
            placeholder_text="Enter a fact title..."
        )

        self.title_entry.pack(pady=10)

        ctk.CTkLabel(
            self,
            text="Category"
        ).pack()

        # Load categories from the database
        categories = self.pm.db.get_categories()

        if not categories:
            categories = ["Misc"]

        self.category = ctk.CTkOptionMenu(
            self,
            values=categories
        )

        self.category.pack(pady=10)

        self.status = ctk.CTkLabel(
            self,
            text="",
            text_color="lightgreen"
        )

        self.status.pack(pady=5)

        self.create_button = ctk.CTkButton(
            self,
            text="Create Project",
            width=240,
            height=42,
            command=self.create_project
        )

        self.create_button.pack(pady=20)

        self.title_entry.focus()

    def create_project(self):

        title = self.title_entry.get().strip()

        if len(title) == 0:
            messagebox.showerror(
                "Missing Title",
                "Please enter a project title."
            )
            return

        try:

            folder = self.pm.create_project(
                title,
                self.category.get()
            )

            self.status.configure(
                text="✔ Project created successfully!"
            )

            messagebox.showinfo(
                "Success",
                f"Project created successfully.\n\n{folder}"
            )

            # Clear the form for the next project
            self.title_entry.delete(0, "end")
            self.category.set(self.pm.db.get_categories()[0])

            self.destroy()

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )