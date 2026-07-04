import json
from pathlib import Path
import urllib.request

PIPER_VERSION = "v1.0.0"

PIPER_BASE = (
    "https://huggingface.co/"
    "rhasspy/piper-voices/"
    f"resolve/{PIPER_VERSION}"
)

VOICE_FOLDER = Path("voices")
INSTALLED_FOLDER = VOICE_FOLDER / "installed"
CACHE_FOLDER = VOICE_FOLDER / "cache"
OUTPUT_FOLDER = VOICE_FOLDER / "output"
TEMP_FOLDER = VOICE_FOLDER / "temp"
SAMPLES_FOLDER = VOICE_FOLDER / "samples"

VOICE_CATALOG = Path("data") / "voice_catalog.json"

ONLINE_VOICE_CATALOG_URL = (
    "https://huggingface.co/rhasspy/piper-voices/raw/main/voices.json"
)

ONLINE_CACHE_FILE = CACHE_FOLDER / "online_voice_catalog.json"

# =====================================================
# Folder Helpers
# =====================================================

def ensure_voice_folders():

    for folder in (
        VOICE_FOLDER,
        INSTALLED_FOLDER,
        CACHE_FOLDER,
        OUTPUT_FOLDER,
        TEMP_FOLDER,
        SAMPLES_FOLDER,
    ):
        folder.mkdir(parents=True, exist_ok=True)


# =====================================================
# Voice Catalog
# =====================================================

def load_voice_catalog():

    ensure_voice_folders()

    # Load cached online catalog first for fast startup
    if ONLINE_CACHE_FILE.exists():

        try:

            with open(
                ONLINE_CACHE_FILE,
                "r",
                encoding="utf-8-sig"
            ) as f:

                online_data = json.load(f)

            return convert_online_catalog(
                online_data
            )

        except Exception as e:

            print(f"Cached voice catalog unavailable: {e}")

    # Fall back to local app catalog
    if VOICE_CATALOG.exists():

        with open(
            VOICE_CATALOG,
            "r",
            encoding="utf-8-sig"
        ) as f:

            data = json.load(f)

        return data.get("voices", [])

    return []

def convert_online_catalog(data):

    voices = []

    allowed_prefixes = [
        "en_US",
        "en_GB",
        "en_AU",
        "en_CA",
        "en_IE"
    ]

    country_names = {
        "GB": "British",
        "US": "American",
        "AU": "Australian",
        "CA": "Canadian",
        "IE": "Irish"
    }

    for voice_id, info in data.items():

        if not any(
            voice_id.startswith(prefix)
            for prefix in allowed_prefixes
        ):
            continue

        parts = voice_id.split("-")

        if len(parts) < 3:
            continue

        language_parts = parts[0].split("_")

        language = language_parts[0]
        region = language_parts[1] if len(language_parts) > 1 else ""
        voice_name = parts[1]
        quality = parts[2]

        country = country_names.get(
            region.upper(),
            region.upper()
        )

        voices.append({

            "id": voice_id,
            "display_name": (
                f"{country} • "
                f"{voice_name.title()} • "
                f"{quality.title()}"
            ),
            "language": language,
            "region": region,
            "voice": voice_name,
            "quality": quality,
            "sample_text": "Hello, this is a preview of this voice."

        })

    return voices
    
# =====================================================
# Installed Voice Helpers
# =====================================================

def get_model_path(voice_id):

    return INSTALLED_FOLDER / f"{voice_id}.onnx"


def get_config_path(voice_id):

    return INSTALLED_FOLDER / f"{voice_id}.onnx.json"


def voice_installed(voice_id):

    return (
        get_model_path(voice_id).exists()
        and
        get_config_path(voice_id).exists()
    )


def find_installed_voice_models():

    voices = []

    for model in INSTALLED_FOLDER.glob("*.onnx"):

        config = model.with_suffix(".onnx.json")

        voices.append({

            "id": model.stem,

            "model": model,

            "config": config,

            "ready": config.exists()

        })

    return sorted(
        voices,
        key=lambda v: v["id"].lower()
    )


# =====================================================
# Output Helpers
# =====================================================

def get_output_file(project_name):

    return OUTPUT_FOLDER / f"{project_name}.wav"


def get_sample_output():

    return SAMPLES_FOLDER / "preview.wav"
    

def get_model_url(voice):

    locale = f"{voice.language}_{voice.region}"

    return (
        f"{PIPER_BASE}/"
        f"{voice.language}/"
        f"{locale}/"
        f"{voice.voice}/"
        f"{voice.quality}/"
        f"{voice.id}.onnx?download=true"
    )


def get_config_url(voice):

    locale = f"{voice.language}_{voice.region}"

    return (
        f"{PIPER_BASE}/"
        f"{voice.language}/"
        f"{locale}/"
        f"{voice.voice}/"
        f"{voice.quality}/"
        f"{voice.id}.onnx.json?download=true"
    )