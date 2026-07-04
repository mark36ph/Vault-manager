from services.voice.models import Voice
from services.voice.audio_player import AudioPlayer
from common.settings_manager import SettingsManager
from services.voice.download_manager import DownloadManager
from services.voice.piper_engine import PiperEngine
from pathlib import Path
from services.voice.utils import (
    ensure_voice_folders,
    load_voice_catalog,
    voice_installed,
    get_model_path,
    get_config_path,
    get_model_url,
    get_config_url
)


class VoiceService:

    def __init__(self):

        ensure_voice_folders()
        self.settings = SettingsManager()
        self.download_manager = DownloadManager()
        self.player = AudioPlayer()
        self.piper = PiperEngine()

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
            voice.model_url = get_model_url(voice)
            voice.config_url = get_config_url(voice)
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

        voice_id = self.settings.get(
            "voice",
            "default_voice"
        )

        if not voice_id:
            return None

        return self.get_voice(voice_id)

    # =====================================================
    # Set Default Voice
    # =====================================================

    def set_default_voice(self, voice_id):

        voice = self.get_voice(voice_id)

        if voice is None:
            raise Exception("Voice not found.")

        if not voice.installed:
            raise Exception("Voice is not installed.")

        self.settings.set(
            "voice",
            "default_voice",
            voice.id
        )

        return voice

    # =====================================================
    # Engine
    # =====================================================

    def get_engine(self):

        return self.settings.get(
            "voice",
            "engine"
        )

    def set_engine(self, engine):

        self.settings.set(
            "voice",
            "engine",
            engine
        )

    # =====================================================
    # Speech
    # =====================================================

    def get_rate(self):

        return self.settings.get(
            "voice",
            "speech_rate"
        )

    def set_rate(self, value):

        self.settings.set(
            "voice",
            "speech_rate",
            value
        )

    def get_volume(self):

        return self.settings.get(
            "voice",
            "volume"
        )

    def set_volume(self, value):

        self.settings.set(
            "voice",
            "volume",
            value
        )

    # =====================================================
    # Output
    # =====================================================

    def get_output_format(self):

        return self.settings.get(
            "voice",
            "output_format"
        )

    def set_output_format(self, fmt):

        self.settings.set(
            "voice",
            "output_format",
            fmt
        )

    # =====================================================
    # Status
    # =====================================================

    def has_voice(self, voice_id):

        return voice_installed(voice_id)

    # =====================================================
    # Placeholders (Phase 2)
    # =====================================================

    def download_voice(
        self,
        voice_id,
        progress_callback=None
    ):

        voice = self.get_voice(voice_id)

        if voice is None:
            raise Exception("Voice not found.")

        self.download_manager.download_voice(
            voice,
            progress_callback
        )

    def delete_voice(self, voice_id):

        voice = self.get_voice(voice_id)

        if voice is None:
            raise Exception("Voice not found.")

        self.download_manager.delete_voice(
            voice
        )

        # If it was the default voice, clear it
        default = self.settings.get(
            "voice",
            "default_voice"
        )

        if default == voice.id:

            self.settings.set(
                "voice",
                "default_voice",
                ""
            )

    def preview_voice(self, voice_id):

        voice = self.get_voice(voice_id)

        if voice is None:
            raise Exception("Voice not found.")

        if not voice.installed:
            raise Exception("Voice not installed.")

        preview_file = (
            Path("voices")
            / "temp"
            / "preview.wav"
        )

        self.generate_voice(
            voice.id,
            voice.sample_text,
            preview_file
        )

        self.player.play(
            preview_file
        )

    def generate_voice(
        self,
        voice_id,
        text,
        output_file,
        length_scale=1.0
    ):

        voice = self.get_voice(voice_id)

        if voice is None:
            raise Exception("Voice not found.")

        if not voice.installed:
            raise Exception("Voice is not installed.")

        self.piper.generate(
            voice=voice,
            text=text,
            output_file=output_file,
            length_scale=length_scale
        )
        return output_file

    def stop_preview(self):

        self.player.stop()

    def speak(
        self,
        voice_id,
        text,
        speed=1.0,
    ):

        temp_file = (
            Path("voices")
            / "temp"
            / "speak.wav"
        )

        self.generate_voice(
            voice_id,
            text,
            temp_file,
            length_scale=1 / float(speed),
        )

        self.player.play(
            temp_file
        )