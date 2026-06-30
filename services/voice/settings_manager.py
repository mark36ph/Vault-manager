import json
from pathlib import Path


class SettingsManager:

    def __init__(self):

        self.settings_file = Path("data") / "voice_settings.json"

        self.defaults = {
            "engine": "Piper",
            "default_voice": "",
            "speech_rate": 1.0,
            "speech_pitch": 1.0,
            "output_format": "wav",
            "volume": 1.0
        }

        self.settings = self.load()

    # ==========================================
    # Load / Save
    # ==========================================

    def load(self):

        if not self.settings_file.exists():

            self.save(self.defaults)

            return self.defaults.copy()

        try:

            with open(
                self.settings_file,
                "r",
                encoding="utf-8-sig"
            ) as f:

                data = json.load(f)

        except Exception as e:

            raise Exception(
                f"Failed to load voice settings:\n{e}"
            )

        # Make sure new settings get added automatically
        for key, value in self.defaults.items():

            data.setdefault(key, value)

        return data

    def save(self, data=None):

        if data is not None:

            self.settings = data

        self.settings_file.parent.mkdir(
            parents=True,
            exist_ok=True
        )

        with open(
            self.settings_file,
            "w",
            encoding="utf-8-sig"
        ) as f:

            json.dump(
                self.settings,
                f,
                indent=4
            )

    # ==========================================
    # Generic Get / Set
    # ==========================================

    def get(self, key):

        return self.settings.get(key)

    def set(self, key, value):

        self.settings[key] = value

        self.save()

    # ==========================================
    # Engine
    # ==========================================

    def get_engine(self):

        return self.get("engine")

    def set_engine(self, engine):

        self.set("engine", engine)

    # ==========================================
    # Default Voice
    # ==========================================

    def get_default_voice(self):

        return self.get("default_voice")

    def set_default_voice(self, voice):

        self.set("default_voice", voice)

    # ==========================================
    # Speech Rate
    # ==========================================

    def get_rate(self):

        return float(self.get("speech_rate"))

    def set_rate(self, value):

        self.set("speech_rate", float(value))

    # ==========================================
    # Speech Pitch
    # ==========================================

    def get_pitch(self):

        return float(self.get("speech_pitch"))

    def set_pitch(self, value):

        self.set("speech_pitch", float(value))

    # ==========================================
    # Volume
    # ==========================================

    def get_volume(self):

        return float(self.get("volume"))

    def set_volume(self, value):

        self.set("volume", float(value))

    # ==========================================
    # Output Format
    # ==========================================

    def get_output_format(self):

        return self.get("output_format")

    def set_output_format(self, fmt):

        self.set("output_format", fmt)

    # ==========================================
    # Reset
    # ==========================================

    def reset(self):

        self.settings = self.defaults.copy()

        self.save()