import os
from pathlib import Path
from tkinter import messagebox

import customtkinter as ctk

from pages.base_page import BasePage


class EditTemplatePage(BasePage):

    def __init__(self, parent, pm, app, template_name):
        super().__init__(parent, pm, "Edit Template")

        self.app = app
        self.pm = pm

        self.template_name = template_name
        self.folder = Path("templates") / template_name

        self.textboxes = {}

        self.build()

    # ======================================================

    def build(self):

        # Header

        header = ctk.CTkFrame(self.content)
        header.pack(fill="x", padx=15, pady=15)

        ctk.CTkLabel(
            header,
            text=f"📁 {self.template_name}",
            font=("Segoe UI", 28, "bold")
        ).pack(anchor="w", padx=15, pady=(15, 5))

        ctk.CTkLabel(
            header,
            text=str(self.folder),
            text_color="gray"
        ).pack(anchor="w", padx=15, pady=(0, 15))

        # Buttons

        buttons = ctk.CTkFrame(self.content)
        buttons.pack(fill="x", padx=15)

        ctk.CTkButton(
            buttons,
            text="💾 Save",
            command=self.save_template
        ).pack(side="left", padx=5)

        ctk.CTkButton(
            buttons,
            text="📂 Open Folder",
            command=self.open_folder
        ).pack(side="left", padx=5)

        ctk.CTkButton(
            buttons,
            text="⬅ Back",
            command=self.app.show_templates
        ).pack(side="right", padx=5)

        # Editor

        self.scroll = ctk.CTkScrollableFrame(self.content)

        self.scroll.pack(
            fill="both",
            expand=True,
            padx=15,
            pady=15
        )

        self.load_files()

    # ======================================================

    def load_files(self):

        self.textboxes.clear()

        if not self.folder.exists():
            return

        files = sorted(
            self.folder.glob("*.txt"),
            key=lambda p: p.name.lower()
        )

        for file in files:

            card = ctk.CTkFrame(self.scroll)

            card.pack(
                fill="x",
                pady=10
            )

            ctk.CTkLabel(
                card,
                text=file.name,
                font=("Segoe UI", 18, "bold")
            ).pack(
                anchor="w",
                padx=15,
                pady=(10, 5)
            )

            textbox = ctk.CTkTextbox(
                card,
                height=220
            )

            textbox.pack(
                fill="x",
                padx=15,
                pady=(0, 15)
            )

            try:

                textbox.insert(
                    "1.0",
                    file.read_text(
                        encoding="utf-8"
                    )
                )

            except Exception:
                pass

            self.textboxes[file.name] = textbox

    # ======================================================

    def save_template(self):

        try:

            for filename, textbox in self.textboxes.items():

                path = self.folder / filename

                path.write_text(
                    textbox.get("1.0", "end").rstrip(),
                    encoding="utf-8"
                )

            messagebox.showinfo(
                "Saved",
                "Template saved successfully."
            )

        except Exception as e:

            messagebox.showerror(
                "Error",
                str(e)
            )

    # ======================================================

    def open_folder(self):

        if self.folder.exists():

            os.startfile(self.folder)

        else:

            messagebox.showerror(
                "Error",
                "Template folder not found."
            )