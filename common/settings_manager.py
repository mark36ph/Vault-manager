from pathlib import Path
from copy import deepcopy
from common.json_utils import (
    load_json,
    save_json
)

SETTINGS_FILE = Path("data") / "settings.json"


DEFAULT_SETTINGS = {

    "general": {
        "projects_folder": "",
        "theme": "dark",
        "start_maximized": True,
        "remember_last_project": True,
        "check_updates": True,
        "app_name": "Fact Vault Manager",
        "version": "1.0.0",
        "default_export_folder": ""
    },

    "images": {
        "provider": "Pixabay",
        "pixabay_api_key": "",
        "pexels_api_key": "",
        "default_orientation": "vertical",
    },

    "voice": {
        "engine": "Piper",
        "default_voice": "",
        "speech_rate": 1.0,
        "speech_pitch": 1.0,
        "volume": 1.0,
        "output_format": "wav"
    },

    "youtube": {
        "default_channel": "",
        "export_folder": ""
    },

    "ai": {
        "provider": "OpenAI",
        "api_key": "",
        "model": "",
        "temperature": 0.7
    }
}

class SettingsManager:
    _instance = None

    def __new__(cls):

        if cls._instance is None:

            cls._instance = super().__new__(cls)

        return cls._instance

    def __init__(self):
        if hasattr(self, "_loaded"):
            return

        self.settings = load_json(
            SETTINGS_FILE,
            deepcopy(DEFAULT_SETTINGS)
        )

        self.merge_defaults()
        self._loaded = True

    # ======================================

    def merge_defaults(self):

        updated = False

        for section, values in DEFAULT_SETTINGS.items():

            if section not in self.settings:

                self.settings[section] = deepcopy(values)

                updated = True

                continue

            for key, value in values.items():

                if key not in self.settings[section]:

                    self.settings[section][key] = value

                    updated = True

        if updated:

            self.save()

    # ======================================

    def save(self):

        save_json(
            SETTINGS_FILE,
            self.settings
        )

    # ======================================

    def get(self, section, key, default=None):

        return self.settings.get(
            section,
            {}
        ).get(
            key,
            default
        )

    # ======================================

    def set(
        self,
        section,
        key,
        value
    ):

        if section not in self.settings:
            self.settings[section] = {}

        self.settings[section][key] = value

        self.save()

    def section(
        self,
        section
    ):

        return self.settings.get(
            section,
            {}
        )

    def all(self):

        return self.settings

    def reload(self):

        self.settings = load_json(
            SETTINGS_FILE,
            deepcopy(DEFAULT_SETTINGS)
        )

        self.merge_defaults()

    def reset(self):

        self.settings = deepcopy(
            DEFAULT_SETTINGS
        )

        self.save()

    def update_section(self, section, values):

        if section not in self.settings:
            self.settings[section] = {}

        self.settings[section].update(values)

        self.save()