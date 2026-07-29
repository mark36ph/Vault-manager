import customtkinter as ctk

from widgets.asset_usage_tracking import install_asset_usage_tracking
from ui.dashboard import Dashboard

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
install_asset_usage_tracking()

app = Dashboard()
app.mainloop()
