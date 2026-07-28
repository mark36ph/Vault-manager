import threading
import urllib.error
import urllib.request
import webbrowser
from io import BytesIO
from pathlib import Path

import customtkinter as ctk
from PIL import Image

from common.settings_manager import SettingsManager
from image_search import (
    ImageSearchError,
    download_image_to_project,
    search_images,
)

THUMBNAIL_SIZE = (220, 145)
PREVIEW_SIZE = (390, 420)
PROVIDERS = ("Both", "Pixabay", "Pexels")


class ImageSearchWindow(ctk.CTkToplevel):

    def __init__(self, parent, project):
        super().__init__(parent)

        self.project = project
        self.project_folder = Path(project["folder"])
        self.settings = SettingsManager()

        self.results = []
        self.thumbnail_references = []
        self.preview_reference = None
        self.selected_result = None
        self.result_cards = {}

        self.title("Search Images")
        self.geometry("1250x780")
        self.minsize(980, 650)
        self.transient(parent)
        self.lift()
        self.focus_force()

        self.build()

    def build(self):
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.pack(fill="x", padx=20, pady=(20, 10))

        ctk.CTkLabel(
            header,
            text=f"Search Images — {self.project['title']}",
            font=("Segoe UI", 26, "bold"),
        ).pack(side="left")

        ctk.CTkButton(
            header,
            text="Close",
            width=100,
            command=self.destroy,
        ).pack(side="right")

        controls = ctk.CTkFrame(self)
        controls.pack(fill="x", padx=20, pady=(0, 10))

        ctk.CTkLabel(controls, text="Search").grid(
            row=0,
            column=0,
            padx=(15, 8),
            pady=15,
            sticky="w",
        )

        self.search_entry = ctk.CTkEntry(
            controls,
            placeholder_text="Example: Saturn planet space",
        )
        self.search_entry.grid(
            row=0,
            column=1,
            padx=(0, 10),
            pady=15,
            sticky="ew",
        )

        default_provider = str(
            self.settings.get("images", "provider", "Pixabay") or "Pixabay"
        ).strip()
        if default_provider not in PROVIDERS:
            default_provider = "Pixabay"

        self.provider = ctk.StringVar(value=default_provider)
        self.provider_menu = ctk.CTkOptionMenu(
            controls,
            variable=self.provider,
            values=list(PROVIDERS),
            width=125,
        )
        self.provider_menu.grid(
            row=0,
            column=2,
            padx=(0, 10),
            pady=15,
        )

        self.orientation = ctk.StringVar(
            value=self.settings.get(
                "images",
                "default_orientation",
                "vertical",
            )
        )
        self.orientation_menu = ctk.CTkOptionMenu(
            controls,
            variable=self.orientation,
            values=["vertical", "horizontal", "all"],
            width=130,
        )
        self.orientation_menu.grid(
            row=0,
            column=3,
            padx=(0, 10),
            pady=15,
        )

        self.search_button = ctk.CTkButton(
            controls,
            text="🔍 Search",
            width=130,
            command=self.start_search,
        )
        self.search_button.grid(
            row=0,
            column=4,
            padx=(0, 15),
            pady=15,
        )

        controls.grid_columnconfigure(1, weight=1)
        self.search_entry.bind("<Return>", lambda _event: self.start_search())

        self.status_frame = ctk.CTkFrame(self, corner_radius=8)
        self.status_frame.pack(fill="x", padx=20, pady=(0, 10))

        self.status_label = ctk.CTkLabel(
            self.status_frame,
            text="Enter a search term, then select Search.",
            anchor="w",
        )
        self.status_label.pack(fill="x", padx=14, pady=10)

        content = ctk.CTkFrame(self, fg_color="transparent")
        content.pack(fill="both", expand=True, padx=20, pady=(0, 20))
        content.grid_rowconfigure(0, weight=1)
        content.grid_columnconfigure(0, weight=3)
        content.grid_columnconfigure(1, weight=2)

        self.results_frame = ctk.CTkScrollableFrame(content)
        self.results_frame.grid(
            row=0,
            column=0,
            padx=(0, 10),
            sticky="nsew",
        )
        for column in range(2):
            self.results_frame.grid_columnconfigure(
                column,
                weight=1,
                uniform="image-result",
            )

        self.preview_panel = ctk.CTkFrame(content)
        self.preview_panel.grid(
            row=0,
            column=1,
            padx=(10, 0),
            sticky="nsew",
        )
        self.preview_panel.grid_columnconfigure(0, weight=1)
        self.preview_panel.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(
            self.preview_panel,
            text="Preview",
            font=("Segoe UI", 20, "bold"),
        ).grid(row=0, column=0, padx=16, pady=(16, 10), sticky="w")

        self.preview_image_label = ctk.CTkLabel(
            self.preview_panel,
            text="Select an image to preview it here.",
            wraplength=340,
        )
        self.preview_image_label.grid(
            row=1,
            column=0,
            padx=16,
            pady=10,
            sticky="nsew",
        )

        self.preview_title_label = ctk.CTkLabel(
            self.preview_panel,
            text="",
            font=("Segoe UI", 16, "bold"),
            wraplength=360,
            justify="left",
            anchor="w",
        )
        self.preview_title_label.grid(
            row=2,
            column=0,
            padx=16,
            pady=(8, 4),
            sticky="ew",
        )

        self.preview_details_label = ctk.CTkLabel(
            self.preview_panel,
            text="",
            text_color="gray",
            justify="left",
            anchor="w",
        )
        self.preview_details_label.grid(
            row=3,
            column=0,
            padx=16,
            pady=(0, 12),
            sticky="ew",
        )

        preview_buttons = ctk.CTkFrame(
            self.preview_panel,
            fg_color="transparent",
        )
        preview_buttons.grid(
            row=4,
            column=0,
            padx=16,
            pady=(0, 16),
            sticky="ew",
        )
        preview_buttons.grid_columnconfigure(0, weight=1)

        self.save_button = ctk.CTkButton(
            preview_buttons,
            text="Save to Project",
            state="disabled",
            command=self.save_selected,
        )
        self.save_button.grid(row=0, column=0, padx=(0, 5), sticky="ew")

        self.source_button = ctk.CTkButton(
            preview_buttons,
            text="Source",
            width=90,
            state="disabled",
            command=self.open_selected_source,
        )
        self.source_button.grid(row=0, column=1, padx=(5, 0))

    def start_search(self):
        query = self.search_entry.get().strip()
        provider = self.provider.get().strip()
        orientation = self.orientation.get()

        if not query:
            self._set_status("Enter an image search term.", "warning")
            self.search_entry.focus_set()
            return

        available, missing = self._resolve_providers(provider)
        if not available:
            names = " and ".join(missing) if missing else provider
            self._set_status(
                f"No API key is configured for {names}. "
                "Open Settings → Images and enter the key.",
                "error",
            )
            return

        self.settings.set("images", "default_orientation", orientation)
        if provider != "Both":
            self.settings.set("images", "provider", provider)

        self._clear_results()
        self.search_button.configure(state="disabled", text="Searching...")

        provider_text = " and ".join(available)
        status = f'Searching {provider_text} for "{query}"...'
        if missing:
            status += f" ({', '.join(missing)} skipped: no API key.)"
        self._set_status(status, "info")

        worker = threading.Thread(
            target=self._perform_search,
            args=(query, available, orientation, missing),
            daemon=True,
        )
        worker.start()

    def _resolve_providers(self, selection):
        requested = ["Pixabay", "Pexels"] if selection == "Both" else [selection]
        key_names = {
            "Pixabay": "pixabay_api_key",
            "Pexels": "pexels_api_key",
        }
        available = []
        missing = []

        for provider in requested:
            api_key = str(
                self.settings.get(
                    "images",
                    key_names[provider],
                    "",
                )
                or ""
            ).strip()
            if api_key:
                available.append(provider)
            else:
                missing.append(provider)

        return available, missing

    def _perform_search(self, query, providers, orientation, missing):
        results_by_provider = []
        errors = []
        per_provider = 18 if len(providers) == 1 else 9

        for provider in providers:
            try:
                provider_results = search_images(
                    provider_name=provider,
                    settings=self.settings,
                    query=query,
                    page=1,
                    per_page=per_provider,
                    orientation=orientation,
                )
                results_by_provider.append(provider_results)
            except (ValueError, ImageSearchError) as exc:
                errors.append(f"{provider}: {exc}")
            except Exception as exc:
                errors.append(f"{provider}: image search failed: {exc}")

        results = self._interleave_results(results_by_provider)

        if not results and errors:
            message = " | ".join(errors)
            self.after(0, lambda: self._show_search_error(message))
            return

        self.after(
            0,
            lambda: self._display_results(results, providers, missing, errors),
        )

    @staticmethod
    def _interleave_results(result_groups):
        merged = []
        longest = max((len(group) for group in result_groups), default=0)
        for index in range(longest):
            for group in result_groups:
                if index < len(group):
                    merged.append(group[index])
        return merged

    def _display_results(self, results, providers, missing, errors):
        self.results = results
        self.search_button.configure(state="normal", text="🔍 Search")

        if not results:
            self._set_status("No matching images were found.", "warning")
            return

        provider_text = " and ".join(providers)
        message = f"{len(results)} images found from {provider_text}."
        extras = []
        if missing:
            extras.append(f"{', '.join(missing)} skipped: no API key")
        if errors:
            extras.append("; ".join(errors))
        if extras:
            message += " " + " | ".join(extras)

        self._set_status(message, "success" if not errors else "warning")

        for index, result in enumerate(results):
            row = index // 2
            column = index % 2

            card = ctk.CTkFrame(self.results_frame, cursor="hand2")
            card.grid(
                row=row,
                column=column,
                padx=8,
                pady=8,
                sticky="nsew",
            )
            self.result_cards[id(result)] = card

            placeholder = ctk.CTkLabel(
                card,
                text="Loading preview...",
                width=THUMBNAIL_SIZE[0],
                height=THUMBNAIL_SIZE[1],
                cursor="hand2",
            )
            placeholder.pack(padx=10, pady=(10, 5))

            title = ctk.CTkLabel(
                card,
                text=result.tags or "Image",
                font=("Segoe UI", 14, "bold"),
                wraplength=210,
                justify="left",
                anchor="w",
                cursor="hand2",
            )
            title.pack(fill="x", padx=12, pady=(5, 2))

            details = ctk.CTkLabel(
                card,
                text=(
                    f"{result.provider} • {result.creator}\n"
                    f"{result.width} × {result.height}"
                ),
                text_color="gray",
                justify="left",
                anchor="w",
                cursor="hand2",
            )
            details.pack(fill="x", padx=12, pady=(0, 10))

            for widget in (card, placeholder, title, details):
                widget.bind(
                    "<Button-1>",
                    lambda _event, selected=result: self.select_result(selected),
                )
                widget.bind(
                    "<Double-Button-1>",
                    lambda _event, selected=result: self.start_download(selected),
                )

            self._load_thumbnail_async(
                result.preview_url,
                placeholder,
                THUMBNAIL_SIZE,
                False,
            )

        self.select_result(results[0])

    def select_result(self, result):
        self.selected_result = result

        for result_id, card in self.result_cards.items():
            card.configure(
                border_width=2 if result_id == id(result) else 0,
                border_color=("#2563EB", "#60A5FA"),
            )

        self.preview_reference = None
        self.preview_image_label.configure(
            image=None,
            text="Loading preview...",
        )
        self.preview_title_label.configure(text=result.tags or "Image")
        self.preview_details_label.configure(
            text=(
                f"Provider: {result.provider}\n"
                f"Creator: {result.creator}\n"
                f"Size: {result.width} × {result.height}"
            )
        )
        self.save_button.configure(state="normal")
        self.source_button.configure(
            state="normal" if result.page_url else "disabled"
        )

        self._load_thumbnail_async(
            result.preview_url,
            self.preview_image_label,
            PREVIEW_SIZE,
            True,
        )

    def _load_thumbnail_async(self, url, label, target_size, is_preview):
        worker = threading.Thread(
            target=self._fetch_thumbnail,
            args=(url, label, target_size, is_preview),
            daemon=True,
        )
        worker.start()

    def _fetch_thumbnail(self, url, label, target_size, is_preview):
        request = urllib.request.Request(
            url,
            headers={"User-Agent": "FactVaultManager/1.0"},
        )

        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                image_data = response.read()

            image = Image.open(BytesIO(image_data)).convert("RGB")
            image.thumbnail(target_size, Image.Resampling.LANCZOS)
            rendered = ctk.CTkImage(
                light_image=image,
                dark_image=image,
                size=image.size,
            )
        except (urllib.error.URLError, urllib.error.HTTPError, OSError):
            self.after(0, lambda: self._show_preview_error(label))
            return

        self.after(
            0,
            lambda: self._set_image(label, rendered, is_preview),
        )

    def _show_preview_error(self, label):
        if label.winfo_exists():
            label.configure(image=None, text="Preview unavailable")

    def _set_image(self, label, rendered, is_preview):
        if not label.winfo_exists():
            return

        if is_preview:
            self.preview_reference = rendered
        else:
            self.thumbnail_references.append(rendered)

        label.configure(image=rendered, text="")

    def save_selected(self):
        if self.selected_result is not None:
            self.start_download(self.selected_result)

    def start_download(self, result):
        self._set_status("Saving image to the project...", "info")
        self.save_button.configure(state="disabled", text="Saving...")

        worker = threading.Thread(
            target=self._perform_download,
            args=(result,),
            daemon=True,
        )
        worker.start()

    def _perform_download(self, result):
        try:
            image_path = download_image_to_project(
                result,
                self.project_folder,
            )
        except ImageSearchError as exc:
            message = str(exc)
            self.after(0, lambda: self._show_download_error(message))
            return
        except Exception as exc:
            message = f"Could not save the image: {exc}"
            self.after(0, lambda: self._show_download_error(message))
            return

        self.after(0, lambda: self._download_complete(image_path))

    def _download_complete(self, image_path):
        self.save_button.configure(state="normal", text="Save to Project")
        self._set_status(
            f"Saved {image_path.name} to {image_path.parent}",
            "success",
        )

    def _show_search_error(self, message):
        self.search_button.configure(state="normal", text="🔍 Search")
        self._set_status(message, "error")

    def _show_download_error(self, message):
        self.save_button.configure(state="normal", text="Save to Project")
        self._set_status(message, "error")

    def _set_status(self, message, status_type="info"):
        colours = {
            "info": ("#DBEAFE", "#1E3A5F"),
            "success": ("#DCFCE7", "#14532D"),
            "warning": ("#FEF3C7", "#78350F"),
            "error": ("#FEE2E2", "#7F1D1D"),
        }
        light_colour, dark_colour = colours.get(
            status_type,
            colours["info"],
        )
        self.status_frame.configure(fg_color=(light_colour, dark_colour))
        self.status_label.configure(text=message)

    def _clear_results(self):
        self.results = []
        self.thumbnail_references.clear()
        self.preview_reference = None
        self.selected_result = None
        self.result_cards.clear()

        for child in self.results_frame.winfo_children():
            child.destroy()

        self.preview_image_label.configure(
            image=None,
            text="Select an image to preview it here.",
        )
        self.preview_title_label.configure(text="")
        self.preview_details_label.configure(text="")
        self.save_button.configure(state="disabled", text="Save to Project")
        self.source_button.configure(state="disabled")

    def open_selected_source(self):
        if self.selected_result is not None:
            self.open_source(self.selected_result.page_url)

    def open_source(self, url):
        if not url:
            self._set_status(
                "This result does not have a source page.",
                "warning",
            )
            return

        webbrowser.open(url)
