import threading
import urllib.error
import urllib.request
import webbrowser
from io import BytesIO

import customtkinter as ctk
from PIL import Image

from common.settings_manager import SettingsManager
from image_search import (
    ImageSearchError,
    download_media_to_project,
    is_media_saved,
    search_media,
)

THUMBNAIL_SIZE = (210, 140)
PREVIEW_SIZE = (380, 390)
PROVIDERS = ("Both", "Pixabay", "Pexels")
MEDIA_TYPES = ("Images", "Videos")


class MediaSearchPanel(ctk.CTkFrame):
    """Embedded Pixabay/Pexels image and video browser."""

    def __init__(self, parent, project, project_folder):
        super().__init__(parent, fg_color="transparent")
        self.project = project
        self.project_folder = project_folder
        self.settings = SettingsManager()
        self.results = []
        self.thumbnail_references = []
        self.preview_reference = None
        self.selected_result = None
        self.result_cards = {}
        self.saved_badges = {}
        self._build()

    def _build(self):
        controls = ctk.CTkFrame(self)
        controls.pack(fill="x", padx=10, pady=(10, 8))
        ctk.CTkLabel(controls, text="Search").grid(
            row=0, column=0, padx=(15, 8), pady=15
        )
        self.search_entry = ctk.CTkEntry(
            controls, placeholder_text="Example: Saturn planet space"
        )
        self.search_entry.grid(row=0, column=1, padx=(0, 10), pady=15, sticky="ew")

        self.media_choice = ctk.StringVar(value="Images")
        ctk.CTkOptionMenu(
            controls,
            variable=self.media_choice,
            values=list(MEDIA_TYPES),
            width=105,
        ).grid(row=0, column=2, padx=(0, 10), pady=15)

        default_provider = str(
            self.settings.get("images", "provider", "Pixabay") or "Pixabay"
        ).strip()
        if default_provider not in PROVIDERS:
            default_provider = "Pixabay"
        self.provider = ctk.StringVar(value=default_provider)
        ctk.CTkOptionMenu(
            controls, variable=self.provider, values=list(PROVIDERS), width=115
        ).grid(row=0, column=3, padx=(0, 10), pady=15)

        self.orientation = ctk.StringVar(
            value=self.settings.get("images", "default_orientation", "vertical")
        )
        ctk.CTkOptionMenu(
            controls,
            variable=self.orientation,
            values=["vertical", "horizontal", "all"],
            width=120,
        ).grid(row=0, column=4, padx=(0, 10), pady=15)

        self.search_button = ctk.CTkButton(
            controls, text="🔍 Search", width=120, command=self.start_search
        )
        self.search_button.grid(row=0, column=5, padx=(0, 15), pady=15)
        controls.grid_columnconfigure(1, weight=1)
        self.search_entry.bind("<Return>", lambda _event: self.start_search())

        self.status_frame = ctk.CTkFrame(self, corner_radius=8)
        self.status_frame.pack(fill="x", padx=10, pady=(0, 8))
        self.status_label = ctk.CTkLabel(
            self.status_frame,
            text="Choose Images or Videos, enter a search term, then select Search.",
            anchor="w",
        )
        self.status_label.pack(fill="x", padx=14, pady=10)

        content = ctk.CTkFrame(self, fg_color="transparent")
        content.pack(fill="both", expand=True, padx=10, pady=(0, 10))
        content.grid_rowconfigure(0, weight=1)
        content.grid_columnconfigure(0, weight=3)
        content.grid_columnconfigure(1, weight=2)

        self.results_frame = ctk.CTkScrollableFrame(content)
        self.results_frame.grid(row=0, column=0, padx=(0, 8), sticky="nsew")
        for column in range(2):
            self.results_frame.grid_columnconfigure(column, weight=1, uniform="media")

        preview = ctk.CTkFrame(content)
        preview.grid(row=0, column=1, padx=(8, 0), sticky="nsew")
        preview.grid_columnconfigure(0, weight=1)
        preview.grid_rowconfigure(1, weight=1)
        ctk.CTkLabel(preview, text="Preview", font=("Segoe UI", 20, "bold")).grid(
            row=0, column=0, padx=16, pady=(16, 10), sticky="w"
        )
        self.preview_image_label = ctk.CTkLabel(
            preview, text="Select a result to preview it here.", wraplength=340
        )
        self.preview_image_label.grid(
            row=1, column=0, padx=16, pady=10, sticky="nsew"
        )
        self.preview_title_label = ctk.CTkLabel(
            preview,
            text="",
            font=("Segoe UI", 16, "bold"),
            wraplength=360,
            justify="left",
            anchor="w",
        )
        self.preview_title_label.grid(
            row=2, column=0, padx=16, pady=(8, 4), sticky="ew"
        )
        self.preview_details_label = ctk.CTkLabel(
            preview, text="", text_color="gray", justify="left", anchor="w"
        )
        self.preview_details_label.grid(
            row=3, column=0, padx=16, pady=(0, 12), sticky="ew"
        )
        buttons = ctk.CTkFrame(preview, fg_color="transparent")
        buttons.grid(row=4, column=0, padx=16, pady=(0, 16), sticky="ew")
        buttons.grid_columnconfigure(0, weight=1)
        self.save_button = ctk.CTkButton(
            buttons, text="Save to Project", state="disabled", command=self.save_selected
        )
        self.save_button.grid(row=0, column=0, padx=(0, 5), sticky="ew")
        self.source_button = ctk.CTkButton(
            buttons,
            text="Source",
            width=90,
            state="disabled",
            command=self.open_selected_source,
        )
        self.source_button.grid(row=0, column=1, padx=(5, 0))

    def start_search(self):
        query = self.search_entry.get().strip()
        if not query:
            self._set_status("Enter a media search term.", "warning")
            return
        selection = self.provider.get().strip()
        providers, missing = self._resolve_providers(selection)
        if not providers:
            self._set_status(
                f"No API key is configured for {' and '.join(missing)}.", "error"
            )
            return
        media_type = "video" if self.media_choice.get() == "Videos" else "image"
        orientation = self.orientation.get()
        self._clear_results()
        self.search_button.configure(state="disabled", text="Searching...")
        self._set_status(
            f"Searching {' and '.join(providers)} for {media_type}s...", "info"
        )
        threading.Thread(
            target=self._perform_search,
            args=(query, providers, media_type, orientation, missing),
            daemon=True,
        ).start()

    def _resolve_providers(self, selection):
        requested = ["Pixabay", "Pexels"] if selection == "Both" else [selection]
        keys = {"Pixabay": "pixabay_api_key", "Pexels": "pexels_api_key"}
        available, missing = [], []
        for provider in requested:
            key = str(self.settings.get("images", keys[provider], "") or "").strip()
            (available if key else missing).append(provider)
        return available, missing

    def _perform_search(self, query, providers, media_type, orientation, missing):
        groups, errors = [], []
        per_provider = 18 if len(providers) == 1 else 9
        for provider in providers:
            try:
                groups.append(
                    search_media(
                        provider,
                        self.settings,
                        query,
                        media_type=media_type,
                        per_page=per_provider,
                        orientation=orientation,
                    )
                )
            except (ValueError, ImageSearchError) as exc:
                errors.append(f"{provider}: {exc}")
        results = self._interleave(groups)
        self.after(
            0,
            lambda: self._display_results(results, providers, media_type, missing, errors),
        )

    @staticmethod
    def _interleave(groups):
        merged = []
        for index in range(max((len(group) for group in groups), default=0)):
            for group in groups:
                if index < len(group):
                    merged.append(group[index])
        return merged

    def _display_results(self, results, providers, media_type, missing, errors):
        self.results = results
        self.search_button.configure(state="normal", text="🔍 Search")
        if not results:
            self._set_status("No matching media was found." + (" " + " | ".join(errors) if errors else ""), "warning")
            return
        message = f"{len(results)} {media_type}s found from {' and '.join(providers)}."
        if missing:
            message += f" {', '.join(missing)} skipped: no API key."
        if errors:
            message += " " + " | ".join(errors)
        self._set_status(message, "warning" if errors else "success")

        for index, result in enumerate(results):
            card = ctk.CTkFrame(self.results_frame)
            card.grid(row=index // 2, column=index % 2, padx=8, pady=8, sticky="nsew")
            self.result_cards[id(result)] = card
            image_label = ctk.CTkLabel(
                card,
                text="Loading preview...",
                width=THUMBNAIL_SIZE[0],
                height=THUMBNAIL_SIZE[1],
            )
            image_label.pack(padx=10, pady=(10, 5))
            title = ctk.CTkLabel(
                card,
                text=result.tags or result.media_type.title(),
                font=("Segoe UI", 14, "bold"),
                wraplength=200,
                justify="left",
                anchor="w",
            )
            title.pack(fill="x", padx=12, pady=(5, 2))
            duration = f" • {result.duration}s" if result.duration else ""
            details = ctk.CTkLabel(
                card,
                text=f"{result.provider} • {result.creator}{duration}\n{result.width} × {result.height}",
                text_color="gray",
                justify="left",
                anchor="w",
            )
            details.pack(fill="x", padx=12, pady=(0, 4))
            badge = ctk.CTkLabel(
                card,
                text="✓ Saved" if is_media_saved(result, self.project_folder) else "",
                text_color=("#15803D", "#86EFAC"),
                font=("Segoe UI", 13, "bold"),
                anchor="w",
            )
            badge.pack(fill="x", padx=12, pady=(0, 8))
            self.saved_badges[id(result)] = badge
            for widget in (card, image_label, title, details, badge):
                widget.bind("<Button-1>", lambda _e, selected=result: self.select_result(selected))
                widget.bind("<Double-Button-1>", lambda _e, selected=result: self.start_download(selected))
            self._load_image_async(result.preview_url, image_label, THUMBNAIL_SIZE, False)
        self.select_result(results[0])

    def select_result(self, result):
        self.selected_result = result
        for result_id, card in self.result_cards.items():
            card.configure(
                border_width=2 if result_id == id(result) else 0,
                border_color=("#2563EB", "#60A5FA"),
            )
        saved = is_media_saved(result, self.project_folder)
        self.preview_image_label.configure(image=None, text="Loading preview...")
        self.preview_title_label.configure(text=result.tags or result.media_type.title())
        duration = f"\nDuration: {result.duration} seconds" if result.duration else ""
        self.preview_details_label.configure(
            text=(
                f"Type: {result.media_type.title()}\nProvider: {result.provider}\n"
                f"Creator: {result.creator}\nSize: {result.width} × {result.height}{duration}"
            )
        )
        self.save_button.configure(
            state="disabled" if saved else "normal",
            text="✓ Already Saved" if saved else "Save to Project",
        )
        self.source_button.configure(state="normal" if result.page_url else "disabled")
        self._load_image_async(result.preview_url, self.preview_image_label, PREVIEW_SIZE, True)

    def _load_image_async(self, url, label, size, preview):
        threading.Thread(
            target=self._fetch_image, args=(url, label, size, preview), daemon=True
        ).start()

    def _fetch_image(self, url, label, size, preview):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "FactVaultManager/1.0"})
            with urllib.request.urlopen(request, timeout=20) as response:
                image = Image.open(BytesIO(response.read())).convert("RGB")
            image.thumbnail(size, Image.Resampling.LANCZOS)
            rendered = ctk.CTkImage(light_image=image, dark_image=image, size=image.size)
        except (urllib.error.URLError, urllib.error.HTTPError, OSError):
            self.after(0, lambda: label.configure(text="Preview unavailable"))
            return
        self.after(0, lambda: self._set_image(label, rendered, preview))

    def _set_image(self, label, rendered, preview):
        if not label.winfo_exists():
            return
        if preview:
            self.preview_reference = rendered
        else:
            self.thumbnail_references.append(rendered)
        label.configure(image=rendered, text="")

    def save_selected(self):
        if self.selected_result:
            self.start_download(self.selected_result)

    def start_download(self, result):
        if is_media_saved(result, self.project_folder):
            self.select_result(result)
            return
        self._set_status(f"Saving {result.media_type} to the project...", "info")
        self.save_button.configure(state="disabled", text="Saving...")
        threading.Thread(target=self._perform_download, args=(result,), daemon=True).start()

    def _perform_download(self, result):
        try:
            path = download_media_to_project(result, self.project_folder)
        except Exception as exc:
            self.after(0, lambda: self._download_error(str(exc)))
            return
        self.after(0, lambda: self._download_complete(result, path))

    def _download_complete(self, result, path):
        badge = self.saved_badges.get(id(result))
        if badge:
            badge.configure(text="✓ Saved")
        self.select_result(result)
        self._set_status(f"Saved {path.name} to {path.parent}", "success")

    def _download_error(self, message):
        self.save_button.configure(state="normal", text="Save to Project")
        self._set_status(message, "error")

    def _clear_results(self):
        self.results = []
        self.thumbnail_references.clear()
        self.preview_reference = None
        self.selected_result = None
        self.result_cards.clear()
        self.saved_badges.clear()
        for child in self.results_frame.winfo_children():
            child.destroy()
        self.preview_image_label.configure(image=None, text="Select a result to preview it here.")
        self.preview_title_label.configure(text="")
        self.preview_details_label.configure(text="")
        self.save_button.configure(state="disabled", text="Save to Project")
        self.source_button.configure(state="disabled")

    def _set_status(self, message, status_type="info"):
        colours = {
            "info": ("#DBEAFE", "#1E3A5F"),
            "success": ("#DCFCE7", "#14532D"),
            "warning": ("#FEF3C7", "#78350F"),
            "error": ("#FEE2E2", "#7F1D1D"),
        }
        self.status_frame.configure(fg_color=colours.get(status_type, colours["info"]))
        self.status_label.configure(text=message)

    def open_selected_source(self):
        if self.selected_result and self.selected_result.page_url:
            webbrowser.open(self.selected_result.page_url)
