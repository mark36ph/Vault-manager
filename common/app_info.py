from pathlib import Path

from common.json_utils import (
    load_json,
    save_json
)


APP_INFO_FILE = Path("data") / "app.json"


DEFAULT_APP_INFO = {

    "name": "Fact Vault Manager",

    "version": "1.0.0",

    "developer": "Mark",

    "company": "",

    "website": "",

    "support_email": "",

    "build": 1

}


class AppInfo:

    _instance = None

    def __new__(cls):

        if cls._instance is None:

            cls._instance = super().__new__(cls)

        return cls._instance

    def __init__(self):

        if hasattr(self, "_loaded"):
            return

        self.info = load_json(
            APP_INFO_FILE,
            DEFAULT_APP_INFO
        )

        self._loaded = True

    def get(self, key, default=None):

        return self.info.get(
            key,
            default
        )

    def set(self, key, value):

        self.info[key] = value

        save_json(
            APP_INFO_FILE,
            self.info
        )

    def all(self):

        return self.info