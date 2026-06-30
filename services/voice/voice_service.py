from services.voice.models import Voice
from services.voice.settings_manager import SettingsManager
from services.voice.utils import (
    ensure_voice_folders,
    load_voice_catalog,
    voice_installed,
    get_model_path,
    get_config_path
)


class VoiceService:

    def __init__(self):

        ensure_voice_folders()

        self.settings = SettingsManager()

    # =====================================================
    # Catalog
    # =====================================================

    def get_available_voices(self):

        voices = []

        for data in load_voice_catalog():

            voice = Voice.from_dict(data)

            voice.installed = voice_installed(voice.id)

            voice.model_path = get_model_path(voice.id)

            voice.config_path = get_config_path(voice.id)

            voices.append(voice)

        return sorted(
            voices,
            key=lambda v: v.display_name.lower()
        )

    # =====================================================
    # Installed
    # =====================================================

    def get_installed_voices(self):

        return [
            voice
            for voice in self.get_available_voices()
            if voice.installed
        ]

    # =====================================================
    # Not Installed
    # =====================================================

    def get_downloadable_voices(self):

        return [
            voice
            for voice in self.get_available_voices()
            if not voice.installed
        ]

    # =====================================================
    # Find Voice
    # =====================================================

    def get_voice(self, voice_id):

        for voice in self.get_available_voices():

            if voice.id == voice_id:

                return voice

        return None

    # =====================================================
    # Default Voice
    # =====================================================

    def get_default_voice(self):

        voice_id = self.settings.get_default_voice()

        if not voice_id:
            return None

        return self.get_voice(voice_id)

    # =====================================================
    # Engine
    # =====================================================

    def get_engine(self):

        return self.settings.get_engine()

    def set_engine(self, engine):

        self.settings.set_engine(engine)

    # =====================================================
    # Speech
    # =====================================================

    def get_rate(self):

        return self.settings.get_rate()

    def set_rate(self, value):

        self.settings.set_rate(value)

    def get_pitch(self):

        return self.settings.get_pitch()

    def set_pitch(self, value):

        self.settings.set_pitch(value)

    def get_volume(self):

        return self.settings.get_volume()

    def set_volume(self, value):

        self.settings.set_volume(value)

    # =====================================================
    # Output
    # =====================================================

    def get_output_format(self):

        return self.settings.get_output_format()

    def set_output_format(self, fmt):

        self.settings.set_output_format(fmt)

    # =====================================================
    # Status
    # =====================================================

    def has_voice(self, voice_id):

        return voice_installed(voice_id)

    # =====================================================
    # Placeholders (Phase 2)
    # =====================================================

    def download_voice(self, voice_id):

        raise NotImplementedError(
            "Download manager not implemented yet."
        )

    def delete_voice(self, voice_id):

        raise NotImplementedError(
            "Delete manager not implemented yet."
        )

    def preview_voice(self, voice_id, text=None):

        raise NotImplementedError(
            "Preview not implemented yet."
        )

    def generate_voice(
        self,
        voice_id,
        text,
        output_file
    ):

        raise NotImplementedError(
            "Piper engine not implemented yet."
        )