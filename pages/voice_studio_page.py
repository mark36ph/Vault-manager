import customtkinter as ctk
from pages.base_page import BasePage
from tkinter import messagebox
from services.voice.voice_service import VoiceService


class VoiceStudioPage(BasePage):

    def __init__(self, parent, pm, app):

        super().__init__(parent, pm, "Voice Studio")

        self.app = app

        self.voice_service = VoiceService()

        self.build()

    # =====================================

    def build(self):

        self.add_section_title("Voice Studio")

        self.installed_frame = ctk.CTkFrame(self)

        self.installed_frame.pack(
            fill="x",
            padx=20,
            pady=10
        )

        self.available_frame = ctk.CTkFrame(self)

        self.available_frame.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=10
        )

        self.progress = ctk.CTkProgressBar(self)

        self.progress.pack(
            fill="x",
            padx=20,
            pady=(10, 0)
        )

        self.progress.set(0)

        self.progress.pack_forget()

        self.status = ctk.CTkLabel(
            self,
            text=""
        )

        self.status.pack()

        self.status.pack_forget()

        self.build_installed()

        self.build_available()

    # =====================================

    def build_installed(self):

        ctk.CTkLabel(
            self.installed_frame,
            text="Installed Voices",
            font=("Segoe UI",20,"bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(15,15)
        )

        default = self.voice_service.get_default_voice()

        for voice in self.voice_service.get_available_voices():

            if not voice.installed:
                continue

            card = ctk.CTkFrame(self.installed_frame)

            card.pack(
                fill="x",
                padx=20,
                pady=5
            )

            left = ctk.CTkFrame(
                card,
                fg_color="transparent"
            )

            left.pack(
                side="left",
                padx=15,
                pady=10
            )

            ctk.CTkLabel(
                left,
                text=voice.display_name,
                font=("Segoe UI",18,"bold")
            ).pack(anchor="w")

            ctk.CTkLabel(
                left,
                text=f"{voice.language} • {voice.quality}",
                text_color="gray"
            ).pack(anchor="w")

            right = ctk.CTkFrame(
                card,
                fg_color="transparent"
            )

            right.pack(
                side="right",
                padx=15
            )

            if default and default.id == voice.id:

                badge = ctk.CTkLabel(
                    right,
                    text="★ DEFAULT",
                    fg_color="#1f6aa5",
                    corner_radius=8,
                    padx=12
                )

                badge.pack(
                    pady=8
                )

            else:

                ctk.CTkButton(
                    right,
                    text="⭐ Set Default",
                    width=120,
                    command=lambda v=voice: self.set_default_voice(v)
                ).pack()
                ctk.CTkButton(
                    right,
                    text="🗑 Delete",
                    fg_color="#AA3333",
                    hover_color="#882222",
                    width=120,
                    command=lambda v=voice: self.delete_voice(v)
                ).pack(
                    pady=(6, 0)
                )

    # =====================================

    def build_available(self):

        ctk.CTkLabel(
            self.available_frame,
            text="Available Voices",
            font=("Segoe UI",20,"bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(15,15)
        )

        for voice in self.voice_service.get_available_voices():

            if voice.installed:
                continue

            card = ctk.CTkFrame(self.available_frame)

            card.pack(
                fill="x",
                padx=20,
                pady=5
            )

            left = ctk.CTkFrame(
                card,
                fg_color="transparent"
            )

            left.pack(
                side="left",
                padx=15,
                pady=10
            )

            ctk.CTkLabel(
                left,
                text=voice.display_name,
                font=("Segoe UI",18,"bold")
            ).pack(anchor="w")

            ctk.CTkLabel(
                left,
                text=f"{voice.language} • {voice.quality}",
                text_color="gray"
            ).pack(anchor="w")

            ctk.CTkButton(
                card,
                text="⬇ Download",
                width=120,
                command=lambda v=voice: self.download_voice(v)
            ).pack(
                side="right",
                padx=15
            )

    def download_voice(self, voice):

        self.progress.pack(
            fill="x",
            padx=20,
            pady=10
        )

        self.status.pack()

        self.progress.set(0)

        self.status.configure(
            text=f"Downloading {voice.display_name}..."
        )

        self.update()

        try:

            self.voice_service.download_voice(
                voice.id,
                self.download_progress
            )

            self.progress.set(1)

            self.status.configure(
                text="Download complete."
            )

        except Exception as e:

            self.status.configure(
                text=str(e)
            )

            return

        self.status.configure(
            text="Download complete."
        )

        self.after(
            500,
            lambda: self.app.show_voice_studio()
        )

    def refresh(self):

        self.app.show_voice_studio()

    def download_progress(
        self,
        downloaded,
        total,
        percent
    ):

        self.progress.set(percent)

        if total > 0:

            mb_done = downloaded / (1024 * 1024)
            mb_total = total / (1024 * 1024)

            self.status.configure(
                text=f"{mb_done:.1f} MB / {mb_total:.1f} MB"
            )

        self.update_idletasks()

    def set_default_voice(self, voice):

        try:

            self.voice_service.set_default_voice(
                voice.id
            )

            self.app.show_voice_studio()

        except Exception as e:

            from tkinter import messagebox

            messagebox.showerror(
                "Voice Studio",
                str(e)
            )
    from tkinter import messagebox
    def delete_voice(self, voice):

        if not messagebox.askyesno(

            "Delete Voice",

            f"Delete '{voice.display_name}'?\n\n"
            "This will remove the downloaded files."

        ):
            return

        try:

            self.voice_service.delete_voice(
                voice.id
            )

            self.app.show_voice_studio()

        except Exception as e:

            messagebox.showerror(
                "Voice Studio",
                str(e)
            )