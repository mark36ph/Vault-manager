import customtkinter as ctk

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


def install_production_settings_links():
    """Add provider-settings shortcuts to the Production setup panel."""
    if getattr(ProductionPage, "_settings_links_installed", False):
        return

    original = ProductionPage._build_controls

    def build_controls(page, parent):
        original(page, parent)

        # The existing action buttons all use pack inside this frame. Adding the
        # shortcuts there avoids mixing pack and grid in CustomTkinter's
        # scrollable-frame internals.
        buttons = page.open_button.master
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
    """Load Production with dictionary rows expected by its UI helpers."""
    original_get_all_projects = app.pm.get_all_projects

    def get_project_dicts():
        return [dict(project) for project in original_get_all_projects()]

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
