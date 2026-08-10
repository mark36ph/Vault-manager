import time
from tkinter import messagebox

import customtkinter as ctk
import pages.production_page as production_page_module

from common.asset_visual_verification import OpenAIImageRelevanceVerifier
from common.fcpxml_paths import rebase_fcpxml_media_paths
from common.narration_sync import NarrationSyncError, regenerate_narration
from common.project_filters import in_progress_projects
from common.provider_setup import ProviderCredentials, ProviderSettingsStore, build_configured_providers
from common.verified_asset_acquisition import install_visual_verification
from pages.media_library_page import MediaLibraryPage
from pages.production_page import ProductionPage, format_elapsed
from pages.settings_page import SettingsPage
from widgets.asset_usage_tracking import install_asset_usage_tracking
from widgets.message_dialog import ask_confirmation, show_message
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
        show_message(page, "Narration", "Select a valid project.", kind="error")
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
        show_message(
            page,
            "Narration regenerated",
            "Narration now matches the script stored in the project database.\n\n"
            "Use Export to Resolve Free again to rebuild the portable package.",
            kind="success",
        )
    except (NarrationSyncError, OSError, RuntimeError, ValueError) as error:
        page._append_log(f"Narration regeneration failed: {error}")
        show_message(page, "Narration", str(error), kind="error")


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


def install_production_elapsed_timer():
    """Keep the Production elapsed-time label ticking from the moment a run starts."""
    if getattr(ProductionPage, "_elapsed_timer_installed", False):
        return

    def tick_elapsed(page):
        if page.run_started_at is None:
            return

        page.elapsed_label.configure(
            text=f"Elapsed {format_elapsed(time.monotonic() - page.run_started_at)}"
        )
        page.after(1000, page._tick_elapsed)

    ProductionPage._tick_elapsed = tick_elapsed
    ProductionPage._elapsed_timer_installed = True


def install_production_status_guard():
    """Keep completed projects viewable without allowing an accidental rerun."""
    if getattr(ProductionPage, "_status_guard_installed", False):
        return

    original_refresh_credentials = ProductionPage._refresh_credentials
    original_start = ProductionPage._start

    def refresh_credentials(page, settings):
        original_refresh_credentials(page, settings)
        project = page._selected_project()
        if project is not None and str(project.get("status") or "") != "In Progress":
            page.start_button.configure(state="disabled")

    def start(page, *, resume: bool):
        project = page._selected_project()
        if project is not None and str(project.get("status") or "") != "In Progress":
            show_message(
                page,
                "Production",
                "This project is already complete. Move it back to In Progress before producing it again.",
                kind="info",
            )
            return
        return original_start(page, resume=resume)

    ProductionPage._refresh_credentials = refresh_credentials
    ProductionPage._start = start
    ProductionPage._status_guard_installed = True


def install_production_visual_verification():
    """Verify downloaded production images with OpenAI before they enter the timeline."""
    if getattr(production_page_module, "_visual_verification_installed", False):
        return

    original_builder = production_page_module.build_configured_providers

    def build_with_visual_verification(project_folder, settings, **options):
        configured = original_builder(project_folder, settings, **options)
        if str(getattr(settings, "asset_kind", "image")) != "image":
            return configured

        credentials = options.get("credentials") or ProviderCredentials()
        verifier = OpenAIImageRelevanceVerifier(
            credentials.get("openai"),
            model=str(getattr(settings, "openai_model", "gpt-5-mini") or "gpt-5-mini"),
        )
        install_visual_verification(configured.asset_engine, verifier)
        return configured

    production_page_module.build_configured_providers = build_with_visual_verification
    production_page_module._visual_verification_installed = True


def install_status_move_fcpxml_rebase(app):
    """Rebase generated Resolve file URIs whenever a project status move changes its folder."""
    original_change_status = app.pm.change_project_status

    def change_project_status(project_id, new_status, scheduled_for=""):
        before = app.pm.db.get_project(project_id)
        old_folder = app.pm.resolve_project_folder(before) if before is not None else None

        updated = original_change_status(project_id, new_status, scheduled_for)

        if old_folder is not None and updated is not None:
            try:
                new_folder = app.pm.resolve_project_folder(updated)
                rebase_fcpxml_media_paths(new_folder, old_folder, new_folder)
            except Exception as error:
                print(f"Could not rebase Resolve FCPXML media paths: {error}")

        return updated

    app.pm.change_project_status = change_project_status


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


def install_legacy_messagebox_bridge(app):
    """Keep legacy tkinter messagebox calls visually consistent with the app."""

    def parent_for(kwargs):
        return kwargs.get("parent") or app

    def show_blocking(title, message, kind, kwargs):
        parent = parent_for(kwargs)
        dialog = show_message(parent, title, message, kind=kind)
        parent.wait_window(dialog)
        return "ok"

    def show_info(title, message, **kwargs):
        return show_blocking(title, message, "info", kwargs)

    def show_success(title, message, **kwargs):
        return show_blocking(title, message, "success", kwargs)

    def show_warning(title, message, **kwargs):
        return show_blocking(title, message, "warning", kwargs)

    def show_error(title, message, **kwargs):
        return show_blocking(title, message, "error", kwargs)

    def ask_yes_no(title, message, **kwargs):
        return ask_confirmation(
            parent_for(kwargs),
            title,
            message,
            confirm_text="Yes",
            cancel_text="No",
            kind="warning",
        )

    messagebox.showinfo = show_info
    messagebox.showwarning = show_warning
    messagebox.showerror = show_error
    messagebox.askyesno = ask_yes_no
    messagebox.askokcancel = ask_yes_no


ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
install_asset_usage_tracking()
install_production_settings_links()
install_production_elapsed_timer()
install_production_status_guard()
install_production_visual_verification()

app = Dashboard()
install_status_move_fcpxml_rebase(app)
install_legacy_messagebox_bridge(app)
add_media_library_button(app)
add_production_button(app)
app.mainloop()
