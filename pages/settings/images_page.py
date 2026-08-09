import threading

import customtkinter as ctk

from common.settings_manager import SettingsManager
from image_search import ImageSearchError, search_images
from widgets.message_dialog import show_message


class ImagesPage(ctk.CTkFrame):
    VALID_PROVIDERS = {"Pixabay", "Pexels"}
    VALID_ORIENTATIONS = {"vertical", "horizontal", "all"}

    def __init__(self, parent, pm, app):
        super().__init__(parent, fg_color="transparent")
        self.pm = pm
        self.app = app
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="Images",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Configure image-search providers, API keys, and the default result orientation.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=4, pady=(0, 16))

        provider_card = self._section("Search provider")
        saved_provider = self.settings.get("images", "provider", "Pixabay")
        if saved_provider not in self.VALID_PROVIDERS:
            saved_provider = "Pixabay"

        self.provider = ctk.StringVar(value=saved_provider)
        self.provider_menu = ctk.CTkOptionMenu(
            provider_card,
            variable=self.provider,
            values=["Pixabay", "Pexels"],
            command=self.provider_changed,
            width=180,
            height=34,
        )
        self.provider_menu.pack(anchor="w", padx=14, pady=(6, 14))

        keys_card = self._section("API keys")
        ctk.CTkLabel(
            keys_card,
            text="Pixabay",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(8, 5))

        self.pixabay_key_entry = ctk.CTkEntry(
            keys_card,
            show="●",
            height=36,
            placeholder_text="Enter Pixabay API key",
        )
        self.pixabay_key_entry.pack(fill="x", padx=14, pady=(0, 10))
        saved_pixabay_key = self.settings.get("images", "pixabay_api_key", "")
        if saved_pixabay_key:
            self.pixabay_key_entry.insert(0, saved_pixabay_key)

        ctk.CTkLabel(
            keys_card,
            text="Pexels",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(2, 5))

        self.pexels_key_entry = ctk.CTkEntry(
            keys_card,
            show="●",
            height=36,
            placeholder_text="Enter Pexels API key",
        )
        self.pexels_key_entry.pack(fill="x", padx=14, pady=(0, 8))
        saved_pexels_key = self.settings.get("images", "pexels_api_key", "")
        if saved_pexels_key:
            self.pexels_key_entry.insert(0, saved_pexels_key)

        self.show_keys = ctk.BooleanVar(value=False)
        self.show_keys_checkbox = ctk.CTkCheckBox(
            keys_card,
            text="Show API keys",
            variable=self.show_keys,
            command=self.toggle_api_key_visibility,
            font=("Segoe UI", 13),
        )
        self.show_keys_checkbox.pack(anchor="w", padx=14, pady=(0, 14))

        defaults_card = self._section("Defaults")
        saved_orientation = self.settings.get(
            "images", "default_orientation", "vertical"
        )
        if saved_orientation not in self.VALID_ORIENTATIONS:
            saved_orientation = "vertical"

        self.orientation = ctk.StringVar(value=saved_orientation)
        ctk.CTkLabel(
            defaults_card,
            text="Orientation",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(8, 5))

        self.orientation_menu = ctk.CTkOptionMenu(
            defaults_card,
            variable=self.orientation,
            values=["vertical", "horizontal", "all"],
            width=180,
            height=34,
        )
        self.orientation_menu.pack(anchor="w", padx=14, pady=(0, 14))

        footer = ctk.CTkFrame(self, fg_color="transparent")
        footer.pack(fill="x", padx=4, pady=(2, 0))

        self.status_label = ctk.CTkLabel(
            footer,
            text="",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        )
        self.status_label.pack(side="left")

        self.save_button = ctk.CTkButton(
            footer,
            text="Save changes",
            width=126,
            height=36,
            corner_radius=7,
            command=self.save_settings,
        )
        self.save_button.pack(side="right")

        self.test_button = ctk.CTkButton(
            footer,
            text="Test connection",
            width=126,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.start_connection_test,
        )
        self.test_button.pack(side="right", padx=(0, 8))

        self.provider_changed(self.provider.get())

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

    def provider_changed(self, provider):
        provider = str(provider or "").strip()
        self.status_label.configure(text=f"Selected provider: {provider}")

    def toggle_api_key_visibility(self):
        show_value = "" if self.show_keys.get() else "●"
        self.pixabay_key_entry.configure(show=show_value)
        self.pexels_key_entry.configure(show=show_value)

    def save_settings(self, *, show_dialog=True):
        provider = self.provider.get().strip()
        orientation = self.orientation.get().strip().lower()
        pixabay_key = self.pixabay_key_entry.get().strip()
        pexels_key = self.pexels_key_entry.get().strip()

        if provider not in self.VALID_PROVIDERS:
            show_message(
                self,
                "Image settings",
                "Select a valid image provider.",
                kind="warning",
            )
            return False

        if orientation not in self.VALID_ORIENTATIONS:
            show_message(
                self,
                "Image settings",
                "Select a valid default orientation.",
                kind="warning",
            )
            return False

        self.settings.update_section(
            "images",
            {
                "provider": provider,
                "pixabay_api_key": pixabay_key,
                "pexels_api_key": pexels_key,
                "default_orientation": orientation,
            },
        )
        self.status_label.configure(text="Image settings saved.")

        if show_dialog:
            show_message(
                self,
                "Image settings saved",
                "Image settings were saved successfully.",
                kind="success",
            )
        return True

    def start_connection_test(self):
        provider = self.provider.get().strip()
        if provider == "Pixabay":
            api_key = self.pixabay_key_entry.get().strip()
        elif provider == "Pexels":
            api_key = self.pexels_key_entry.get().strip()
        else:
            show_message(
                self,
                "Image settings",
                "Select a valid image provider.",
                kind="warning",
            )
            return

        if not api_key:
            show_message(
                self,
                provider,
                f"Enter a {provider} API key before testing.",
                kind="warning",
            )
            return

        if not self.save_settings(show_dialog=False):
            return

        self.test_button.configure(state="disabled", text="Testing...")
        self.save_button.configure(state="disabled")
        self.status_label.configure(text=f"Testing the {provider} connection...")

        threading.Thread(
            target=self.perform_connection_test,
            args=(provider,),
            daemon=True,
        ).start()

    def perform_connection_test(self, provider):
        try:
            results = search_images(
                provider_name=provider,
                settings=self.settings,
                query="nature",
                page=1,
                per_page=3,
                orientation="all",
            )
        except (ValueError, ImageSearchError) as exc:
            self.after(
                0,
                lambda message=str(exc): self.connection_test_failed(
                    provider, message
                ),
            )
            return
        except Exception as exc:
            self.after(
                0,
                lambda message=str(exc): self.connection_test_failed(
                    provider, f"Connection test failed: {message}"
                ),
            )
            return

        self.after(
            0,
            lambda: self.connection_test_succeeded(provider, len(results)),
        )

    def connection_test_succeeded(self, provider, result_count):
        self.test_button.configure(state="normal", text="Test connection")
        self.save_button.configure(state="normal")
        self.status_label.configure(text=f"{provider} connection successful.")
        show_message(
            self,
            f"{provider} connection successful",
            (
                f"The {provider} connection was successful.\n\n"
                f"Test results returned: {result_count}"
            ),
            kind="success",
        )

    def connection_test_failed(self, provider, message):
        self.test_button.configure(state="normal", text="Test connection")
        self.save_button.configure(state="normal")
        self.status_label.configure(text=f"{provider} connection failed.")
        show_message(
            self,
            f"{provider} connection failed",
            message,
            kind="error",
        )
