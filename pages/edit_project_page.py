from tkinter import messagebox
import os
import customtkinter as ctk
from pages.base_page import BasePage

class EditProjectPage(BasePage):
    def __init__(self, parent, pm, app, project_id):
        super().__init__(parent, pm, "Edit Project")
        self.app = app
        self.project_id = project_id
        self.project = self.pm.db.get_project(project_id)
        if not self.project:
            messagebox.showerror("Error","Project not found.")
            self.app.show_projects()
            return
        self.build()
        self.load_project()

    def build(self):
        self.form = ctk.CTkScrollableFrame(self.content)
        self.form.pack(fill="both", expand=True)

        ctk.CTkLabel(self.form,text="Project Title").pack(anchor="w",padx=15,pady=(15,5))
        self.title_entry=ctk.CTkEntry(self.form,width=500)
        self.title_entry.pack(anchor="w",padx=15)

        ctk.CTkLabel(self.form,text="Category").pack(anchor="w",padx=15,pady=(15,5))
        self.category=ctk.CTkOptionMenu(self.form,values=self.pm.db.get_categories() or ["Misc"],width=220)
        self.category.pack(anchor="w",padx=15)

        ctk.CTkLabel(self.form,text="Status").pack(anchor="w",padx=15,pady=(15,5))
        self.status=ctk.CTkOptionMenu(self.form,values=["In Progress","Completed","Scheduled"],width=220)
        self.status.pack(anchor="w",padx=15)

        btns=ctk.CTkFrame(self.form,fg_color="transparent")
        btns.pack(anchor="w",padx=15,pady=20)
        ctk.CTkButton(btns,text="📂 Open Folder",command=self.open_folder).pack(side="left",padx=5)
        ctk.CTkButton(btns,text="💾 Save",command=self.save_project).pack(side="left",padx=5)
        ctk.CTkButton(btns,text="← Back",command=self.app.show_projects).pack(side="left",padx=5)

        for label,attr,h in [("Script","script",250),("Description","description",120),("Pinned Comment","pinned_comment",120),("Notes","notes",150)]:
            ctk.CTkLabel(self.form,text=label,font=("Segoe UI",18,"bold")).pack(anchor="w",padx=15,pady=(20,5))
            box=ctk.CTkTextbox(self.form,width=900,height=h)
            box.pack(fill="x",padx=15)
            setattr(self,attr,box)

    def open_folder(self):
        try:
            os.startfile(self.project["folder"])
        except Exception as e:
            messagebox.showerror("Error",str(e))

    def load_project(self):
        self.title_entry.insert(0,self.project["title"])
        self.category.set(self.project["category"])
        self.status.set(self.project["status"])
        self.script.insert("1.0",self.project["script"] or "")
        self.description.insert("1.0",self.project["description"] or "")
        self.pinned_comment.insert("1.0",self.project["pinned_comment"] or "")
        self.notes.insert("1.0",self.project["notes"] or "")

    def save_project(self):
        self.pm.db.update_project(
            self.project_id,
            self.title_entry.get().strip(),
            self.category.get(),
            self.status.get(),
            self.script.get("1.0","end").strip(),
            self.description.get("1.0","end").strip(),
            self.pinned_comment.get("1.0","end").strip(),
            self.notes.get("1.0","end").strip()
        )
        messagebox.showinfo("Saved","Project saved successfully.")
        self.app.show_projects()
