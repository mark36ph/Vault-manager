from pages.base_page import BasePage


class StatisticsPage(BasePage):

    def __init__(self, parent, pm, app):

        super().__init__(parent, pm, "Statistics")

        self.app = app

        self.build()

    def build(self):

        self.add_section_title("Application Settings")