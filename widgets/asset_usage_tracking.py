from pathlib import Path
from tkinter import messagebox

from widgets.project_assets_panel import (
    ProjectAssetsPanel,
    delete_project_asset,
)
from widgets.text_input_dialog import ask_confirmation


TEXT_EXTENSIONS = {
    ".txt",
    ".md",
    ".json",
    ".csv",
    ".srt",
    ".vtt",
    ".html",
    ".htm",
    ".xml",
    ".yaml",
    ".yml",
    ".toml",
}
IGNORED_DIRECTORIES = {".git", ".venv", "venv", "__pycache__"}
MAX_TEXT_FILE_SIZE = 2 * 1024 * 1024


def find_asset_references(asset, project_folder):
    """Return project text files that mention the selected asset.

    The scan deliberately excludes the media file itself and its companion
    ``.source.txt`` metadata file so attribution metadata is not mistaken for
    a usage reference.
    """
    project_folder = Path(project_folder)
    asset_path = Path(asset.path)
    source_path = Path(asset.source_path) if asset.source_path else None

    try:
        relative_asset = asset_path.relative_to(project_folder).as_posix()
    except ValueError:
        relative_asset = asset_path.name

    search_terms = {
        asset_path.name.casefold(),
        relative_asset.casefold(),
        relative_asset.replace("/", "\\").casefold(),
    }

    references = []
    if not project_folder.exists():
        return references

    for candidate in project_folder.rglob("*"):
        if not candidate.is_file():
            continue
        if any(part in IGNORED_DIRECTORIES for part in candidate.parts):
            continue
        if candidate == asset_path or (source_path is not None and candidate == source_path):
            continue
        if candidate.suffix.lower() not in TEXT_EXTENSIONS:
            continue
        try:
            if candidate.stat().st_size > MAX_TEXT_FILE_SIZE:
                continue
            text = candidate.read_text(encoding="utf-8", errors="ignore").casefold()
        except OSError:
            continue
        if any(term and term in text for term in search_terms):
            references.append(candidate)

    return sorted(references, key=lambda path: str(path).casefold())


def _reference_summary(references, project_folder, limit=5):
    project_folder = Path(project_folder)
    lines = []
    for path in references[:limit]:
        try:
            lines.append(f"• {path.relative_to(project_folder)}")
        except ValueError:
            lines.append(f"• {path}")
    remaining = len(references) - len(lines)
    if remaining > 0:
        lines.append(f"• and {remaining} more")
    return "\n".join(lines)


def install_asset_usage_tracking():
    """Add usage counts and guarded deletion to ``ProjectAssetsPanel``."""
    if getattr(ProjectAssetsPanel, "_usage_tracking_installed", False):
        return

    original_select_asset = ProjectAssetsPanel.select_asset
    original_delete_selected = ProjectAssetsPanel.delete_selected

    def select_asset_with_usage(self, asset):
        original_select_asset(self, asset)
        references = find_asset_references(asset, self.project_folder)
        count = len(references)
        usage_text = "Not referenced by project files"
        if count == 1:
            usage_text = "Referenced by 1 project file"
        elif count > 1:
            usage_text = f"Referenced by {count} project files"
        current = self.details_label.cget("text") or ""
        self.details_label.configure(text=f"{current}\nUsage: {usage_text}")

    def delete_selected_with_usage(self):
        asset = self.selected_asset
        if asset is None:
            return

        references = find_asset_references(asset, self.project_folder)
        if not references:
            return original_delete_selected(self)

        summary = _reference_summary(references, self.project_folder)
        confirmed = ask_confirmation(
            self,
            title="Asset Is In Use",
            message=(
                f"{asset.name} is referenced by {len(references)} project "
                f"file{'s' if len(references) != 1 else ''}:\n\n"
                f"{summary}\n\n"
                "Deleting it may leave broken references. Delete it anyway?"
            ),
            confirm_text="Delete Anyway",
            cancel_text="Cancel",
            danger=True,
            width=620,
        )
        if not confirmed:
            return

        try:
            delete_project_asset(asset)
        except OSError as exc:
            messagebox.showerror("Delete Asset", str(exc), parent=self)
            return
        self.refresh_assets()

    ProjectAssetsPanel.select_asset = select_asset_with_usage
    ProjectAssetsPanel.delete_selected = delete_selected_with_usage
    ProjectAssetsPanel._usage_tracking_installed = True


__all__ = ["find_asset_references", "install_asset_usage_tracking"]
