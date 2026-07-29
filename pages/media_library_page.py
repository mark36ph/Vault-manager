import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from tkinter import messagebox

import customtkinter as ctk
from PIL import Image

from image_search import get_media_library_root
from pages.base_page import BasePage

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"}
VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"}
PREVIEW_SIZE = (460, 380)


@dataclass(frozen=True)
class LibraryAsset:
    path: Path
    media_type: str
    source_path: Path | None = None

    @property
    def name(self):
        return self.path.name


def scan_media_library(library_root=None):
    root = get_media_library_root(library_root)
    assets = []
    for folder_name, media_type, extensions in (
        ("Images", "Image", IMAGE_EXTENSIONS),
        ("Videos", "Video", VIDEO_EXTENSIONS),
    ):
        folder = root / folder_name
        if not folder.exists():
            continue
        for path in folder.iterdir():
            if path.is_file() and path.suffix.lower() in extensions:
                source = path.with_suffix(".source.txt")
                assets.append(LibraryAsset(path, media_type, source if source.exists() else None))
    return sorted(assets, key=lambda item: (item.media_type, item.name.lower()))


def copy_library_asset_to_project(asset, project_folder):
    project_folder = Path(project_folder)
    destination_folder = project_folder / "Assets" / ("Videos" if asset.media_type == "Video" else "Images")
    destination_folder.mkdir(parents=True, exist_ok=True)
    destination = destination_folder / asset.name
    counter = 2
    while destination.exists():
        destination = destination_folder / f"{asset.path.stem}-{counter}{asset.path.suffix}"
        counter += 1
    shutil.copy2(asset.path, destination)
    if asset.source_path and asset.source_path.exists():
        shutil.copy2(asset.source_path, destination.with_suffix(".source.txt"))
    return destination


def _open_path(path):
    path = str(path)
    if sys.platform == "win32":
        os.startfile(path)
    elif sys.platform == "darwin":
        subprocess.Popen(["open", path])
    else:
        subprocess.Popen(["xdg-open", path])


class MediaLibraryPage(BasePage):
    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Media Library")
        self.app = app
        self.assets = []
        self.visible_assets = []
        self.selected_asset = None
        self.preview_reference = None
        self.filter_value = ctk.StringVar(value="All")
        self.project_by_title = {}
        self.build()
        self.refresh()

    def build(self):
        toolbar = ctk.CTkFrame(self.content)
        toolbar.pack(fill="x", pady=(0, 12))

        self.search = ctk.CTkEntry(toolbar, placeholder_text="Search library...")
        self.search.pack(side="left", fill="x", expand=True, padx=12, pady=12)
        self.search.bind("<KeyRelease>", lambda _event: self.apply_filter())

        ctk.CTkSegmentedButton(
            toolbar,
            values=["All", "Images", "Videos"],
            variable=self.filter_value,
            command=lambda _value: self.apply_filter(),
        ).pack(side="left", padx=8)

        ctk.CTkButton(toolbar, text="Refresh", width=100, command=self.refresh).pack(side="right", padx=12)

        body = ctk.CTkFrame(self.content, fg_color="transparent")
        body.pack(fill="both", expand=True)
        body.grid_rowconfigure(0, weight=1)
        body.grid_columnconfigure(0, weight=2)
        body.grid_columnconfigure(1, weight=3)

        self.list_frame = ctk.CTkScrollableFrame(body)
        self.list_frame.grid(row=0, column=0, padx=(0, 8), sticky="nsew")

        preview = ctk.CTkFrame(body)
        preview.grid(row=0, column=1, padx=(8, 0), sticky="nsew")
        preview.grid_columnconfigure(0, weight=1)
        preview.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(preview, text="Library Preview", font=("Segoe UI", 22, "bold")).grid(
            row=0, column=0, padx=18, pady=(18, 10), sticky="w"
        )
        self.preview_label = ctk.CTkLabel(preview, text="Select an image or video.", wraplength=420)
        self.preview_label.grid(row=1, column=0, padx=18, pady=10, sticky="nsew")
        self.details_label = ctk.CTkLabel(preview, text="", justify="left", anchor="w", wraplength=430)
        self.details_label.grid(row=2, column=0, padx=18, pady=(4, 10), sticky="ew")

        project_row = ctk.CTkFrame(preview, fg_color="transparent")
        project_row.grid(row=3, column=0, padx=18, pady=8, sticky="ew")
        project_row.grid_columnconfigure(0, weight=1)
        self.project_menu = ctk.CTkOptionMenu(project_row, values=["No projects available"])
        self.project_menu.grid(row=0, column=0, padx=(0, 8), sticky="ew")
        self.add_button = ctk.CTkButton(
            project_row, text="Add to Project", state="disabled", command=self.add_to_project
        )
        self.add_button.grid(row=0, column=1)

        action_row = ctk.CTkFrame(preview, fg_color="transparent")
        action_row.grid(row=4, column=0, padx=18, pady=(6, 18), sticky="ew")
        action_row.grid_columnconfigure((0, 1), weight=1)
        self.open_button = ctk.CTkButton(action_row, text="Open", state="disabled", command=self.open_selected)
        self.open_button.grid(row=0, column=0, padx=(0, 5), sticky="ew")
        self.folder_button = ctk.CTkButton(
            action_row, text="Open Folder", state="disabled", command=self.open_folder
        )
        self.folder_button.grid(row=0, column=1, padx=(5, 0), sticky="ew")

    def refresh(self):
        self.assets = scan_media_library()
        projects = self.pm.get_all_projects()
        self.project_by_title = {project["title"]: project for project in projects}
        values = list(self.project_by_title) or ["No projects available"]
        self.project_menu.configure(values=values)
        self.project_menu.set(values[0])
        self.apply_filter()

    def apply_filter(self):
        query = self.search.get().strip().lower()
        wanted_type = {"Images": "Image", "Videos": "Video"}.get(self.filter_value.get())
        self.visible_assets = [
            asset for asset in self.assets
            if (wanted_type is None or asset.media_type == wanted_type)
            and query in asset.name.lower()
        ]
        self.render_list()

    def render_list(self):
        for child in self.list_frame.winfo_children():
            child.destroy()
        if not self.visible_assets:
            ctk.CTkLabel(self.list_frame, text="No matching library media found.", text_color="gray").pack(pady=30)
            self.clear_selection()
            return
        for asset in self.visible_assets:
            button = ctk.CTkButton(
                self.list_frame,
                text=f"{'🖼' if asset.media_type == 'Image' else '▶'}  {asset.name}",
                anchor="w",
                height=42,
                command=lambda item=asset: self.select_asset(item),
            )
            button.pack(fill="x", padx=8, pady=4)

    def select_asset(self, asset):
        self.selected_asset = asset
        self.preview_reference = None
        if asset.media_type == "Image":
            try:
                with Image.open(asset.path) as image:
                    image.thumbnail(PREVIEW_SIZE)
                    self.preview_reference = ctk.CTkImage(image.copy(), size=image.size)
                self.preview_label.configure(image=self.preview_reference, text="")
            except Exception:
                self.preview_label.configure(image=None, text="Preview unavailable")
        else:
            self.preview_label.configure(image=None, text="▶ VIDEO\n\nUse Open to play this file.", font=("Segoe UI", 24, "bold"))
        metadata = ""
        if asset.source_path:
            try:
                metadata = asset.source_path.read_text(encoding="utf-8").strip()
            except OSError:
                metadata = ""
        self.details_label.configure(text=f"{asset.name}\n{asset.media_type}\n\n{metadata}")
        self.open_button.configure(state="normal")
        self.folder_button.configure(state="normal")
        self.add_button.configure(state="normal" if self.project_by_title else "disabled")

    def clear_selection(self):
        self.selected_asset = None
        self.preview_reference = None
        self.preview_label.configure(image=None, text="Select an image or video.")
        self.details_label.configure(text="")
        self.open_button.configure(state="disabled")
        self.folder_button.configure(state="disabled")
        self.add_button.configure(state="disabled")

    def add_to_project(self):
        if not self.selected_asset:
            return
        project = self.project_by_title.get(self.project_menu.get())
        if not project:
            messagebox.showwarning("Media Library", "Select a project first.", parent=self)
            return
        try:
            destination = copy_library_asset_to_project(self.selected_asset, project["folder"])
        except Exception as exc:
            messagebox.showerror("Media Library", str(exc), parent=self)
            return
        messagebox.showinfo(
            "Media Library",
            f"Added {destination.name} to {project['title']}.",
            parent=self,
        )

    def open_selected(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path)

    def open_folder(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path.parent)


__all__ = ["LibraryAsset", "MediaLibraryPage", "copy_library_asset_to_project", "scan_media_library"]
