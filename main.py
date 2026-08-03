import time

import customtkinter as ctk
from tkinter import messagebox

from common.narration_sync import NarrationSyncError, regenerate_narration
from common.project_filters import in_progress_projects
from common.provider_setup import ProviderSettingsStore, build_configured_providers
from pages.media_library_page import MediaLibraryPage
from pages.production_page import ProductionPage
from pages.settings_page import SettingsPage
from widgets.asset_usage_tracking import install_asset_usage_tracking
from ui.dashboard import Dashboard


def add_media_library_button(app):
    """Add the Media Library button before Settings so it stays visible."""
    existing_widgets = list(app.sidebar.winfo_children())
    app.add_sidebar_button(
        "🖼 Media Library",
        lambda: app.load_page(MediaLibraryPage, app),
    )

    new_widgets = [
        widget for widget in app.sidebar.winfo_children()
        if widget not in existing_widgets
    ]
    if not new_widgets:
        return

    library_button = new_widgets[-1]
    settings_button = None
    for widget in existing_widgets:
        try:
            if widget.cget("text") == "⚙ Settings":
                settings_button = widget
                break
        except Exception:
            continue

    if settings_button is not None:
        library_button.pack_configure(before=settings_button)


def open_settings_section(app, section):
    """Open Settings directly on the requested provider section."""
    app.load_page(SettingsPage, app)
    page = app.current_page
    callback = getattr(page, f"show_{section}", None)
    if callback is not None:
        page.select_page(section, callback)


def regenerate_saved_narration(page):
    """Rebuild narration from the exact script stored in the projects database."""
    folder = page._project_folder()
    project = page._selected_project()
    if folder is None or project is None:
        messagebox.showerror("Narration", "Select a valid project.")
        return
    try:
        settings = ProviderSettingsStore(folder).load()
        configured = build_configured_providers(folder, settings)
        speech_provider = configured.registry.require("voice")
        script = str(project.get("script") or "")
        page._append_log("Regenerating narration from the database script")
        result = regenerate_narration(folder, script, speech_provider)
        page._append_log(
            f"Narration regenerated from {result.word_count} words: {result.audio_path.name}"
        )
        messagebox.showinfo(
            "Narration regenerated",
            "Narration now matches the script stored in the project database.\n\n"
            "Use Export to Resolve Free again to rebuild the portable package.",
        )
    except (NarrationSyncError, OSError, RuntimeError, ValueError) as error:
        page._append_log(f"Narration regeneration failed: {error}")
        messagebox.showerror("Narration", str(error))


def install_production_settings_links():
    """Add provider-settings and narration shortcuts to Production."""
    if getattr(ProductionPage, "_settings_links_installed", False):
        return

    original = ProductionPage._build_controls

    def build_controls(page, parent):
        original(page, parent)

        buttons = page.open_button.master
        ctk.CTkButton(
            buttons,
            text="🎙 Regenerate Narration from Script",
            command=lambda: regenerate_saved_narration(page),
        ).pack(fill="x", pady=3)
        ctk.CTkLabel(
            buttons,
            text="Update API keys",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", pady=(12, 6))
        ctk.CTkButton(
            buttons,
            text="🤖 Open AI Settings",
            command=lambda: open_settings_section(page.app, "ai"),
        ).pack(fill="x", pady=3)
        ctk.CTkButton(
            buttons,
            text="🖼 Open Image API Settings",
            command=lambda: open_settings_section(page.app, "images"),
        ).pack(fill="x", pady=3)

    ProductionPage._build_controls = build_controls
    ProductionPage._settings_links_installed = True


def load_production_page(app):
    """Load Production with only In Progress projects as dictionaries."""
    original_get_all_projects = app.pm.get_all_projects

    def get_project_dicts():
        return in_progress_projects(dict(project) for project in original_get_all_projects())

    app.pm.get_all_projects = get_project_dicts
    try:
        app.load_page(ProductionPage, app)
    finally:
        app.pm.get_all_projects = original_get_all_projects


def add_production_button(app):
    """Add the Production button before Media Library and Settings."""
    existing_widgets = list(app.sidebar.winfo_children())
    app.add_sidebar_button(
        "🎬 Production",
        lambda: load_production_page(app),
    )

    new_widgets = [
        widget for widget in app.sidebar.winfo_children()
        if widget not in existing_widgets
    ]
    if not new_widgets:
        return

    production_button = new_widgets[-1]
    before_button = None
    for widget in existing_widgets:
        try:
            if widget.cget("text") in {"🖼 Media Library", "⚙ Settings"}:
                before_button = widget
                break
        except Exception:
            continue

    if before_button is not None:
        production_button.pack_configure(before=before_button)


ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
install_asset_usage_tracking()
install_production_settings_links()

app = Dashboard()
add_media_library_button(app)
add_production_button(app)
app.mainloop()
