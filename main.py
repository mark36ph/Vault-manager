import customtkinter as ctk

from pages.media_library_page import MediaLibraryPage
from pages.production_page import ProductionPage
from widgets.asset_usage_tracking import install_asset_usage_tracking
from ui.dashboard import Dashboard


def _insert_sidebar_button(app, text, command, *, before_text="⚙ Settings"):
    """Add a sidebar button and keep it before Settings."""
    existing_widgets = list(app.sidebar.winfo_children())
    app.add_sidebar_button(text, command)

    new_widgets = [
        widget for widget in app.sidebar.winfo_children()
        if widget not in existing_widgets
    ]
    if not new_widgets:
        return

    new_button = new_widgets[-1]
    target_button = None
    for widget in existing_widgets:
        try:
            if widget.cget("text") == before_text:
                target_button = widget
                break
        except Exception:
            continue

    if target_button is not None:
        new_button.pack_configure(before=target_button)


def add_feature_buttons(app):
    _insert_sidebar_button(
        app,
        "🎬 Production",
        lambda: app.load_page(ProductionPage, app),
    )
    _insert_sidebar_button(
        app,
        "🖼 Media Library",
        lambda: app.load_page(MediaLibraryPage, app),
    )


ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
install_asset_usage_tracking()

app = Dashboard()
add_feature_buttons(app)
app.mainloop()
