import customtkinter as ctk
ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")
app=ctk.CTk()
app.title("Fact Vault Manager")
app.geometry("1200x700")
ctk.CTkLabel(app,text="Fact Vault Manager v0.1",font=("Segoe UI",28,"bold")).pack(pady=40)
app.mainloop()
