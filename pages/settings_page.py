import customtkinter as ctk

from pages.base_page import BasePage
from pages.settings.general_page import GeneralPage


class SettingsPage(BasePage):

    def __init__(self, parent, pm, app):
        self.nav_buttons = {}
        super().__init__(parent, pm, "Settings")
        self.app = app
        self.build()

    def build(self):

        self.sidebar = ctk.CTkFrame(
            self,
            width=240,
            corner_radius=10
        )

        self.sidebar.pack_propagate(False)

        self.sidebar.pack(
            side="left",
            fill="y",
            padx=(20, 10),
            pady=(10, 20)
        )

        self.content.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0, 20),
            pady=(10, 20)
        )

        self.current_page = None

        ctk.CTkLabel(
            self.sidebar,
            text="⚙ Settings",
            font=("Segoe UI", 24, "bold")
        ).pack(
            anchor="w",
            padx=20,
            pady=(20, 25)
        )

        ctk.CTkFrame(
            self.sidebar,
            height=2
        ).pack(
            fill="x",
            padx=15,
            pady=(0, 15)
        )

        self.add_button(
            "general",
            "📁 General",
            self.show_general
        )

        self.add_button(
            "voice",
            "🎤 Voice",
            lambda: None
        )

        self.add_button(
            "ai",
            "🤖 AI",
            lambda: None
        )

        self.add_button(
            "youtube",
            "🎬 YouTube",
            lambda: None
        )

        self.add_button(
            "appearance",
            "🎨 Appearance",
            lambda: None
        )

        self.add_button(
            "projects",
            "📂 Projects",
            lambda: None
        )

        self.add_button(
            "advanced",
            "🔧 Advanced",
            lambda: None
        )

        self.add_button(
            "about",
            "ℹ About",
            lambda: None
        )

        self.select_page(
            "general",
            self.show_general
        )

        footer = ctk.CTkFrame(
            self.sidebar,
            fg_color="transparent"
        )

        footer.pack(
            side="bottom",
            fill="x",
            padx=15,
            pady=15
        )

        ctk.CTkLabel(
            footer,
            text=(
                f"{self.app.settings.get('general', 'app_name', 'Fact Vault Manager')}\n"
                f"Version {self.app.settings.get('general', 'version', '1.0.0')}"
            ),
            justify="left",
            font=("Segoe UI", 11)
        ).pack(anchor="w")

    def add_button(self, key, text, command):

        button = ctk.CTkButton(
            self.sidebar,
            text=text,
            anchor="w",
            height=42,
            corner_radius=8,
            fg_color="transparent",
            hover_color=("gray85", "#2B2B2B"),
            text_color=("black", "white"),
            command=lambda: self.select_page(key, command)
        )

        button.pack(
            fill="x",
            padx=10,
            pady=2
        )

        self.nav_buttons[key] = button

    def select_page(self, key, callback):

        for name, button in self.nav_buttons.items():

            if name == key:

                button.configure(
                    fg_color=("gray75", "#1f538d")
                )

            else:

                button.configure(
                    fg_color="transparent"
                )

        callback()
    
    def clear(self):

        if self.current_page:

            self.current_page.destroy()

            self.current_page = None

    def show_general(self):

        self.clear()

        self.current_page = GeneralPage(
            self.content,
            self.pm,
            self.app
        )

        self.current_page.pack(
            fill="both",
            expand=True
        )
        self.current_page.focus_set()