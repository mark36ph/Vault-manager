import threading
from tkinter import messagebox

import customtkinter as ctk

from common.settings_manager import SettingsManager
from image_search import (
    ImageSearchError,
    search_images,
)


class ImagesPage(ctk.CTkFrame):

    VALID_PROVIDERS = {
        "Pixabay",
        "Pexels",
    }

    VALID_ORIENTATIONS = {
        "vertical",
        "horizontal",
        "all",
    }

    def __init__(self, parent, pm, app):
        super().__init__(parent)

        self.pm = pm
        self.app = app
        self.settings = SettingsManager()

        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="Image Settings",
            font=("Segoe UI", 28, "bold"),
        ).pack(
            anchor="w",
            padx=20,
            pady=(20, 5),
        )

        ctk.CTkLabel(
            self,
            text=(
                "Configure image-search providers and choose which "
                "provider the project image browser should use."
            ),
            text_color="gray",
        ).pack(
            anchor="w",
            padx=20,
            pady=(0, 20),
        )

        container = ctk.CTkFrame(self)
        container.pack(
            fill="x",
            padx=20,
            pady=(0, 20),
        )

        container.grid_columnconfigure(
            0,
            weight=1,
        )

        # ==========================================
        # Provider
        # ==========================================

        ctk.CTkLabel(
            container,
            text="Image Provider",
            font=("Segoe UI", 16, "bold"),
        ).grid(
            row=0,
            column=0,
            padx=20,
            pady=(20, 8),
            sticky="w",
        )

        saved_provider = self.settings.get(
            "images",
            "provider",
            "Pixabay",
        )

        if saved_provider not in self.VALID_PROVIDERS:
            saved_provider = "Pixabay"

        self.provider = ctk.StringVar(
            value=saved_provider,
        )

        self.provider_menu = ctk.CTkOptionMenu(
            container,
            variable=self.provider,
            values=[
                "Pixabay",
                "Pexels",
            ],
            command=self.provider_changed,
            width=220,
        )
        self.provider_menu.grid(
            row=1,
            column=0,
            padx=20,
            pady=(0, 20),
            sticky="w",
        )

        # ==========================================
        # Pixabay API key
        # ==========================================

        ctk.CTkLabel(
            container,
            text="Pixabay API Key",
            font=("Segoe UI", 16, "bold"),
        ).grid(
            row=2,
            column=0,
            padx=20,
            pady=(0, 8),
            sticky="w",
        )

        self.pixabay_key_entry = ctk.CTkEntry(
            container,
            show="●",
            placeholder_text="Enter your Pixabay API key",
        )
        self.pixabay_key_entry.grid(
            row=3,
            column=0,
            padx=20,
            pady=(0, 8),
            sticky="ew",
        )

        saved_pixabay_key = self.settings.get(
            "images",
            "pixabay_api_key",
            "",
        )

        if saved_pixabay_key:
            self.pixabay_key_entry.insert(
                0,
                saved_pixabay_key,
            )

        # ==========================================
        # Pexels API key
        # ==========================================

        ctk.CTkLabel(
            container,
            text="Pexels API Key",
            font=("Segoe UI", 16, "bold"),
        ).grid(
            row=4,
            column=0,
            padx=20,
            pady=(12, 8),
            sticky="w",
        )

        self.pexels_key_entry = ctk.CTkEntry(
            container,
            show="●",
            placeholder_text="Enter your Pexels API key",
        )
        self.pexels_key_entry.grid(
            row=5,
            column=0,
            padx=20,
            pady=(0, 8),
            sticky="ew",
        )

        saved_pexels_key = self.settings.get(
            "images",
            "pexels_api_key",
            "",
        )

        if saved_pexels_key:
            self.pexels_key_entry.insert(
                0,
                saved_pexels_key,
            )

        # ==========================================
        # Show API keys
        # ==========================================

        self.show_keys = ctk.BooleanVar(
            value=False,
        )

        self.show_keys_checkbox = ctk.CTkCheckBox(
            container,
            text="Show API keys",
            variable=self.show_keys,
            command=self.toggle_api_key_visibility,
        )
        self.show_keys_checkbox.grid(
            row=6,
            column=0,
            padx=20,
            pady=(0, 20),
            sticky="w",
        )

        # ==========================================
        # Default orientation
        # ==========================================

        ctk.CTkLabel(
            container,
            text="Default Orientation",
            font=("Segoe UI", 16, "bold"),
        ).grid(
            row=7,
            column=0,
            padx=20,
            pady=(0, 8),
            sticky="w",
        )

        saved_orientation = self.settings.get(
            "images",
            "default_orientation",
            "vertical",
        )

        if saved_orientation not in self.VALID_ORIENTATIONS:
            saved_orientation = "vertical"

        self.orientation = ctk.StringVar(
            value=saved_orientation,
        )

        self.orientation_menu = ctk.CTkOptionMenu(
            container,
            variable=self.orientation,
            values=[
                "vertical",
                "horizontal",
                "all",
            ],
            width=220,
        )
        self.orientation_menu.grid(
            row=8,
            column=0,
            padx=20,
            pady=(0, 20),
            sticky="w",
        )

        # ==========================================
        # Status
        # ==========================================

        self.status_label = ctk.CTkLabel(
            container,
            text="",
            text_color="gray",
        )
        self.status_label.grid(
            row=9,
            column=0,
            padx=20,
            pady=(0, 10),
            sticky="w",
        )

        # ==========================================
        # Buttons
        # ==========================================

        button_row = ctk.CTkFrame(
            container,
            fg_color="transparent",
        )
        button_row.grid(
            row=10,
            column=0,
            padx=20,
            pady=(5, 20),
            sticky="e",
        )

        self.test_button = ctk.CTkButton(
            button_row,
            text="Test Connection",
            width=150,
            command=self.start_connection_test,
        )
        self.test_button.pack(
            side="left",
            padx=(0, 10),
        )

        self.save_button = ctk.CTkButton(
            button_row,
            text="💾 Save Changes",
            width=160,
            command=self.save_settings,
        )
        self.save_button.pack(
            side="left",
        )

        self.provider_changed(
            self.provider.get()
        )

    def provider_changed(self, provider):
        """
        Update the status text when the selected provider changes.

        Both API-key fields remain visible.
        """
        provider = str(provider or "").strip()

        self.status_label.configure(
            text=f"Selected provider: {provider}",
        )

    def toggle_api_key_visibility(self):
        show_value = (
            ""
            if self.show_keys.get()
            else "●"
        )

        self.pixabay_key_entry.configure(
            show=show_value,
        )

        self.pexels_key_entry.configure(
            show=show_value,
        )

    def save_settings(self, *, show_message=True):
        provider = self.provider.get().strip()
        orientation = (
            self.orientation.get()
            .strip()
            .lower()
        )

        pixabay_key = (
            self.pixabay_key_entry.get()
            .strip()
        )

        pexels_key = (
            self.pexels_key_entry.get()
            .strip()
        )

        if provider not in self.VALID_PROVIDERS:
            messagebox.showwarning(
                "Image Settings",
                "Select a valid image provider.",
                parent=self,
            )
            return False

        if orientation not in self.VALID_ORIENTATIONS:
            messagebox.showwarning(
                "Image Settings",
                "Select a valid default orientation.",
                parent=self,
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

        self.status_label.configure(
            text="Image settings saved.",
        )

        if show_message:
            messagebox.showinfo(
                "Image Settings",
                "Image settings saved successfully.",
                parent=self,
            )

        return True

    def start_connection_test(self):
        provider = self.provider.get().strip()

        if provider == "Pixabay":
            api_key = (
                self.pixabay_key_entry.get()
                .strip()
            )
        elif provider == "Pexels":
            api_key = (
                self.pexels_key_entry.get()
                .strip()
            )
        else:
            messagebox.showwarning(
                "Image Settings",
                "Select a valid image provider.",
                parent=self,
            )
            return

        if not api_key:
            messagebox.showwarning(
                provider,
                f"Enter a {provider} API key before testing.",
                parent=self,
            )
            return

        # search_images() reads from SettingsManager, so save
        # the current entry values before starting the test.
        if not self.save_settings(
            show_message=False,
        ):
            return

        self.test_button.configure(
            state="disabled",
            text="Testing...",
        )

        self.save_button.configure(
            state="disabled",
        )

        self.status_label.configure(
            text=f"Testing the {provider} connection...",
        )

        worker = threading.Thread(
            target=self.perform_connection_test,
            args=(provider,),
            daemon=True,
        )
        worker.start()

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

        except (
            ValueError,
            ImageSearchError,
        ) as exc:
            self.after(
                0,
                lambda message=str(exc): (
                    self.connection_test_failed(
                        provider,
                        message,
                    )
                ),
            )
            return

        except Exception as exc:
            self.after(
                0,
                lambda message=str(exc): (
                    self.connection_test_failed(
                        provider,
                        (
                            "Connection test failed: "
                            f"{message}"
                        ),
                    )
                ),
            )
            return

        self.after(
            0,
            lambda: self.connection_test_succeeded(
                provider,
                len(results),
            ),
        )

    def connection_test_succeeded(
        self,
        provider,
        result_count,
    ):
        self.test_button.configure(
            state="normal",
            text="Test Connection",
        )

        self.save_button.configure(
            state="normal",
        )

        self.status_label.configure(
            text=f"{provider} connection successful.",
        )

        messagebox.showinfo(
            provider,
            (
                f"The {provider} connection was successful.\n\n"
                f"Test results returned: {result_count}"
            ),
            parent=self,
        )

    def connection_test_failed(
        self,
        provider,
        message,
    ):
        self.test_button.configure(
            state="normal",
            text="Test Connection",
        )

        self.save_button.configure(
            state="normal",
        )

        self.status_label.configure(
            text=f"{provider} connection failed.",
        )

        messagebox.showerror(
            provider,
            message,
            parent=self,
        )