from pages.base_page import BasePage


class SettingsPage(BasePage):

    def __init__(self, parent, pm, app):

        super().__init__(parent, pm, "Settings")

        self.app = app

        self.build()

    def build(self):

        self.add_section_title("Application Settings")