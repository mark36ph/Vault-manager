import customtkinter as ctk
from tkinter import simpledialog, messagebox
from pathlib import Path
import shutil
from dialogs.input_dialog import InputDialog
import os
from pages.base_page import BasePage


class TemplatesPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Templates")

        self.app = app

        self.build()

    def build(self):

        ctk.CTkLabel(
            self.content,
            text="Template Manager",
            font=("Segoe UI", 28, "bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(20,10)
        )

        top = ctk.CTkFrame(
            self.content,
            fg_color="transparent"
        )

        top.pack(
            fill="x",
            padx=20,
            pady=(0,10)
        )

        ctk.CTkButton(
            top,
            text="➕ New Template",
            command=self.new_template
        ).pack(side="left")

        self.template_list = ctk.CTkScrollableFrame(
            self.content
        )

        self.template_list.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=10
        )

        self.load_templates()

    def load_templates(self):

        import os
        from pathlib import Path

        for widget in self.template_list.winfo_children():
            widget.destroy()

        templates = self.pm.get_templates()

        for template in templates:

            folder = Path("templates") / template

            files = len(
                [
                    f for f in folder.iterdir()
                    if f.is_file()
                ]
            ) if folder.exists() else 0

            card = ctk.CTkFrame(
                self.template_list,
                corner_radius=10
            )

            card.pack(
                fill="x",
                padx=10,
                pady=8
            )

            ctk.CTkLabel(
                card,
                text=f"📁 {template}",
                font=("Segoe UI",22,"bold")
            ).pack(
                anchor="w",
                padx=20,
                pady=(15,5)
            )

            file_names = sorted(
                [
                    f.name
                    for f in folder.iterdir()
                    if f.is_file()
                ],
                key=str.lower
            )

            preview = "\n".join(file_names[:5])

            if len(file_names) > 5:
                preview += "\n..."

            ctk.CTkLabel(
                card,
                text=f"{files} file{'s' if files != 1 else ''}",
                text_color="gray"
            ).pack(
                anchor="w",
                padx=20
            )

            ctk.CTkLabel(
                card,
                text=preview,
                justify="left",
                text_color="gray70",
                font=("Segoe UI", 12)
            ).pack(
                anchor="w",
                padx=20,
                pady=(5, 10)
            )

            buttons = ctk.CTkFrame(
                card,
                fg_color="transparent"
            )

            buttons.pack(
                fill="x",
                padx=15,
                pady=15
            )

            ctk.CTkButton(
                buttons,
                text="📂 Open",
                width=90,
                command=lambda t=template: self.open_template(t)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="✏ Rename",
                width=90,
                command=lambda t=template: self.rename_template(t)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="✏ Edit",
                width=90,
                command=lambda t=template: self.app.show_edit_template(t)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="📄 Duplicate",
                width=110,
                command=lambda t=template: self.duplicate_template(t)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="🗑 Delete",
                width=90,
                fg_color="#B22222",
                hover_color="#8B0000",
                command=lambda t=template: self.delete_template(t)
            ).pack(side="right", padx=5)
            
    def open_template(self, template):

        folder = Path("templates") / template

        if not folder.exists():

            messagebox.showerror(
                "Error",
                "Template folder not found."
            )

            return

        os.startfile(folder)

    def edit_template(self, template):
        print("Edit:", template)

    def duplicate_template(self, template):

        source = Path("templates") / template

        if not source.exists():
            messagebox.showerror(
                "Error",
                "Template not found."
            )
            return

        dialog = InputDialog(
            self,
            "Duplicate Template",
            "Enter the new template name:"
        )

        self.wait_window(dialog)

        new_name = dialog.result

        if not new_name:
            return

        destination = Path("templates") / new_name

        if destination.exists():

            messagebox.showerror(
                "Error",
                "A template with that name already exists."
            )

            return

        shutil.copytree(source, destination)

        self.load_templates()

    def delete_template(self, template):

        folder = Path("templates") / template

        answer = messagebox.askyesno(
            "Delete Template",
            f"Delete '{template}'?"
        )

        if not answer:
            return

        shutil.rmtree(folder)

        self.load_templates()

    def new_template(self):

        dialog = InputDialog(
            self,
            "New Template",
            "Enter the template name"
        )

        self.wait_window(dialog)

        name = dialog.result

        if not name:
            return

        folder = Path("templates") / name

        if folder.exists():

            messagebox.showerror(
                "Exists",
                "A template with that name already exists."
            )

            return

        folder.mkdir(parents=True)

        defaults = {

            "Script.txt": """HOOK

    INTRO

    FACT 1

    FACT 2

    OUTRO
    """,

            "Description.txt": """Write your description here...
    """,

            "Notes.txt": """Ideas

    Research

    Checklist
    """,

            "Pinned Comment.txt": """Thanks for watching!
    """
        }

        for filename, content in defaults.items():

            (folder / filename).write_text(
                content,
                encoding="utf-8"
            )

        self.load_templates()

    def rename_template(self, template):

        source = Path("templates") / template

        dialog = InputDialog(
            self,
            "Rename Template",
            "Enter the new template name:"
        )

        self.wait_window(dialog)

        new_name = dialog.result

        if not new_name:
            return

        destination = Path("templates") / new_name

        if destination.exists():

            messagebox.showerror(
                "Error",
                "That template already exists."
            )

            return

        source.rename(destination)

        self.load_templates()