import customtkinter as ctk
from pages.base_page import BasePage
from services.voice.voice_service import VoiceService

class VoicePage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Voice Studio")
        self.app = app
        self.voice_service = VoiceService()
        self.build()

    def build(self):

        # ===================================
        # Header
        # ===================================

        header = ctk.CTkFrame(self.content)
        header.pack(fill="x", padx=20, pady=20)

        ctk.CTkLabel(        
            header,
            text="Voice Studio",
            font=("Segoe UI", 30, "bold")
        ).pack(anchor="w", padx=15, pady=(15, 5))

        ctk.CTkLabel(
            header,
            text="Manage installed Piper voices",
            text_color="gray"
        ).pack(anchor="w", padx=15, pady=(0, 15))

        # ===================================
        # Toolbar
        # ===================================

        toolbar = ctk.CTkFrame(self.content)
        toolbar.pack(fill="x", padx=20)

        self.search = ctk.CTkEntry(
            toolbar,
            placeholder_text="Search voices..."
        )

        self.search.pack(
            side="left",
            fill="x",
            expand=True,
            padx=(10, 5),
            pady=10
        )

        self.search.bind(
            "<KeyRelease>",
            lambda e: self.load_voices()
        )

        ctk.CTkButton(
            toolbar,
            text="Refresh",
            command=self.load_voices
        ).pack(
            side="right",
            padx=10
        )

        # ===================================
        # Voice List
        # ===================================

        self.voice_list = ctk.CTkScrollableFrame(
            self.content
        )

        self.voice_list.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=15
        )

        self.load_voices()

    def load_voices(self):

        for widget in self.voice_list.winfo_children():
            widget.destroy()

        search = self.search.get().lower()

        voices = self.voice_service.get_installed_voices()

        if not voices:

            ctk.CTkLabel(
                self.voice_list,
                text="No Piper voices installed.",
                text_color="gray"
            ).pack(pady=30)

            return

        for voice in voices:

            if search not in voice.lower():
                continue

            card = ctk.CTkFrame(self.voice_list)

            card.pack(
                fill="x",
                padx=10,
                pady=8
            )

            ctk.CTkLabel(
                card,
                text=voice,
                font=("Segoe UI", 20, "bold")
            ).pack(
                anchor="w",
                padx=20,
                pady=(15, 5)
            )

            buttons = ctk.CTkFrame(
                card,
                fg_color="transparent"
            )

            buttons.pack(
                fill="x",
                padx=15,
                pady=(0, 15)
            )

            ctk.CTkButton(
                buttons,
                text="Preview",
                width=100,
                command=lambda v=voice: self.preview_voice(v)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="Set Default",
                width=110,
                command=lambda v=voice: self.set_default(v)
            ).pack(side="left", padx=5)

            ctk.CTkButton(
                buttons,
                text="Delete",
                width=90,
                fg_color="#B22222",
                hover_color="#8B0000",
                command=lambda v=voice: self.delete_voice(v)
            ).pack(side="right", padx=5)

    def preview_voice(self, voice):
        print("Preview:", voice)


    def set_default(self, voice):
        print("Default:", voice)


    def delete_voice(self, voice):
        print("Delete:", voice)