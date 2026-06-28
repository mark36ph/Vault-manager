import customtkinter as ctk
from ui.dashboard import Dashboard

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

app = Dashboard()
app.mainloop()