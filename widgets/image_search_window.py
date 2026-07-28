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

THUMBNAIL_SIZE = (
    240,
    160,
)


class ImageSearchWindow(ctk.CTkToplevel):

    def __init__(
        self,
        parent,
        project,
    ):
        super().__init__(parent)

        self.project = project
        self.project_folder = Path(
            project["folder"]
        )
        self.settings = SettingsManager()

        self.results = []
        self.thumbnail_references = []

        self.title(
            "Search Images"
        )
        self.geometry(
            "1100x760"
        )
        self.minsize(
            850,
            600,
        )

        self.transient(
            parent
        )
        self.lift()
        self.focus_force()

        self.build()

    def build(self):
        header = ctk.CTkFrame(
            self,
            fg_color="transparent",
        )
        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10),
        )

        ctk.CTkLabel(
            header,
            text=(
                f"Search Images — "
                f"{self.project['title']}"
            ),
            font=(
                "Segoe UI",
                26,
                "bold",
            ),
        ).pack(
            side="left",
        )

        ctk.CTkButton(
            header,
            text="Close",
            width=100,
            command=self.destroy,
        ).pack(
            side="right",
        )

        controls = ctk.CTkFrame(
            self
        )
        controls.pack(
            fill="x",
            padx=20,
            pady=(0, 10),
        )

        ctk.CTkLabel(
            controls,
            text="Search",
        ).grid(
            row=0,
            column=0,
            padx=(15, 8),
            pady=15,
            sticky="w",
        )

        self.search_entry = ctk.CTkEntry(
            controls,
            placeholder_text=(
                "Example: Saturn planet space"
            ),
        )
        self.search_entry.grid(
            row=0,
            column=1,
            padx=(0, 10),
            pady=15,
            sticky="ew",
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
            values=[
                "vertical",
                "horizontal",
                "all",
            ],
            width=130,
        )
        self.orientation_menu.grid(
            row=0,
            column=2,
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
            column=3,
            padx=(0, 15),
            pady=15,
        )

        controls.grid_columnconfigure(
            1,
            weight=1,
        )

        self.search_entry.bind(
            "<Return>",
            lambda event: self.start_search(),
        )

        self.status_label = ctk.CTkLabel(
            self,
            text=(
                "Enter a search term, "
                "then select Search."
            ),
            text_color="gray",
        )
        self.status_label.pack(
            fill="x",
            padx=25,
            pady=(0, 8),
        )

        self.results_frame = (
            ctk.CTkScrollableFrame(
                self,
            )
        )
        self.results_frame.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 20),
        )

        for column in range(3):
            self.results_frame.grid_columnconfigure(
                column,
                weight=1,
                uniform="image-result",
            )

    def start_search(self):
        query = self.search_entry.get().strip()

        provider = str(
            self.settings.get(
                "images",
                "provider",
                "Pixabay",
            )
            or "Pixabay"
        ).strip()

        key_setting = {
            "Pixabay": "pixabay_api_key",
            "Pexels": "pexels_api_key",
        }.get(provider)

        api_key = str(
            self.settings.get(
                "images",
                key_setting,
                "",
            )
            or ""
        ).strip()

        orientation = (
            self.orientation.get()
        )

        if not query:
            self._set_status(
                "Enter an image search term.",
                "warning",
            )
            self.search_entry.focus_set()
            return

        if not api_key:
            self._set_status(
                (
                    f"No {provider} API key is configured. "
                    "Open Settings → Images and enter the key."
                ),
                "error",
            )
            return

        self.settings.set(
            "images",
            "default_orientation",
            orientation,
        )

        self._clear_results()

        self.search_button.configure(
            state="disabled",
            text="Searching...",
        )

        self.status_label.configure(
            text=(
                f'Searching Pixabay for "{query}"...'
            ),
        )

        worker = threading.Thread(
            target=self._perform_search,
            args=(
                query,
                provider,
                orientation,
            ),
            daemon=True,
        )
        worker.start()

    def _perform_search(
        self,
        query,
        provider,
        orientation,
    ):
        try:
            results = search_images(
                provider_name=provider,
                settings=self.settings,
                query=query,
                page=1,
                per_page=18,
                orientation=orientation,
            )

        except (
            ValueError,
            ImageSearchError,
        ) as exc:
            message = str(exc)

            self.after(
                0,
                lambda: self._show_search_error(
                    message
                ),
            )
            return

        except Exception as exc:
            message = f"Image search failed: {exc}"

            self.after(
                0,
                lambda: self._show_search_error(
                    message
                ),
            )
            return

        self.after(
            0,
            lambda: self._display_results(
                results,
                provider,
            ),
        )

    def _display_results(
        self,
        results,
        provider,
    ):
        self.results = results

        self.search_button.configure(
            state="normal",
            text="🔍 Search",
        )

        if not results:
            self.status_label.configure(
                text=(
                    "No matching images were found."
                ),
            )
            return

        self._set_status(
            (
                f"{len(results)} images found. "
                f"Results provided by {provider}."
            ),
            "success",
        )

        for index, result in enumerate(
            results
        ):
            row = index // 3
            column = index % 3

            card = ctk.CTkFrame(
                self.results_frame,
            )
            card.grid(
                row=row,
                column=column,
                padx=8,
                pady=8,
                sticky="nsew",
            )

            placeholder = ctk.CTkLabel(
                card,
                text="Loading preview...",
                width=THUMBNAIL_SIZE[0],
                height=THUMBNAIL_SIZE[1],
            )
            placeholder.pack(
                padx=10,
                pady=(10, 5),
            )

            tags = (
                result.tags
                or "Image"
            )

            ctk.CTkLabel(
                card,
                text=tags,
                font=(
                    "Segoe UI",
                    15,
                    "bold",
                ),
                wraplength=230,
                justify="left",
            ).pack(
                fill="x",
                padx=12,
                pady=(5, 2),
            )

            details = (
                f"Creator: {result.creator}\n"
                f"{result.width} × {result.height}"
            )

            ctk.CTkLabel(
                card,
                text=details,
                text_color="gray",
                justify="left",
            ).pack(
                fill="x",
                padx=12,
                pady=(0, 8),
            )

            buttons = ctk.CTkFrame(
                card,
                fg_color="transparent",
            )
            buttons.pack(
                fill="x",
                padx=10,
                pady=(0, 10),
            )

            ctk.CTkButton(
                buttons,
                text="Save",
                command=(
                    lambda selected=result:
                    self.start_download(
                        selected
                    )
                ),
            ).pack(
                side="left",
                fill="x",
                expand=True,
                padx=(0, 4),
            )

            ctk.CTkButton(
                buttons,
                text="Source",
                width=75,
                command=(
                    lambda url=result.page_url:
                    self.open_source(
                        url
                    )
                ),
            ).pack(
                side="right",
                padx=(4, 0),
            )

            self._load_thumbnail_async(
                result.preview_url,
                placeholder,
            )

    def _load_thumbnail_async(
        self,
        url,
        label,
    ):
        worker = threading.Thread(
            target=self._fetch_thumbnail,
            args=(
                url,
                label,
            ),
            daemon=True,
        )
        worker.start()

    def _fetch_thumbnail(
        self,
        url,
        label,
    ):
        request = urllib.request.Request(
            url,
            headers={
                "User-Agent": (
                    "FactVaultManager/1.0"
                ),
            },
        )

        try:
            with urllib.request.urlopen(
                request,
                timeout=20,
            ) as response:
                image_data = response.read()

            image = Image.open(
                BytesIO(image_data)
            ).convert(
                "RGB"
            )

            image.thumbnail(
                THUMBNAIL_SIZE,
                Image.Resampling.LANCZOS,
            )

            thumbnail = ctk.CTkImage(
                light_image=image,
                dark_image=image,
                size=image.size,
            )

        except (
            urllib.error.URLError,
            urllib.error.HTTPError,
            OSError,
        ):
            self.after(
                0,
                lambda: self._show_preview_error(
                    label
                ),
            )
            return

        self.after(
            0,
            lambda: self._set_thumbnail(
                label,
                thumbnail,
            ),
        )

    def _show_preview_error(
        self,
        label,
    ):
        if label.winfo_exists():
            label.configure(
                text="Preview unavailable"
            )

    def _set_thumbnail(
        self,
        label,
        thumbnail,
    ):
        if not label.winfo_exists():
            return

        self.thumbnail_references.append(
            thumbnail
        )

        label.configure(
            image=thumbnail,
            text="",
        )

    def start_download(
        self,
        result,
    ):
        self.status_label.configure(
            text=(
                "Saving image to the project..."
            ),
        )

        worker = threading.Thread(
            target=self._perform_download,
            args=(result,),
            daemon=True,
        )
        worker.start()

    def _perform_download(
        self,
        result,
    ):
        try:
            image_path = (
                download_image_to_project(
                    result,
                    self.project_folder,
                )
            )

        except ImageSearchError as exc:
            message = str(exc)

            self.after(
                0,
                lambda: self._show_download_error(
                    message
                ),
            )
            return

        except Exception as exc:
            message = (
                f"Could not save the image: {exc}"
            )

            self.after(
                0,
                lambda: self._show_download_error(
                    message
                ),
            )
            return

        self.after(
            0,
            lambda: self._download_complete(
                image_path
            ),
        )

    def _download_complete(
        self,
        image_path,
    ):
        self._set_status(
            (
                f"Saved {image_path.name} to "
                f"{image_path.parent}"
            ),
            "success",
        )

    def _show_search_error(
        self,
        message,
    ):
        self.search_button.configure(
            state="normal",
            text="🔍 Search",
        )

        self._set_status(
            message,
            "error",
        )

    def _show_download_error(
        self,
        message,
    ):
        self._set_status(
            message,
            "error",
        )

    def _set_status(
        self,
        message,
        status_type="info",
    ):
        colours = {
            "info": (
                "#DBEAFE",
                "#1E3A5F",
            ),
            "success": (
                "#DCFCE7",
                "#14532D",
            ),
            "warning": (
                "#FEF3C7",
                "#78350F",
            ),
            "error": (
                "#FEE2E2",
                "#7F1D1D",
            ),
        }

        light_colour, dark_colour = colours.get(
            status_type,
            colours["info"],
        )

        self.status_frame.configure(
            fg_color=(
                light_colour,
                dark_colour,
            ),
        )

        self.status_label.configure(
            text=message,
        )
    
    def _clear_results(self):
        self.results = []
        self.thumbnail_references.clear()

        for child in (
            self.results_frame.winfo_children()
        ):
            child.destroy()

    def open_source(
        self,
        url,
    ):

        if not url:
            self._set_status(
                "This result does not have a source page.",
                "warning",
            )
            return

        webbrowser.open(
            url
        )