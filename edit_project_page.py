from tkinter import messagebox
import customtkinter as ctk
import os
from pages.base_page import BasePage


class EditProjectPage(BasePage):

    def __init__(self, parent, pm, app, project_id):

        super().__init__(parent, pm, "Edit Project")

        self.app = app
        self.project_id = project_id

        self.project = self.pm.db.get_project(project_id)

        if not self.project:

            messagebox.showerror(
                "Error",
                "Project not found."
            )

            self.app.show_projects()
            return

        self.build()

        self.load_project()

    # =====================================

    def build(self):

        self.form = ctk.CTkScrollableFrame(
            self.content
        )

        self.form.pack(
            fill="both",
            expand=True
        )

        self.title_label = ctk.CTkLabel(
            self.form,
            text="Project Title"
        )

        self.title_label.pack(
            anchor="w",
            padx=15,
            pady=(15,5)
        )

        self.title_entry = ctk.CTkEntry(
            self.form,
            width=500
        )

        self.title_entry.pack(
            anchor="w",
            padx=15
        )

        self.category_label = ctk.CTkLabel(
            self.form,
            text="Category"
        )

        self.category_label.pack(
            anchor="w",
            padx=15,
            pady=(20,5)
        )

        categories = self.pm.db.get_categories()

        if not categories:
            categories = ["Misc"]

        self.category = ctk.CTkOptionMenu(
            self.form,
            values=categories,
            width=220
        )

        self.category.pack(
            anchor="w",
            padx=15
        )


        # =====================================


        def open_folder(self):

            try:
                os.startfile(self.project["folder"])
            except Exception as e:
                messagebox.showerror(
                    "Error",
                    str(e)
                )


    # =====================================

        def load_project(self):

            self.title_entry.delete(0, "end")
            self.title_entry.insert(
                0,
                self.project["title"]
            )

            self.category.set(
                self.project["category"]
            )

            self.status.set(
                self.project["status"]
            )

            self.script.delete(
                "1.0",
                "end"
            )

            self.script.insert(
                "1.0",
                self.project["script"]
            )

            self.description.delete(
                "1.0",
                "end"
            )

            self.description.insert(
                "1.0",
                self.project["description"]
            )

            self.pinned_comment.delete(
                "1.0",
                "end"
            )

            self.pinned_comment.insert(
                "1.0",
                self.project["pinned_comment"]
            )

            self.notes.delete(
                "1.0",
                "end"
            )

            self.notes.insert(
                "1.0",
                self.project["notes"]
            )
        
        # =====================================

    # =====================================

        def save_project(self):

            try:

                self.pm.db.update_project(

                    self.project_id,

                    self.title_entry.get().strip(),

                    self.category.get(),

                    self.status.get(),

                    self.script.get("1.0", "end").strip(),

                    self.description.get("1.0", "end").strip(),

                    self.pinned_comment.get("1.0", "end").strip(),

                    self.notes.get("1.0", "end").strip()

                )

                messagebox.showinfo(
                    "Saved",
                    "Project saved successfully."
                )

                self.app.show_projects()

            except Exception as e:

                messagebox.showerror(
                    "Error",
                    str(e)
                )


        # =====================================
        # Script
        # =====================================

        ctk.CTkLabel(
            self.form,
            text="Script",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", padx=15, pady=(25,5))

        self.script = ctk.CTkTextbox(
            self.form,
            width=900,
            height=250
        )

        self.script.pack(
            fill="x",
            padx=15
        )

        # =====================================
        # Description
        # =====================================

        ctk.CTkLabel(
            self.form,
            text="Description",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", padx=15, pady=(25,5))

        self.description = ctk.CTkTextbox(
            self.form,
            width=900,
            height=120
        )

        self.description.pack(
            fill="x",
            padx=15
        )

        # =====================================
        # Pinned Comment
        # =====================================

        ctk.CTkLabel(
            self.form,
            text="Pinned Comment",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", padx=15, pady=(25,5))

        self.pinned_comment = ctk.CTkTextbox(
            self.form,
            width=900,
            height=120
        )

        self.pinned_comment.pack(
            fill="x",
            padx=15
        )

        # =====================================
        # Notes
        # =====================================

        ctk.CTkLabel(
            self.form,
            text="Notes",
            font=("Segoe UI", 18, "bold")
        ).pack(anchor="w", padx=15, pady=(25,5))

        self.notes = ctk.CTkTextbox(
            self.form,
            width=900,
            height=150
        )

        self.notes.pack(
            fill="x",
            padx=15,
            pady=(0,20)
        )

        # =====================================
        # Status
        # =====================================

        self.status_label = ctk.CTkLabel(
            self.form,
            text="Status"
        )

        self.status_label.pack(
            anchor="w",
            padx=15,
            pady=(20,5)
        )

        self.status = ctk.CTkOptionMenu(
            self.form,
            values=[
                "In Progress",
                "Completed",
                "Scheduled"
            ],
            width=220
        )

        self.status.pack(
            anchor="w",
            padx=15
        )

        # =====================================
        # Buttons
        # =====================================

        buttons = ctk.CTkFrame(
            self.form,
            fg_color="transparent"
        )

        buttons.pack(
            anchor="w",
            padx=15,
            pady=25
        )

        self.open_folder_btn = ctk.CTkButton(
            buttons,
            text="📂 Open Folder",
            command=self.open_folder
        )

        self.open_folder_btn.pack(
            side="left",
            padx=(0,10)
        )

        self.save_btn = ctk.CTkButton(
            buttons,
            text="💾 Save",
            command=self.save_project
        )

        self.save_btn.pack(
            side="left",
            padx=(0,10)
        )

        self.cancel_btn = ctk.CTkButton(
            buttons,
            text="← Back",
            command=self.app.show_projects
        )

        self.cancel_btn.pack(
            side="left"
        )
