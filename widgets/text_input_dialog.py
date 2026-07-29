from tkinter import TclError, messagebox

import customtkinter as ctk


# CustomTkinter can leave a deleted Tk image name attached to a CTkLabel when
# an image reference is released immediately before the label is reconfigured.
# Recover by clearing that stale native image and retrying the requested update.
_original_label_configure = ctk.CTkLabel.configure
_original_askyesno = messagebox.askyesno


def _safe_label_configure(self, require_redraw=False, **kwargs):
    try:
        return _original_label_configure(
            self,
            require_redraw=require_redraw,
            **kwargs,
        )
    except TclError as exc:
        if "image" not in str(exc) or "doesn't exist" not in str(exc):
            raise
        self._label.configure(image="")
        self._image = None
        return _original_label_configure(
            self,
            require_redraw=require_redraw,
            **kwargs,
        )


if ctk.CTkLabel.configure is not _safe_label_configure:
    ctk.CTkLabel.configure = _safe_label_configure


class _CentredModal(ctk.CTkToplevel):
    """Shared behaviour for themed modal dialogs."""

    def _centre_over_parent(self, parent, width):
        owner = parent.winfo_toplevel()
        owner.update_idletasks()
        self.update_idletasks()
        x = owner.winfo_rootx() + max(0, (owner.winfo_width() - width) // 2)
        y = owner.winfo_rooty() + max(0, (owner.winfo_height() - self.winfo_height()) // 2)
        self.geometry(f"+{x}+{y}")


class TextInputDialog(_CentredModal):
    """A themed modal text-input dialog for FactVaultManager."""

    def __init__(
        self,
        parent,
        *,
        title,
        prompt,
        initial_value="",
        confirm_text="Save",
        helper_text="",
        validator=None,
        width=560,
    ):
        super().__init__(parent)
        self.result = None
        self.validator = validator
        self._width = max(460, int(width))

        self.title(title)
        self.resizable(False, False)
        self.transient(parent.winfo_toplevel())
        self.protocol("WM_DELETE_WINDOW", self.cancel)

        container = ctk.CTkFrame(self, corner_radius=14)
        container.pack(fill="both", expand=True, padx=18, pady=18)

        ctk.CTkLabel(
            container,
            text=title,
            font=("Segoe UI", 22, "bold"),
            anchor="w",
        ).pack(fill="x", padx=22, pady=(22, 8))

        ctk.CTkLabel(
            container,
            text=prompt,
            font=("Segoe UI", 14),
            anchor="w",
        ).pack(fill="x", padx=22, pady=(4, 8))

        self.entry = ctk.CTkEntry(container, height=42)
        self.entry.pack(fill="x", padx=22, pady=(0, 6))
        self.entry.insert(0, str(initial_value or ""))

        self.helper_label = ctk.CTkLabel(
            container,
            text=helper_text,
            text_color="gray",
            anchor="w",
            justify="left",
            wraplength=self._width - 90,
        )
        self.helper_label.pack(fill="x", padx=22, pady=(0, 4))

        self.error_label = ctk.CTkLabel(
            container,
            text="",
            text_color=("#B91C1C", "#FCA5A5"),
            anchor="w",
            justify="left",
            wraplength=self._width - 90,
        )
        self.error_label.pack(fill="x", padx=22, pady=(0, 10))

        buttons = ctk.CTkFrame(container, fg_color="transparent")
        buttons.pack(fill="x", padx=22, pady=(4, 22))

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=110,
            fg_color="transparent",
            border_width=1,
            command=self.cancel,
        ).pack(side="right", padx=(8, 0))

        ctk.CTkButton(
            buttons,
            text=confirm_text,
            width=120,
            command=self.confirm,
        ).pack(side="right")

        self.bind("<Return>", lambda _event: self.confirm())
        self.bind("<Escape>", lambda _event: self.cancel())
        self.entry.bind("<KeyRelease>", lambda _event: self.error_label.configure(text=""))

        self.update_idletasks()
        height = max(290, self.winfo_reqheight())
        self.geometry(f"{self._width}x{height}")
        self._centre_over_parent(parent, self._width)

        self.grab_set()
        self.after(50, self._focus_entry)

    def _focus_entry(self):
        if not self.winfo_exists():
            return
        self.entry.focus_set()
        self.entry.select_range(0, "end")
        self.entry.icursor("end")

    def confirm(self):
        value = self.entry.get().strip()
        if self.validator is not None:
            try:
                error = self.validator(value)
            except Exception as exc:
                error = str(exc)
            if error:
                self.error_label.configure(text=str(error))
                self.entry.focus_set()
                return
        self.result = value
        self.destroy()

    def cancel(self):
        self.result = None
        self.destroy()

    def show(self):
        self.wait_window()
        return self.result


class ConfirmationDialog(_CentredModal):
    """A themed yes/no confirmation dialog with an optional danger action."""

    def __init__(
        self,
        parent,
        *,
        title,
        message,
        confirm_text="Confirm",
        cancel_text="Cancel",
        danger=False,
        width=560,
    ):
        super().__init__(parent)
        self.result = False
        self._width = max(460, int(width))

        self.title(title)
        self.resizable(False, False)
        self.transient(parent.winfo_toplevel())
        self.protocol("WM_DELETE_WINDOW", self.cancel)

        container = ctk.CTkFrame(self, corner_radius=14)
        container.pack(fill="both", expand=True, padx=18, pady=18)

        title_colour = ("#B91C1C", "#FCA5A5") if danger else None
        ctk.CTkLabel(
            container,
            text=title,
            font=("Segoe UI", 22, "bold"),
            text_color=title_colour,
            anchor="w",
        ).pack(fill="x", padx=22, pady=(22, 10))

        ctk.CTkLabel(
            container,
            text=message,
            font=("Segoe UI", 14),
            anchor="w",
            justify="left",
            wraplength=self._width - 90,
        ).pack(fill="x", padx=22, pady=(4, 18))

        buttons = ctk.CTkFrame(container, fg_color="transparent")
        buttons.pack(fill="x", padx=22, pady=(4, 22))

        cancel_button = ctk.CTkButton(
            buttons,
            text=cancel_text,
            width=110,
            fg_color="transparent",
            border_width=1,
            command=self.cancel,
        )
        cancel_button.pack(side="right", padx=(8, 0))

        confirm_options = {}
        if danger:
            confirm_options.update(
                fg_color=("#DC2626", "#991B1B"),
                hover_color=("#B91C1C", "#7F1D1D"),
            )
        ctk.CTkButton(
            buttons,
            text=confirm_text,
            width=120,
            command=self.confirm,
            **confirm_options,
        ).pack(side="right")

        self.bind("<Return>", lambda _event: self.confirm())
        self.bind("<Escape>", lambda _event: self.cancel())

        self.update_idletasks()
        height = max(250, self.winfo_reqheight())
        self.geometry(f"{self._width}x{height}")
        self._centre_over_parent(parent, self._width)
        self.grab_set()
        self.after(50, cancel_button.focus_set)

    def confirm(self):
        self.result = True
        self.destroy()

    def cancel(self):
        self.result = False
        self.destroy()

    def show(self):
        self.wait_window()
        return self.result


def ask_text(parent, **kwargs):
    """Display a themed text input dialog and return the entered value."""
    return TextInputDialog(parent, **kwargs).show()


def ask_confirmation(parent, **kwargs):
    """Display a themed confirmation dialog and return True or False."""
    return ConfirmationDialog(parent, **kwargs).show()


def _themed_askyesno(title=None, message=None, **options):
    """Use the themed modal for Tk yes/no prompts when a parent is available."""
    parent = options.get("parent")
    if parent is None or not hasattr(parent, "winfo_toplevel"):
        return _original_askyesno(title=title, message=message, **options)
    return ask_confirmation(
        parent,
        title=str(title or "Confirm"),
        message=str(message or "Are you sure?"),
        confirm_text="Delete" if "delete" in str(title or "").lower() else "Yes",
        cancel_text="Cancel" if "delete" in str(title or "").lower() else "No",
        danger="delete" in str(title or "").lower(),
    )


if messagebox.askyesno is not _themed_askyesno:
    messagebox.askyesno = _themed_askyesno


__all__ = [
    "ConfirmationDialog",
    "TextInputDialog",
    "ask_confirmation",
    "ask_text",
]
