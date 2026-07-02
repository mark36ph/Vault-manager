import requests
import webbrowser
from packaging import version

from common.app_info import AppInfo


VERSION_URL = "https://raw.githubusercontent.com/mark36ph/Vault-manager/refs/heads/main/version.json"


class UpdateManager:

    def __init__(self):

        self.app_info = AppInfo()

    def get_current_version(self):

        return self.app_info.get(
            "version",
            "0.0.0"
        )

    def get_latest_info(self):

        response = requests.get(
            VERSION_URL,
            timeout=10
        )

        response.raise_for_status()

        return response.json()

    def is_update_available(self):

        latest = self.get_latest_info()

        current_version = self.get_current_version()

        latest_version = latest.get(
            "latest_version",
            "0.0.0"
        )

        return version.parse(latest_version) > version.parse(current_version)

    def check_for_updates(self):

        latest = self.get_latest_info()

        current_version = self.get_current_version()

        latest_version = latest.get(
            "latest_version",
            "0.0.0"
        )

        return {
            "current_version": current_version,
            "latest_version": latest_version,
            "update_available": version.parse(latest_version) > version.parse(current_version),
            "release_notes": latest.get("release_notes", ""),
            "download_url": latest.get("download_url", "")
        }

    def open_download_page(self, url):

        if url:
            webbrowser.open(url)