import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from tkinter import messagebox

import customtkinter as ctk
from PIL import Image

from image_search import (
    get_library_metadata_path,
    get_media_library_root,
    migrate_library_metadata,
)
from pages.base_page import BasePage

IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"}
VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"}
PROJECT_STATUSES = ("In Progress", "Scheduled", "Completed", "Published")
PREVIEW_SIZE = (460, 380)


@dataclass(frozen=True)
class LibraryAsset:
    path: Path
    media_type: str
    source_path: Path | None = None
    project_title: str = ""

    @property
    def name(self):
        return self.path.name


def _asset_from_path(path, *, project_title=""):
    path = Path(path)
    suffix = path.suffix.lower()
    if suffix in IMAGE_EXTENSIONS:
        media_type = "Image"
    elif suffix in VIDEO_EXTENSIONS:
        media_type = "Video"
    else:
        return None

    source = path.with_suffix(".source.txt")
    return LibraryAsset(
        path,
        media_type,
        source if source.exists() else None,
        project_title=str(project_title or ""),
    )


def scan_media_library(library_root=None, project_media=None):
    """Return shared-library media plus media stored anywhere under project Assets."""
    root = get_media_library_root(library_root)
    migrate_library_metadata(root)
    assets = []
    seen = set()

    for folder_name, media_type, extensions in (
        ("Images", "Image", IMAGE_EXTENSIONS),
        ("Videos", "Video", VIDEO_EXTENSIONS),
    ):
        folder = root / folder_name
        if not folder.exists():
            continue
        for path in folder.iterdir():
            if path.is_file() and path.suffix.lower() in extensions:
                source = get_library_metadata_path(path, root)
                assets.append(
                    LibraryAsset(
                        path,
                        media_type,
                        source if source.exists() else None,
                    )
                )
                try:
                    seen.add(path.resolve())
                except OSError:
                    seen.add(path)

    for project_title, folder in project_media or ():
        folder = Path(folder)
        if not folder.exists():
            continue
        for path in folder.rglob("*"):
            if not path.is_file():
                continue
            asset = _asset_from_path(path, project_title=project_title)
            if asset is None:
                continue
            try:
                key = path.resolve()
            except OSError:
                key = path
            if key in seen:
                continue
            seen.add(key)
            assets.append(asset)

    return sorted(
        assets,
        key=lambda item: (
            item.media_type,
            item.project_title.lower(),
            item.name.lower(),
        ),
    )


def discover_project_media_roots(*roots):
    """Find real project Assets folders from current and legacy project layouts."""
    found = []
    seen = set()

    for root in roots:
        if root is None:
            continue
        root = Path(root)
        if not root.exists():
            continue

        for status in PROJECT_STATUSES:
            status_folder = root / status
            if not status_folder.is_dir():
                continue

            for project_folder in status_folder.iterdir():
                if not project_folder.is_dir():
                    continue
                assets_folder = project_folder / "Assets"
                if not assets_folder.is_dir():
                    continue

                try:
                    key = assets_folder.resolve()
                except OSError:
                    key = assets_folder
                if key in seen:
                    continue
                seen.add(key)
                found.append((project_folder.name, assets_folder))

    return found


def copy_library_asset_to_project(asset, project_folder):
    project_folder = Path(project_folder)
    if not asset.path.is_file():
        raise FileNotFoundError(f"Library media file does not exist:\n{asset.path}")

    destination_folder = project_folder / "Assets" / ("Videos" if asset.media_type == "Video" else "Images")
    destination_folder.mkdir(parents=True, exist_ok=True)
    destination = destination_folder / asset.name
    counter = 2
    while destination.exists():
        destination = destination_folder / f"{asset.path.stem}-{counter}{asset.path.suffix}"
        counter += 1

    shutil.copy2(asset.path, destination)
    if not destination.is_file():
        raise OSError(f"The media file could not be copied to:\n{destination}")

    if asset.source_path and asset.source_path.exists():
        metadata_destination = destination.with_suffix(".source.txt")
        shutil.copy2(asset.source_path, metadata_destination)
        if not metadata_destination.is_file():
            raise OSError(f"The source metadata could not be copied to:\n{metadata_destination}")
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

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))
        self.subtitle = ctk.CTkLabel(
            self,
            text="Browse shared media and assets gathered by Production.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)

        self.build()
        self.refresh()

    def build(self):
        toolbar = ctk.CTkFrame(self.content, fg_color="transparent")
        toolbar.pack(fill="x", pady=(0, 10))

        self.search = ctk.CTkEntry(
            toolbar,
            placeholder_text="Search media or project...",
            height=36,
        )
        self.search.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.search.bind("<KeyRelease>", lambda _event: self.apply_filter())

        ctk.CTkSegmentedButton(
            toolbar,
            values=["All", "Images", "Videos"],
            variable=self.filter_value,
            command=lambda _value: self.apply_filter(),
            height=34,
        ).pack(side="left", padx=8)

        ctk.CTkButton(
            toolbar,
            text="Refresh",
            width=88,
            height=34,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.refresh,
        ).pack(side="right")

        body = ctk.CTkFrame(self.content, fg_color="transparent")
        body.pack(fill="both", expand=True)
        body.grid_rowconfigure(0, weight=1)
        body.grid_columnconfigure(0, weight=2)
        body.grid_columnconfigure(1, weight=3)

        self.list_frame = ctk.CTkScrollableFrame(
            body,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        self.list_frame.grid(row=0, column=0, padx=(0, 6), sticky="nsew")

        preview = ctk.CTkFrame(
            body,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        preview.grid(row=0, column=1, padx=(6, 0), sticky="nsew")
        preview.grid_columnconfigure(0, weight=1)
        preview.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(
            preview,
            text="Preview",
            font=("Segoe UI", 15, "bold"),
        ).grid(row=0, column=0, padx=16, pady=(14, 8), sticky="w")

        self.preview_label = ctk.CTkLabel(
            preview,
            text="Select an image or video.",
            wraplength=420,
            text_color=("#667085", "#8F96A3"),
        )
        self.preview_label.grid(row=1, column=0, padx=16, pady=8, sticky="nsew")

        self.details_label = ctk.CTkLabel(
            preview,
            text="",
            justify="left",
            anchor="w",
            wraplength=430,
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        )
        self.details_label.grid(row=2, column=0, padx=16, pady=(4, 8), sticky="ew")

        project_row = ctk.CTkFrame(preview, fg_color="transparent")
        project_row.grid(row=3, column=0, padx=16, pady=6, sticky="ew")
        project_row.grid_columnconfigure(0, weight=1)
        self.project_menu = ctk.CTkOptionMenu(project_row, values=["No projects available"], height=34)
        self.project_menu.grid(row=0, column=0, padx=(0, 8), sticky="ew")
        self.add_button = ctk.CTkButton(
            project_row,
            text="Add to Project",
            height=34,
            state="disabled",
            command=self.add_to_project,
        )
        self.add_button.grid(row=0, column=1)

        action_row = ctk.CTkFrame(preview, fg_color="transparent")
        action_row.grid(row=4, column=0, padx=16, pady=(6, 16), sticky="ew")
        action_row.grid_columnconfigure((0, 1), weight=1)
        self.open_button = ctk.CTkButton(
            action_row,
            text="Open",
            height=34,
            state="disabled",
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.open_selected,
        )
        self.open_button.grid(row=0, column=0, padx=(0, 4), sticky="ew")
        self.folder_button = ctk.CTkButton(
            action_row,
            text="Open Folder",
            height=34,
            state="disabled",
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.open_folder,
        )
        self.folder_button.grid(row=0, column=1, padx=(4, 0), sticky="ew")

    def refresh(self):
        projects = self.pm.get_all_projects()
        self.project_by_title = {project["title"]: project for project in projects}
        values = list(self.project_by_title) or ["No projects available"]
        self.project_menu.configure(values=values)
        self.project_menu.set(values[0])

        project_media = []
        seen_folders = set()

        def add_project_media(title, assets_folder):
            assets_folder = Path(assets_folder)
            try:
                key = assets_folder.resolve()
            except OSError:
                key = assets_folder
            if key in seen_folders:
                return
            seen_folders.add(key)
            project_media.append((str(title or assets_folder.parent.name), assets_folder))

        for project in projects:
            try:
                project_folder = self.pm.resolve_project_folder(project)
            except Exception:
                continue
            add_project_media(project["title"], Path(project_folder) / "Assets")

        discovery_roots = [Path.cwd(), Path.cwd() / "projects"]
        try:
            discovery_roots.append(self.pm.get_projects_root())
        except Exception:
            pass

        for title, assets_folder in discover_project_media_roots(*discovery_roots):
            add_project_media(title, assets_folder)

        self.assets = scan_media_library(project_media=project_media)
        self.apply_filter()

    def apply_filter(self):
        query = self.search.get().strip().lower()
        wanted_type = {"Images": "Image", "Videos": "Video"}.get(self.filter_value.get())
        self.visible_assets = [
            asset
            for asset in self.assets
            if (wanted_type is None or asset.media_type == wanted_type)
            and (
                query in asset.name.lower()
                or query in asset.project_title.lower()
            )
        ]
        self.render_list()

    def render_list(self):
        for child in self.list_frame.winfo_children():
            child.destroy()

        if not self.visible_assets:
            ctk.CTkLabel(
                self.list_frame,
                text="No matching media found.",
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=12, pady=20)
            self.clear_selection()
            return

        for asset in self.visible_assets:
            source = asset.project_title or "Shared Library"
            ctk.CTkButton(
                self.list_frame,
                text=f"{'Image' if asset.media_type == 'Image' else 'Video'}  ·  {asset.name}  ·  {source}",
                anchor="w",
                height=36,
                corner_radius=6,
                fg_color="transparent",
                hover_color=("#F2F4F7", "#252A33"),
                text_color=("#344054", "#D0D5DD"),
                command=lambda item=asset: self.select_asset(item),
            ).pack(fill="x", padx=6, pady=2)

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
            self.preview_label.configure(
                image=None,
                text="VIDEO\n\nUse Open to play this file.",
                font=("Segoe UI", 18, "bold"),
            )

        metadata = ""
        if asset.source_path:
            try:
                metadata = asset.source_path.read_text(encoding="utf-8").strip()
            except OSError:
                metadata = ""

        source = f"Project: {asset.project_title}" if asset.project_title else "Shared Media Library"
        details = f"{asset.name}\n{asset.media_type}\n{source}"
        if metadata:
            details += f"\n\n{metadata}"
        self.details_label.configure(text=details)
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
            project_folder = self.pm.resolve_project_folder(project)
            destination = copy_library_asset_to_project(self.selected_asset, project_folder)
        except Exception as exc:
            messagebox.showerror("Media Library", str(exc), parent=self)
            return
        messagebox.showinfo(
            "Media Library",
            f"Added {destination.name} to {project['title']}.\n\n{destination}",
            parent=self,
        )

    def open_selected(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path)

    def open_folder(self):
        if self.selected_asset:
            _open_path(self.selected_asset.path.parent)


__all__ = [
    "LibraryAsset",
    "MediaLibraryPage",
    "copy_library_asset_to_project",
    "discover_project_media_roots",
    "scan_media_library",
]
