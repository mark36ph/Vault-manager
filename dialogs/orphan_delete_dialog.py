from pathlib import Path

import customtkinter as ctk


MUTED_TEXT = ("#667085", "#8F96A3")


class OrphanDeleteDialog(ctk.CTkToplevel):
    """Choose one orphan project folder for explicit permanent deletion."""

    def __init__(self, parent, orphan_issues):
        super().__init__(parent)
        self.result = None
        self.folder_by_label = {}

        self.title("Delete Orphan Folder")
        self.geometry("680x330")
        self.minsize(620, 310)
        self.resizable(True, False)
        self.transient(parent)
        self.grab_set()

        ctk.CTkLabel(
            self,
            text="Delete Orphan Folder",
            font=("Segoe UI", 20, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(24, 4))

        ctk.CTkLabel(
            self,
            text=(
                "Choose an orphan folder to permanently delete. This is only available "
                "for folders that are not linked to a database project."
            ),
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
            justify="left",
            anchor="w",
            wraplength=620,
        ).pack(fill="x", padx=28, pady=(0, 20))

        ctk.CTkLabel(
            self,
            text="Orphan folder",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(0, 6))

        labels = []
        for index, issue in enumerate(orphan_issues, start=1):
            folder = str(issue.get("folder") or "")
            path = Path(folder)
            label = f"{index}. {path.parent.name} / {path.name}"
            labels.append(label)
            self.folder_by_label[label] = folder

        self.folder_menu = ctk.CTkOptionMenu(
            self,
            values=labels,
            height=40,
            command=self._folder_changed,
        )
        self.folder_menu.pack(fill="x", padx=28)
        if labels:
            self.folder_menu.set(labels[0])

        self.path_label = ctk.CTkLabel(
            self,
            text=self.folder_by_label.get(labels[0], "") if labels else "",
            font=("Consolas", 10),
            text_color=MUTED_TEXT,
            anchor="w",
            justify="left",
            wraplength=620,
        )
        self.path_label.pack(fill="x", padx=28, pady=(7, 0))

        buttons = ctk.CTkFrame(self, fg_color="transparent")
        buttons.pack(side="bottom", fill="x", padx=28, pady=(18, 24))

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=112,
            height=38,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self._cancel,
        ).pack(side="right", padx=(10, 0))

        ctk.CTkButton(
            buttons,
            text="Choose folder",
            width=132,
            height=38,
            command=self._confirm,
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", self._cancel)
        self.bind("<Escape>", lambda _event: self._cancel())
        self.bind("<Return>", lambda _event: self._confirm())

    def _folder_changed(self, label):
        self.path_label.configure(text=self.folder_by_label.get(label, ""))

    def _confirm(self):
        folder = self.folder_by_label.get(self.folder_menu.get(), "")
        if not folder:
            return
        self.result = folder
        self.destroy()

    def _cancel(self):
        self.result = None
        self.destroy()


__all__ = ["OrphanDeleteDialog"]
