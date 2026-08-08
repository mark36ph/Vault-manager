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

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))
        self.build()

    def build(self):
        subtitle = ctk.CTkLabel(
            self,
            text="Edit the text files used when creating projects from this template.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)

        toolbar = ctk.CTkFrame(self.content, fg_color="transparent")
        toolbar.pack(fill="x", pady=(0, 10))

        info = ctk.CTkFrame(toolbar, fg_color="transparent")
        info.pack(side="left", fill="x", expand=True)
        ctk.CTkLabel(
            info,
            text=self.template_name,
            font=("Segoe UI", 17, "bold"),
            anchor="w",
        ).pack(fill="x")
        ctk.CTkLabel(
            info,
            text=str(self.folder),
            font=("Segoe UI", 11),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        ).pack(fill="x", pady=(2, 0))

        ctk.CTkButton(
            toolbar,
            text="Back",
            width=78,
            height=34,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.app.show_templates,
        ).pack(side="right", padx=(6, 0))
        ctk.CTkButton(
            toolbar,
            text="Open Folder",
            width=102,
            height=34,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.open_folder,
        ).pack(side="right", padx=(6, 0))
        ctk.CTkButton(
            toolbar,
            text="Save",
            width=84,
            height=34,
            command=self.save_template,
        ).pack(side="right")

        self.scroll = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent",
        )
        self.scroll.pack(fill="both", expand=True)
        self.load_files()

    def load_files(self):
        self.textboxes.clear()

        if not self.folder.exists():
            ctk.CTkLabel(
                self.scroll,
                text="Template folder not found.",
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=4, pady=20)
            return

        files = sorted(self.folder.glob("*.txt"), key=lambda p: p.name.lower())
        if not files:
            ctk.CTkLabel(
                self.scroll,
                text="No text files in this template yet.",
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=4, pady=20)
            return

        for file in files:
            card = ctk.CTkFrame(
                self.scroll,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            card.pack(fill="x", pady=(0, 10))

            ctk.CTkLabel(
                card,
                text=file.name,
                font=("Segoe UI", 14, "bold"),
            ).pack(anchor="w", padx=14, pady=(12, 6))

            textbox = ctk.CTkTextbox(
                card,
                height=190,
                font=("Segoe UI", 13),
                corner_radius=6,
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            textbox.pack(fill="x", padx=14, pady=(0, 14))

            try:
                textbox.insert("1.0", file.read_text(encoding="utf-8"))
            except Exception:
                pass

            self.textboxes[file.name] = textbox

    def save_template(self):
        try:
            for filename, textbox in self.textboxes.items():
                path = self.folder / filename
                path.write_text(
                    textbox.get("1.0", "end").rstrip(),
                    encoding="utf-8",
                )
            messagebox.showinfo("Saved", "Template saved successfully.")
        except Exception as error:
            messagebox.showerror("Error", str(error))

    def open_folder(self):
        if self.folder.exists():
            os.startfile(self.folder)
        else:
            messagebox.showerror("Error", "Template folder not found.")
