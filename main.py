import customtkinter as ctk

from pages.media_library_page import MediaLibraryPage
from pages.production_page import ProductionPage
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

app = Dashboard()
add_media_library_button(app)
add_production_button(app)
app.mainloop()
