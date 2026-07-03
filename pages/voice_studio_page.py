import customtkinter as ctk
from pages.base_page import BasePage
from tkinter import messagebox
from services.voice.voice_service import VoiceService


class VoiceStudioPage(BasePage):

    def __init__(self, parent, pm, app):

        super().__init__(parent, pm, "Voice Studio")
        self.app = app
        self.voice_service = VoiceService()
        self.placeholder = "Type or paste text here..."
        self.placeholder_color = ("gray50", "gray50")
        self.normal_color = ("black", "white")
        self.build()

    # =====================================

    def build(self):

        self.add_section_title("Voice Studio")
        self.build_generate_panel()
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

    def build_generate_panel(self):

        panel = ctk.CTkFrame(self)

        panel.pack(
            fill="x",
            padx=20,
            pady=(10, 5)
        )

        ctk.CTkLabel(
            panel,
            text="Generate Speech",
            font=("Segoe UI", 20, "bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(15, 10)
        )

        self.speech_text = ctk.CTkTextbox(
            panel,
            height=120
        )

        self.speech_text.pack(
            fill="x",
            padx=20,
            pady=(0, 10)
        )

        self.speech_text.insert(
            "1.0",
            self.placeholder
        )

        self.speech_text.configure(
            text_color=self.placeholder_color
        )

        self.speech_text.bind(
            "<FocusIn>",
            self.clear_placeholder
        )

        self.speech_text.bind(
            "<FocusOut>",
            self.restore_placeholder
        )

        btns = ctk.CTkFrame(
            panel,
            fg_color="transparent"
        )

        btns.pack(
            anchor="e",
            padx=20,
            pady=(0, 15)
        )

        ctk.CTkButton(
            btns,
            text="▶ Speak",
            width=160,
            command=self.speak_text
        ).pack(
            side="left",
            padx=5
        )

        ctk.CTkButton(
            btns,
            text="■ Stop",
            width=120,
            command=self.voice_service.stop_preview
        ).pack(
            side="left",
            padx=5
        )

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

            ctk.CTkButton(
                right,
                text="▶ Preview",
                width=120,
                command=lambda v=voice: self.preview_voice(v)
            ).pack(
                pady=(0, 6)
            )
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

    def preview_voice(self, voice):

        try:

            self.voice_service.preview_voice(
                voice.id
            )

        except Exception as e:

            messagebox.showerror(
                "Voice Preview",
                str(e)
            )

    def generate_speech(self):

        try:

            voice = self.voice_service.get_default_voice()

            if voice is None:

                messagebox.showerror(
                    "Generate Speech",
                    "No default voice selected."
                )

                return

            text = self.speech_text.get(
                "1.0",
                "end"
            ).strip()

            if not text or text == self.placeholder:

                messagebox.showerror(
                    "Generate Speech",
                    "Please enter some text first."
                )

                return

            output_file = (
                self.pm.get_voice_folder(
                    {
                        "title": "Voice Studio",
                        "status": "In Progress"
                    }
                )
                / "voice_studio.wav"
            )

            self.voice_service.generate_voice(
                voice.id,
                text,
                output_file
            )

            messagebox.showinfo(
                "Generate Speech",
                f"WAV created successfully:\n\n{output_file}"
            )

        except Exception as e:

            messagebox.showerror(
                "Generate Speech Failed",
                str(e)
            )

    def speak_text(self):

        try:

            voice = self.voice_service.get_default_voice()

            if voice is None:

                messagebox.showerror(
                    "Voice Studio",
                    "Please choose a default voice."
                )

                return

            text = self.speech_text.get(
                "1.0",
                "end"
            ).strip()

            if not text or text == self.placeholder:

                messagebox.showerror(
                    "Voice Studio",
                    "Please enter some text."
                )

                return

            self.voice_service.speak(
                voice.id,
                text
            )

        except Exception as e:

            messagebox.showerror(
                "Voice Studio",
                str(e)
            )

    def clear_placeholder(self, event=None):

        text = self.speech_text.get(
            "1.0",
            "end"
        ).strip()

        if text == self.placeholder:

            self.speech_text.delete(
                "1.0",
                "end"
            )

            self.speech_text.configure(
                text_color=self.normal_color
            )


    def restore_placeholder(self, event=None):

        text = self.speech_text.get(
            "1.0",
            "end"
        ).strip()

        if not text:

            self.speech_text.insert(
                "1.0",
                self.placeholder
            )

            self.speech_text.configure(
                text_color=self.placeholder_color
            )