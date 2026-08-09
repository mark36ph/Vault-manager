import os
import shutil
from pathlib import Path

import customtkinter as ctk

from dialogs.input_dialog import InputDialog
from pages.base_page import BasePage
from widgets.message_dialog import ask_confirmation, show_message


INVALID_TEMPLATE_CHARS = '<>:"/\\|?*'


class TemplatesPage(BasePage):
    """Manage reusable project templates in a compact card layout."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Templates")
        self.app = app

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="Create and manage reusable project structures.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)
        self.content.pack_configure(padx=24, pady=(0, 20))

        self.build()

    def build(self):
        toolbar = ctk.CTkFrame(self.content, fg_color="transparent")
        toolbar.pack(fill="x", pady=(0, 10))

        self.count_label = ctk.CTkLabel(
            toolbar,
            text="0 templates",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        )
        self.count_label.pack(side="left")

        ctk.CTkButton(
            toolbar,
            text="+ New Template",
            width=120,
            height=36,
            corner_radius=7,
            command=self.new_template,
        ).pack(side="right")

        self.template_list = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent",
        )
        self.template_list.pack(fill="both", expand=True)

        self.load_templates()

    def load_templates(self):
        for widget in self.template_list.winfo_children():
            widget.destroy()

        try:
            templates = self.pm.get_templates()
        except Exception as error:
            self.count_label.configure(text="Templates unavailable")
            show_message(self, "Templates", str(error), kind="error")
            return

        count = len(templates)
        self.count_label.configure(text=f"{count} template{'s' if count != 1 else ''}")

        if not templates:
            empty = ctk.CTkFrame(
                self.template_list,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            empty.pack(fill="x", pady=4)
            ctk.CTkLabel(
                empty,
                text="No templates yet",
                font=("Segoe UI", 15, "bold"),
            ).pack(anchor="w", padx=16, pady=(16, 3))
            ctk.CTkLabel(
                empty,
                text="Create a template to reuse the same project file structure.",
                font=("Segoe UI", 12),
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=16, pady=(0, 16))
            return

        for template in templates:
            folder = Path("templates") / template
            try:
                file_names = sorted(
                    [f.name for f in folder.iterdir() if f.is_file()],
                    key=str.lower,
                ) if folder.exists() else []
            except OSError:
                file_names = []

            card = ctk.CTkFrame(
                self.template_list,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            card.pack(fill="x", pady=4)

            header = ctk.CTkFrame(card, fg_color="transparent")
            header.pack(fill="x", padx=14, pady=(12, 4))

            ctk.CTkLabel(
                header,
                text=template,
                font=("Segoe UI", 15, "bold"),
                anchor="w",
            ).pack(side="left", fill="x", expand=True)

            files = len(file_names)
            ctk.CTkLabel(
                header,
                text=f"{files} file{'s' if files != 1 else ''}",
                font=("Segoe UI", 11),
                text_color=("#667085", "#8F96A3"),
            ).pack(side="right")

            preview = "  •  ".join(file_names[:4]) or "Empty template"
            if len(file_names) > 4:
                preview += "  •  …"

            ctk.CTkLabel(
                card,
                text=preview,
                justify="left",
                anchor="w",
                font=("Segoe UI", 12),
                text_color=("#667085", "#8F96A3"),
            ).pack(fill="x", padx=14, pady=(0, 10))

            buttons = ctk.CTkFrame(card, fg_color="transparent")
            buttons.pack(fill="x", padx=14, pady=(0, 12))

            secondary = {
                "height": 32,
                "corner_radius": 6,
                "fg_color": "transparent",
                "border_width": 1,
                "border_color": ("#D0D5DD", "#3A404A"),
                "text_color": ("#344054", "#D0D5DD"),
                "hover_color": ("#F2F4F7", "#252A33"),
            }

            ctk.CTkButton(
                buttons,
                text="Open",
                width=70,
                command=lambda t=template: self.open_template(t),
                **secondary,
            ).pack(side="left", padx=(0, 5))

            ctk.CTkButton(
                buttons,
                text="Edit",
                width=70,
                command=lambda t=template: self.app.show_edit_template(t),
                **secondary,
            ).pack(side="left", padx=(0, 5))

            ctk.CTkButton(
                buttons,
                text="Rename",
                width=78,
                command=lambda t=template: self.rename_template(t),
                **secondary,
            ).pack(side="left", padx=(0, 5))

            ctk.CTkButton(
                buttons,
                text="Duplicate",
                width=86,
                command=lambda t=template: self.duplicate_template(t),
                **secondary,
            ).pack(side="left")

            ctk.CTkButton(
                buttons,
                text="Delete",
                width=72,
                height=32,
                corner_radius=6,
                fg_color="transparent",
                border_width=1,
                border_color=("#FDA29B", "#7A3030"),
                text_color=("#B42318", "#FDA29B"),
                hover_color=("#FEF3F2", "#3A2222"),
                command=lambda t=template: self.delete_template(t),
            ).pack(side="right")

    @staticmethod
    def _validate_template_name(name):
        name = str(name or "").strip()
        if not name:
            return "Please enter a template name."
        if any(char in name for char in INVALID_TEMPLATE_CHARS):
            return f"Template names cannot contain: {INVALID_TEMPLATE_CHARS}"
        if name.endswith(".") or name.endswith(" "):
            return "Template names cannot end with a space or period."
        return ""

    def _show_name_error(self, message):
        show_message(self, "Template Name", message, kind="error")

    def open_template(self, template):
        folder = Path("templates") / template
        if not folder.exists():
            show_message(self, "Templates", "Template folder not found.", kind="error")
            self.load_templates()
            return
        try:
            os.startfile(folder)
        except Exception as error:
            show_message(self, "Templates", str(error), kind="error")

    def duplicate_template(self, template):
        source = Path("templates") / template
        if not source.exists():
            show_message(self, "Templates", "Template not found.", kind="error")
            self.load_templates()
            return

        dialog = InputDialog(
            self,
            "Duplicate Template",
            "Enter the new template name:",
            initial_value=f"{template} Copy",
            button_text="Duplicate",
        )
        self.wait_window(dialog)
        new_name = dialog.result
        if not new_name:
            return

        error = self._validate_template_name(new_name)
        if error:
            self._show_name_error(error)
            return

        destination = Path("templates") / new_name
        if destination.exists():
            self._show_name_error("A template with that name already exists.")
            return

        try:
            shutil.copytree(source, destination)
        except Exception as error:
            show_message(self, "Duplicate Template", str(error), kind="error")
            return

        self.load_templates()

    def delete_template(self, template):
        folder = Path("templates") / template
        if not folder.exists():
            show_message(self, "Delete Template", "Template folder not found.", kind="error")
            self.load_templates()
            return
        if not ask_confirmation(
            self,
            "Delete Template",
            f"Delete '{template}'?\n\nThis cannot be undone.",
            confirm_text="Delete",
            kind="error",
        ):
            return
        try:
            shutil.rmtree(folder)
        except Exception as error:
            show_message(self, "Delete Template", str(error), kind="error")
            return
        self.load_templates()

    def new_template(self):
        dialog = InputDialog(
            self,
            "New Template",
            "Enter the template name:",
            button_text="Create",
        )
        self.wait_window(dialog)
        name = dialog.result
        if not name:
            return

        error = self._validate_template_name(name)
        if error:
            self._show_name_error(error)
            return

        folder = Path("templates") / name
        if folder.exists():
            self._show_name_error("A template with that name already exists.")
            return

        try:
            folder.mkdir(parents=True)
        except Exception as error:
            show_message(self, "New Template", str(error), kind="error")
            return
        self.load_templates()

    def rename_template(self, template):
        source = Path("templates") / template
        if not source.exists():
            show_message(self, "Rename Template", "Template folder not found.", kind="error")
            self.load_templates()
            return

        dialog = InputDialog(
            self,
            "Rename Template",
            "Enter the new template name:",
            initial_value=template,
            button_text="Rename",
        )
        self.wait_window(dialog)
        new_name = dialog.result
        if not new_name or new_name == template:
            return

        error = self._validate_template_name(new_name)
        if error:
            self._show_name_error(error)
            return

        destination = Path("templates") / new_name
        if destination.exists():
            self._show_name_error("That template already exists.")
            return

        try:
            source.rename(destination)
        except Exception as error:
            show_message(self, "Rename Template", str(error), kind="error")
            return
        self.load_templates()
