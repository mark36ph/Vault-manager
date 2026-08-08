import customtkinter as ctk

from common.app_info import AppInfo
from pages.base_page import BasePage
from pages.settings.about_page import AboutPage
from pages.settings.ai_page import AIPage
from pages.settings.general_page import GeneralPage
from pages.settings.images_page import ImagesPage
from pages.settings.resolve_page import ResolvePage


class SettingsPage(BasePage):
    """Compact settings workspace with secondary navigation."""

    SIDEBAR_WIDTH = 188

    def __init__(self, parent, pm, app):
        self.nav_buttons = {}
        super().__init__(parent, pm, "Settings")
        self.app = app
        self.app_info = AppInfo()
        self.current_page = None

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="App preferences, providers, Resolve export, and version information.",
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

        ctk.CTkLabel(
            self.sidebar,
            text="PREFERENCES",
            font=("Segoe UI", 10, "bold"),
            text_color=("#98A2B3", "#717784"),
            anchor="w",
        ).pack(fill="x", padx=14, pady=(16, 8))

        self.add_button("general", "General", self.show_general)
        self.add_button("images", "Images", self.show_images)
        self.add_button("resolve", "DaVinci Resolve", self.show_resolve)
        self.add_button("ai", "AI", self.show_ai)
        self.add_button("about", "About", self.show_about)

        footer = ctk.CTkFrame(self.sidebar, fg_color="transparent")
        footer.pack(side="bottom", fill="x", padx=14, pady=14)
        self.footer_label = ctk.CTkLabel(
            footer,
            text=self._footer_text(),
            justify="left",
            anchor="w",
            font=("Segoe UI", 10),
            text_color=("#98A2B3", "#717784"),
        )
        self.footer_label.pack(fill="x")

        self.select_page("general", self.show_general)

    def _footer_text(self):
        name = self.app_info.get("name", "Fact Vault Manager")
        version = self.app_info.get("version", "1.0.0")
        return f"{name}\nv{version}"

    def refresh_footer(self):
        self.app_info = AppInfo()
        self.footer_label.configure(text=self._footer_text())

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

    def _show_page(self, page_class):
        self.clear()
        self.current_page = page_class(self.content, self.pm, self.app)
        self.current_page.pack(fill="both", expand=True, padx=1, pady=1)
        self.current_page.focus_set()

    def show_general(self):
        self._show_page(GeneralPage)

    def show_images(self):
        self._show_page(ImagesPage)

    def show_ai(self):
        self._show_page(AIPage)

    def show_resolve(self):
        self._show_page(ResolvePage)

    def show_about(self):
        self._show_page(AboutPage)
