import customtkinter as ctk

from pages.media_library_page import MediaLibraryPage
from widgets.asset_usage_tracking import install_asset_usage_tracking
from ui.dashboard import Dashboard

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
install_asset_usage_tracking()

app = Dashboard()
app.add_sidebar_button(
    "🖼 Media Library",
    lambda: app.load_page(MediaLibraryPage, app),
)
app.mainloop()
