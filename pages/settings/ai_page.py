import customtkinter as ctk

from common.settings_manager import SettingsManager
from widgets.message_dialog import show_message


class AIPage(ctk.CTkFrame):
    """Configure the OpenAI provider used by production."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, fg_color="transparent")
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="AI",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Configure OpenAI for research, scripts, visual prompts, and narration.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=4, pady=(0, 16))

        provider_card = self._section("Provider")
        self.provider = ctk.StringVar(value="OpenAI")
        ctk.CTkOptionMenu(
            provider_card,
            variable=self.provider,
            values=["OpenAI"],
            width=180,
            height=34,
        ).pack(anchor="w", padx=14, pady=(6, 14))

        credentials = self._section("Credentials")
        ctk.CTkLabel(
            credentials,
            text="OpenAI API key",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(8, 5))

        self.api_key_entry = ctk.CTkEntry(
            credentials,
            show="●",
            height=36,
            placeholder_text="Enter OpenAI API key",
        )
        self.api_key_entry.pack(fill="x", padx=14, pady=(0, 8))
        saved_key = self.settings.get("ai", "api_key", "")
        if saved_key:
            self.api_key_entry.insert(0, saved_key)

        self.show_key = ctk.BooleanVar(value=False)
        ctk.CTkCheckBox(
            credentials,
            text="Show API key",
            variable=self.show_key,
            command=lambda: self.api_key_entry.configure(
                show="" if self.show_key.get() else "●"
            ),
            font=("Segoe UI", 13),
        ).pack(anchor="w", padx=14, pady=(0, 14))

        model_card = self._section("Model")
        ctk.CTkLabel(
            model_card,
            text="Text model",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(8, 5))

        self.model_entry = ctk.CTkEntry(
            model_card,
            height=36,
            placeholder_text="gpt-5-mini",
        )
        self.model_entry.pack(fill="x", padx=14, pady=(0, 6))
        self.model_entry.insert(
            0,
            str(self.settings.get("ai", "model", "") or "gpt-5-mini"),
        )

        ctk.CTkLabel(
            model_card,
            text="Used by the production pipeline for text generation tasks.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=14, pady=(0, 14))

        footer = ctk.CTkFrame(self, fg_color="transparent")
        footer.pack(fill="x", padx=4, pady=(2, 0))

        self.status_label = ctk.CTkLabel(
            footer,
            text="",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        )
        self.status_label.pack(side="left")

        ctk.CTkButton(
            footer,
            text="Save changes",
            width=126,
            height=36,
            corner_radius=7,
            command=self.save_settings,
        ).pack(side="right")

    def _section(self, title):
        frame = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        frame.pack(fill="x", padx=4, pady=(0, 10))
        ctk.CTkLabel(
            frame,
            text=title,
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 2))
        return frame

    def save_settings(self):
        api_key = self.api_key_entry.get().strip()
        model = self.model_entry.get().strip() or "gpt-5-mini"
        self.settings.update_section(
            "ai",
            {"provider": "OpenAI", "api_key": api_key, "model": model},
        )
        self.status_label.configure(text="AI settings saved.")
        show_message(
            self,
            "AI settings saved",
            "AI settings were saved successfully.",
            kind="success",
        )
