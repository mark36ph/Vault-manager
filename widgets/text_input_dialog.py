import customtkinter as ctk


class TextInputDialog(ctk.CTkToplevel):
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
        self._centre_over_parent(parent)

        self.grab_set()
        self.after(50, self._focus_entry)

    def _centre_over_parent(self, parent):
        owner = parent.winfo_toplevel()
        owner.update_idletasks()
        x = owner.winfo_rootx() + max(0, (owner.winfo_width() - self._width) // 2)
        y = owner.winfo_rooty() + max(0, (owner.winfo_height() - self.winfo_height()) // 2)
        self.geometry(f"+{x}+{y}")

    def _focus_entry(self):
        if not self.winfo_exists():
            return
        self.entry.focus_set()
        self.entry.selection_range(0, "end")
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


def ask_text(parent, **kwargs):
    """Display a themed text input dialog and return the entered value."""
    return TextInputDialog(parent, **kwargs).show()


__all__ = ["TextInputDialog", "ask_text"]
