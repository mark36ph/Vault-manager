import customtkinter as ctk
from tkinter import messagebox

from common.settings_manager import SettingsManager


class AIPage(ctk.CTkFrame):
    """Configure the OpenAI provider used by production."""

    def __init__(self, parent, pm, app):
        super().__init__(parent)
        self.pm = pm
        self.app = app
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(self, text="AI Settings", font=("Segoe UI", 28, "bold")).pack(
            anchor="w", padx=20, pady=(20, 5)
        )
        ctk.CTkLabel(
            self,
            text="Configure OpenAI for research, scripts, visual prompts, and narration.",
            text_color="gray",
        ).pack(anchor="w", padx=20, pady=(0, 20))

        container = ctk.CTkFrame(self)
        container.pack(fill="x", padx=20, pady=(0, 20))
        container.grid_columnconfigure(0, weight=1)

        ctk.CTkLabel(container, text="AI Provider", font=("Segoe UI", 16, "bold")).grid(
            row=0, column=0, padx=20, pady=(20, 8), sticky="w"
        )
        self.provider = ctk.StringVar(value="OpenAI")
        ctk.CTkOptionMenu(container, variable=self.provider, values=["OpenAI"]).grid(
            row=1, column=0, padx=20, pady=(0, 18), sticky="w"
        )

        ctk.CTkLabel(container, text="OpenAI API Key", font=("Segoe UI", 16, "bold")).grid(
            row=2, column=0, padx=20, pady=(0, 8), sticky="w"
        )
        self.api_key_entry = ctk.CTkEntry(
            container, show="●", placeholder_text="Enter your OpenAI API key"
        )
        self.api_key_entry.grid(row=3, column=0, padx=20, pady=(0, 8), sticky="ew")
        saved_key = self.settings.get("ai", "api_key", "")
        if saved_key:
            self.api_key_entry.insert(0, saved_key)

        self.show_key = ctk.BooleanVar(value=False)
        ctk.CTkCheckBox(
            container,
            text="Show API key",
            variable=self.show_key,
            command=lambda: self.api_key_entry.configure(show="" if self.show_key.get() else "●"),
        ).grid(row=4, column=0, padx=20, pady=(0, 18), sticky="w")

        ctk.CTkLabel(container, text="Text Model", font=("Segoe UI", 16, "bold")).grid(
            row=5, column=0, padx=20, pady=(0, 8), sticky="w"
        )
        self.model_entry = ctk.CTkEntry(container, placeholder_text="gpt-5-mini")
        self.model_entry.grid(row=6, column=0, padx=20, pady=(0, 8), sticky="ew")
        self.model_entry.insert(0, str(self.settings.get("ai", "model", "") or "gpt-5-mini"))

        self.status_label = ctk.CTkLabel(container, text="", text_color="gray")
        self.status_label.grid(row=7, column=0, padx=20, pady=(6, 8), sticky="w")

        ctk.CTkButton(
            container, text="💾 Save Changes", width=160, command=self.save_settings
        ).grid(row=8, column=0, padx=20, pady=(5, 20), sticky="e")

    def save_settings(self):
        api_key = self.api_key_entry.get().strip()
        model = self.model_entry.get().strip() or "gpt-5-mini"
        self.settings.update_section(
            "ai", {"provider": "OpenAI", "api_key": api_key, "model": model}
        )
        self.status_label.configure(text="AI settings saved.")
        messagebox.showinfo("AI Settings", "AI settings saved successfully.", parent=self)
