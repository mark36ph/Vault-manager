import json
from pathlib import Path

VOICE_FOLDER = Path("voices")
INSTALLED_FOLDER = VOICE_FOLDER / "installed"
CACHE_FOLDER = VOICE_FOLDER / "cache"
OUTPUT_FOLDER = VOICE_FOLDER / "output"
TEMP_FOLDER = VOICE_FOLDER / "temp"
SAMPLES_FOLDER = VOICE_FOLDER / "samples"

VOICE_CATALOG = Path("data") / "voice_catalog.json"


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

    if not VOICE_CATALOG.exists():
        return []

    with open(
        VOICE_CATALOG,
        "r",
        encoding="utf-8-sig"
    ) as f:

        data = json.load(f)

    return data.get("voices", [])


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