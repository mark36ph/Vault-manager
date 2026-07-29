import os
import subprocess
import sys
import threading
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path
from tkinter import Menu, messagebox, simpledialog

import customtkinter as ctk
from PIL import Image


IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"}
VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"}
THUMBNAIL_SIZE = (210, 140)
PREVIEW_SIZE = (420, 390)


@dataclass(frozen=True)
class ProjectAsset:
    path: Path
    media_type: str
    source_path: Path | None = None

    @property
    def name(self):
        return self.path.name

    @property
    def size_bytes(self):
        try:
            return self.path.stat().st_size
        except OSError:
            return 0


def scan_project_assets(project_folder):
    """Return supported image and video assets stored in a project."""
    project_folder = Path(project_folder)
    assets = []
    folders = (
        (project_folder / "Assets" / "Images", "Image", IMAGE_EXTENSIONS),
        (project_folder / "Assets" / "Videos", "Video", VIDEO_EXTENSIONS),
    )

    for folder, media_type, extensions in folders:
        if not folder.exists():
            continue
        for path in folder.iterdir():
            if not path.is_file() or path.suffix.lower() not in extensions:
                continue
            source_path = path.with_suffix(".source.txt")
            assets.append(
                ProjectAsset(
                    path=path,
                    media_type=media_type,
                    source_path=source_path if source_path.exists() else None,
                )
            )

    return sorted(
        assets,
        key=lambda asset: (
            asset.media_type,
            asset.name.lower(),
        ),
    )


def rename_project_asset(asset, new_name):
    """Rename an asset and its companion source file."""
    new_name = str(new_name or "").strip()
    if not new_name:
        raise ValueError("Enter a file name.")

    requested = Path(new_name)
    if requested.name != new_name or new_name in {".", ".."}:
        raise ValueError("Enter a file name without folders.")

    if requested.suffix:
        if requested.suffix.lower() != asset.path.suffix.lower():
            raise ValueError(f"The file extension must remain {asset.path.suffix}.")
        target_name = requested.name
    else:
        target_name = f"{requested.name}{asset.path.suffix}"

    target = asset.path.with_name(target_name)
    if target == asset.path:
        return asset
    if target.exists():
        raise FileExistsError(f"{target.name} already exists.")

    asset.path.rename(target)

    source_target = None
    if asset.source_path and asset.source_path.exists():
        source_target = target.with_suffix(".source.txt")
        asset.source_path.rename(source_target)

    return ProjectAsset(
        path=target,
        media_type=asset.media_type,
        source_path=source_target,
    )


def delete_project_asset(asset):
    """Delete an asset and its companion source file."""
    asset.path.unlink()
    if asset.source_path and asset.source_path.exists():
        asset.source_path.unlink()


def _format_size(size_bytes):
    value = float(size_bytes)
    for unit in ("B", "KB", "MB", "GB"):
        if value < 1024 or unit == "GB":
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
    return f"{value:.1f} GB"


def _open_path(path):
    path = str(path)
    if sys.platform == "win32":
        os.startfile(path)
    elif sys.platform == "darwin":
        subprocess.Popen(["open", path])
    else:
        subprocess.Popen(["xdg-open", path])


class ProjectAssetsPanel(ctk.CTkFrame):
    """Browse and manage media already stored in a project."""

    def __init__(self, parent, project_folder):
        super().__init__(parent, fg_color="transparent")
        self.project_folder = Path(project_folder)
        self.assets = []
        self.visible_assets = []
        self.selected_asset = None
        self.thumbnail_references = []
        self.preview_reference = None
        self.asset_cards = {}
        self.filter_value = ctk.StringVar(value="All")
        self._build()
        self.refresh_assets()

    def _build(self):
        toolbar = ctk.CTkFrame(self)
        toolbar.pack(fill="x", padx=10, pady=(10, 8))

        ctk.CTkLabel(
            toolbar,
            text="Project Assets",
            font=("Segoe UI", 22, "bold"),
        ).pack(side="left", padx=15, pady=14)

        ctk.CTkSegmentedButton(
            toolbar,
            values=["All", "Images", "Videos"],
            variable=self.filter_value,
            command=lambda _value: self.apply_filter(),
        ).pack(side="left", padx=15)

        ctk.CTkButton(
            toolbar,
            text="↻ Refresh",
            width=110,
            command=self.refresh_assets,
        ).pack(side="right", padx=15, pady=12)

        self.status_label = ctk.CTkLabel(
            self,
            text="",
            anchor="w",
            text_color="gray",
        )
        self.status_label.pack(fill="x", padx=20, pady=(0, 8))

        content = ctk.CTkFrame(self, fg_color="transparent")
        content.pack(fill="both", expand=True, padx=10, pady=(0, 10))
        content.grid_rowconfigure(0, weight=1)
        content.grid_columnconfigure(0, weight=3)
        content.grid_columnconfigure(1, weight=2)

        self.grid_frame = ctk.CTkScrollableFrame(content)
        self.grid_frame.grid(row=0, column=0, padx=(0, 8), sticky="nsew")
        for column in range(2):
            self.grid_frame.grid_columnconfigure(column, weight=1, uniform="asset")

        preview = ctk.CTkFrame(content)
        preview.grid(row=0, column=1, padx=(8, 0), sticky="nsew")
        preview.grid_columnconfigure(0, weight=1)
        preview.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(
            preview,
            text="Asset Preview",
            font=("Segoe UI", 20, "bold"),
        ).grid(row=0, column=0, padx=16, pady=(16, 10), sticky="w")

        self.preview_label = ctk.CTkLabel(
            preview,
            text="Select an asset to preview it here.",
            wraplength=360,
        )
        self.preview_label.grid(row=1, column=0, padx=16, pady=10, sticky="nsew")

        self.name_label = ctk.CTkLabel(
            preview,
            text="",
            font=("Segoe UI", 16, "bold"),
            wraplength=380,
            justify="left",
            anchor="w",
        )
        self.name_label.grid(row=2, column=0, padx=16, pady=(8, 4), sticky="ew")

        self.details_label = ctk.CTkLabel(
            preview,
            text="",
            text_color="gray",
            justify="left",
            anchor="w",
        )
        self.details_label.grid(row=3, column=0, padx=16, pady=(0, 12), sticky="ew")

        buttons = ctk.CTkFrame(preview, fg_color="transparent")
        buttons.grid(row=4, column=0, padx=16, pady=(0, 16), sticky="ew")
        for column in range(2):
            buttons.grid_columnconfigure(column, weight=1)

        self.open_button = ctk.CTkButton(
            buttons,
            text="Open",
            state="disabled",
            command=self.open_selected,
        )
        self.open_button.grid(row=0, column=0, padx=(0, 5), pady=(0, 8), sticky="ew")

        self.folder_button = ctk.CTkButton(
            buttons,
            text="Open Folder",
            state="disabled",
            command=self.open_selected_folder,
        )
        self.folder_button.grid(row=0, column=1, padx=(5, 0), pady=(0, 8), sticky="ew")

        self.rename_button = ctk.CTkButton(
            buttons,
            text="Rename",
            state="disabled",
            command=self.rename_selected,
        )
        self.rename_button.grid(row=1, column=0, padx=(0, 5), sticky="ew")

        self.delete_button = ctk.CTkButton(
            buttons,
            text="Delete",
            state="disabled",
            command=self.delete_selected,
            fg_color=("#DC2626", "#991B1B"),
            hover_color=("#B91C1C", "#7F1D1D"),
        )
        self.delete_button.grid(row=1, column=1, padx=(5, 0), sticky="ew")

    def refresh_assets(self):
        self.assets = scan_project_assets(self.project_folder)
        self.apply_filter()

    def apply_filter(self):
        selection = self.filter_value.get()
        media_type = {"Images": "Image", "Videos": "Video"}.get(selection)
        self.visible_assets = [
            asset for asset in self.assets
            if media_type is None or asset.media_type == media_type
        ]
        self._render_assets()

    def _render_assets(self):
        self.thumbnail_references.clear()
        self.asset_cards.clear()
        for child in self.grid_frame.winfo_children():
            child.destroy()

        count = len(self.visible_assets)
        total = len(self.assets)
        self.status_label.configure(
            text=f"Showing {count} of {total} project assets. Double-click an asset to open it."
        )

        if not self.visible_assets:
            ctk.CTkLabel(
                self.grid_frame,
                text="No matching project assets were found.",
                text_color="gray",
            ).grid(row=0, column=0, columnspan=2, padx=20, pady=40)
            self._clear_selection()
            return

        for index, asset in enumerate(self.visible_assets):
            card = ctk.CTkFrame(self.grid_frame)
            card.grid(
                row=index // 2,
                column=index % 2,
                padx=8,
                pady=8,
                sticky="nsew",
            )
            self.asset_cards[asset.path] = card

            preview = ctk.CTkLabel(
                card,
                text="Loading preview..." if asset.media_type == "Image" else "▶ VIDEO",
                width=THUMBNAIL_SIZE[0],
                height=THUMBNAIL_SIZE[1],
                font=("Segoe UI", 18, "bold"),
            )
            preview.pack(padx=10, pady=(10, 5))

            title = ctk.CTkLabel(
                card,
                text=asset.name,
                font=("Segoe UI", 14, "bold"),
                wraplength=200,
                justify="left",
                anchor="w",
            )
            title.pack(fill="x", padx=12, pady=(5, 2))

            details = ctk.CTkLabel(
                card,
                text=f"{asset.media_type} • {_format_size(asset.size_bytes)}",
                text_color="gray",
                anchor="w",
            )
            details.pack(fill="x", padx=12, pady=(0, 10))

            for widget in (card, preview, title, details):
                widget.bind(
                    "<Button-1>",
                    lambda _event, selected=asset: self.select_asset(selected),
                )
                widget.bind(
                    "<Double-Button-1>",
                    lambda _event, selected=asset: _open_path(selected.path),
                )
                widget.bind(
                    "<Button-3>",
                    lambda event, selected=asset: self._show_context_menu(event, selected),
                )

            if asset.media_type == "Image":
                self._load_image_async(asset.path, preview, THUMBNAIL_SIZE, False)

        self.select_asset(self.visible_assets[0])

    def select_asset(self, asset):
        self.selected_asset = asset
        for path, card in self.asset_cards.items():
            card.configure(
                border_width=2 if path == asset.path else 0,
                border_color=("#2563EB", "#60A5FA"),
            )

        self.preview_reference = None
        self.name_label.configure(text=asset.name)
        self.details_label.configure(
            text=(
                f"Type: {asset.media_type}\n"
                f"Size: {_format_size(asset.size_bytes)}\n"
                f"Folder: {asset.path.parent}"
            )
        )
        for button in (
            self.open_button,
            self.folder_button,
            self.rename_button,
            self.delete_button,
        ):
            button.configure(state="normal")

        if asset.media_type == "Image":
            self.preview_label.configure(image=None, text="Loading preview...")
            self._load_image_async(asset.path, self.preview_label, PREVIEW_SIZE, True)
        else:
            self.preview_label.configure(
                image=None,
                text="▶ VIDEO\n\nSelect Open to play this video in your default media player.",
                font=("Segoe UI", 18, "bold"),
            )

    def _load_image_async(self, path, label, target_size, is_preview):
        threading.Thread(
            target=self._load_image,
            args=(path, label, target_size, is_preview),
            daemon=True,
        ).start()

    def _load_image(self, path, label, target_size, is_preview):
        try:
            image = Image.open(path).convert("RGB")
            image.thumbnail(target_size, Image.Resampling.LANCZOS)
            rendered = ctk.CTkImage(
                light_image=image,
                dark_image=image,
                size=image.size,
            )
        except OSError:
            self.after(0, lambda: self._show_preview_error(label))
            return
        self.after(0, lambda: self._set_image(label, rendered, is_preview))

    def _set_image(self, label, rendered, is_preview):
        if not label.winfo_exists():
            return
        if is_preview:
            self.preview_reference = rendered
        else:
            self.thumbnail_references.append(rendered)
        label.configure(image=rendered, text="")

    def _show_preview_error(self, label):
        if label.winfo_exists():
            label.configure(image=None, text="Preview unavailable")

    def _show_context_menu(self, event, asset):
        self.select_asset(asset)
        menu = Menu(self, tearoff=0)
        menu.add_command(label="Open", command=self.open_selected)
        menu.add_command(label="Open Folder", command=self.open_selected_folder)
        menu.add_separator()
        menu.add_command(label="Rename", command=self.rename_selected)
        menu.add_command(label="Delete", command=self.delete_selected)
        menu.tk_popup(event.x_root, event.y_root)

    def open_selected(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path)

    def open_selected_folder(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path.parent)

    def rename_selected(self):
        asset = self.selected_asset
        if asset is None:
            return
        new_name = simpledialog.askstring(
            "Rename Asset",
            "New file name:",
            initialvalue=asset.path.stem,
            parent=self,
        )
        if new_name is None:
            return
        try:
            renamed = rename_project_asset(asset, new_name)
        except (ValueError, FileExistsError, OSError) as exc:
            messagebox.showerror("Rename Asset", str(exc), parent=self)
            return
        self.refresh_assets()
        for candidate in self.assets:
            if candidate.path == renamed.path:
                self.select_asset(candidate)
                break

    def delete_selected(self):
        asset = self.selected_asset
        if asset is None:
            return
        confirmed = messagebox.askyesno(
            "Delete Asset",
            f"Delete {asset.name}?\n\nThis cannot be undone.",
            parent=self,
        )
        if not confirmed:
            return
        try:
            delete_project_asset(asset)
        except OSError as exc:
            messagebox.showerror("Delete Asset", str(exc), parent=self)
            return
        self.refresh_assets()

    def _clear_selection(self):
        self.selected_asset = None
        self.preview_reference = None
        self.preview_label.configure(
            image=None,
            text="Select an asset to preview it here.",
            font=("Segoe UI", 13),
        )
        self.name_label.configure(text="")
        self.details_label.configure(text="")
        for button in (
            self.open_button,
            self.folder_button,
            self.rename_button,
            self.delete_button,
        ):
            button.configure(state="disabled")


__all__ = [
    "ProjectAsset",
    "ProjectAssetsPanel",
    "delete_project_asset",
    "rename_project_asset",
    "scan_project_assets",
]
