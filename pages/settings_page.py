import customtkinter as ctk

from pages.base_page import BasePage
from pages.settings.about_page import AboutPage
from pages.settings.ai_page import AIPage
from pages.settings.general_page import GeneralPage
from pages.settings.images_page import ImagesPage
from pages.settings.integrity_page import IntegrityPage
from pages.settings.resolve_page import ResolvePage


class SettingsPage(BasePage):
    """Compact settings workspace with secondary navigation."""

    SIDEBAR_WIDTH = 188

    def __init__(self, parent, pm, app):
        self.nav_buttons = {}
        super().__init__(parent, pm, "Settings")
        self.app = app
        self.current_page = None

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="App preferences, project integrity, providers, Resolve export, and version information.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)

        self.build()

    def build(self):
        self.content.pack_forget()

        workspace = ctk.CTkFrame(self, fg_color="transparent")
        workspace.pack(fill="both", expand=True, padx=24, pady=(0, 20))
        workspace.grid_columnconfigure(1, weight=1)
        workspace.grid_rowconfigure(0, weight=1)

        self.sidebar = ctk.CTkFrame(
            workspace,
            width=self.SIDEBAR_WIDTH,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        self.sidebar.grid(row=0, column=0, sticky="nsw", padx=(0, 12))
        self.sidebar.grid_propagate(False)

        self.content = ctk.CTkFrame(
            workspace,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        self.content.grid(row=0, column=1, sticky="nsew")
        self.content.grid_columnconfigure(0, weight=1)
        self.content.grid_rowconfigure(0, weight=1)

        ctk.CTkLabel(
            self.sidebar,
            text="PREFERENCES",
            font=("Segoe UI", 10, "bold"),
            text_color=("#98A2B3", "#717784"),
            anchor="w",
        ).pack(fill="x", padx=14, pady=(16, 8))

        pages = {
            "general": self.show_general,
            "integrity": self.show_integrity,
            "images": self.show_images,
            "resolve": self.show_resolve,
            "ai": self.show_ai,
            "about": self.show_about,
        }

        self.add_button("general", "General", self.show_general)
        self.add_button("integrity", "Project Integrity", self.show_integrity)
        self.add_button("images", "Images", self.show_images)
        self.add_button("resolve", "DaVinci Resolve", self.show_resolve)
        self.add_button("ai", "AI", self.show_ai)
        self.add_button("about", "About", self.show_about)

        saved_page = str(getattr(self.app, "_settings_selected_page", "general") or "general")
        if saved_page not in pages:
            saved_page = "general"
        self.select_page(saved_page, pages[saved_page])

    def add_button(self, key, text, command):
        button = ctk.CTkButton(
            self.sidebar,
            text=text,
            anchor="w",
            height=36,
            corner_radius=6,
            border_width=0,
            fg_color="transparent",
            hover_color=("#F2F4F7", "#252A33"),
            text_color=("#344054", "#D0D5DD"),
            font=("Segoe UI", 13),
            command=lambda: self.select_page(key, command),
        )
        button.pack(fill="x", padx=8, pady=1)
        self.nav_buttons[key] = button

    def select_page(self, key, callback):
        self.app._settings_selected_page = key
        for name, button in self.nav_buttons.items():
            if name == key:
                button.configure(
                    fg_color=("#EAF1FF", "#24344D"),
                    text_color=("#175CD3", "#AFCBFF"),
                    hover_color=("#E2ECFC", "#2B3D59"),
                )
            else:
                button.configure(
                    fg_color="transparent",
                    text_color=("#344054", "#D0D5DD"),
                    hover_color=("#F2F4F7", "#252A33"),
                )
        callback()

    def clear(self):
        if self.current_page:
            self.current_page.destroy()
            self.current_page = None

    def _show_page(self, page_class, *, top_only=False):
        self.clear()
        self.current_page = page_class(self.content, self.pm, self.app)
        self.current_page.grid(
            row=0,
            column=0,
            sticky="new" if top_only else "nsew",
            padx=1,
            pady=1,
        )
        self.current_page.focus_set()

    def show_general(self):
        self._show_page(GeneralPage)

    def show_integrity(self):
        self._show_page(IntegrityPage)

    def show_images(self):
        self._show_page(ImagesPage)

    def show_ai(self):
        self._show_page(AIPage)

    def show_resolve(self):
        self._show_page(ResolvePage)

    def show_about(self):
        self._show_page(AboutPage, top_only=True)
