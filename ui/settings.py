import customtkinter as ctk
from tkinter import filedialog, messagebox
from pathlib import Path
import json

SETTINGS_FILE = Path("data/settings.json")

class SettingsWindow(ctk.CTkToplevel):

    def __init__(self,parent):
        super().__init__(parent)
        
        self.transient(parent)
        self.lift()
        self.focus_force()
        self.grab_set()

        self.title("Settings")
        self.geometry("700x250")

        ctk.CTkLabel(
            self,
            text="Settings",
            font=("Segoe UI",26,"bold")
        ).pack(pady=15)

        frame=ctk.CTkFrame(self)
        frame.pack(fill="x",padx=20,pady=10)

        ctk.CTkLabel(frame,text="Projects Folder").grid(row=0,column=0,padx=10,pady=10,sticky="w")

        self.entry=ctk.CTkEntry(frame,width=420)
        self.entry.grid(row=0,column=1,padx=10)

        ctk.CTkButton(
            frame,
            text="Browse",
            command=self.browse
        ).grid(row=0,column=2,padx=10)

        self.status=ctk.CTkLabel(self,text="",text_color="lightgreen")
        self.status.pack()

        ctk.CTkButton(
            self,
            text="Save Settings",
            command=self.save
        ).pack(pady=15)

        self.load()

    def browse(self):
        folder=filedialog.askdirectory()
        if folder:
            self.entry.delete(0,"end")
            self.entry.insert(0,folder)

    def load(self):
        try:
            data=json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
            self.entry.delete(0,"end")
            self.entry.insert(0,data.get("projects_folder",""))
        except Exception:
            pass

    def save(self):
        data={
            "projects_folder":self.entry.get(),
            "theme":"dark"
        }
        SETTINGS_FILE.parent.mkdir(exist_ok=True)
        SETTINGS_FILE.write_text(json.dumps(data,indent=4),encoding="utf-8")
        self.status.configure(text="âœ” Settings saved successfully")
        messagebox.showinfo("Saved","Settings saved.")
