"""Persist provider choices and build a configured production provider registry."""
from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from common.asset_acquisition import AssetAcquisitionEngine, make_asset_acquisition_provider
from common.content_production import ProviderRegistry
from common.provider_integrations import (
    OpenAISpeechProvider,
    OpenAITextProvider,
    PexelsAssetProvider,
    PixabayAssetProvider,
)
from common.settings_manager import SettingsManager


class ProviderSetupError(RuntimeError):
    """Raised when selected provider settings cannot be used."""


@dataclass(frozen=True)
class ProviderSettings:
    text_provider: str = "openai"
    asset_providers: tuple[str, ...] = ("pexels", "pixabay")
    voice_provider: str = "openai"
    openai_model: str = "gpt-5-mini"
    openai_voice_model: str = "gpt-4o-mini-tts"
    openai_voice: str = "alloy"
    asset_kind: str = "image"
    asset_limit: int = 20
    asset_attempts: int = 3

    def validate(self) -> None:
        if self.text_provider not in {"openai"}:
            raise ValueError(f"unsupported text provider: {self.text_provider}")
        if self.voice_provider not in {"openai", "none"}:
            raise ValueError(f"unsupported voice provider: {self.voice_provider}")
        unknown_assets = set(self.asset_providers) - {"pexels", "pixabay"}
        if unknown_assets:
            raise ValueError(f"unsupported asset providers: {', '.join(sorted(unknown_assets))}")
        if self.asset_kind not in {"image", "video"}:
            raise ValueError("asset_kind must be image or video")
        if self.asset_limit < 1:
            raise ValueError("asset_limit must be at least 1")
        if self.asset_attempts < 1:
            raise ValueError("asset_attempts must be at least 1")

    @classmethod
    def from_dict(cls, payload: Mapping[str, Any]) -> "ProviderSettings":
        values = dict(payload)
        if "asset_providers" in values:
            values["asset_providers"] = tuple(values["asset_providers"])
        settings = cls(**values)
        settings.validate()
        return settings


class ProviderSettingsStore:
    """Store non-secret provider preferences in the project folder."""

    FILENAME = "provider_settings.json"

    def __init__(self, project_folder: str | Path) -> None:
        self.path = Path(project_folder) / self.FILENAME

    def load(self) -> ProviderSettings:
        if not self.path.is_file():
            return ProviderSettings()
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise ProviderSetupError(f"could not read provider settings: {self.path}") from error
        if not isinstance(payload, Mapping):
            raise ProviderSetupError("provider settings must contain a JSON object")
        try:
            return ProviderSettings.from_dict(payload)
        except (TypeError, ValueError) as error:
            raise ProviderSetupError(f"invalid provider settings: {error}") from error

    def save(self, settings: ProviderSettings) -> Path:
        if not isinstance(settings, ProviderSettings):
            raise TypeError("settings must be ProviderSettings")
        settings.validate()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = asdict(settings)
        payload["asset_providers"] = list(settings.asset_providers)
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        temporary.replace(self.path)
        return self.path


@dataclass(frozen=True)
class CredentialStatus:
    name: str
    configured: bool
    source: str


def credentials_from_app_settings(
    app_settings: Any | None = None,
    *,
    environment: Mapping[str, str] | None = None,
) -> dict[str, str]:
    """Combine saved app credentials with environment-variable fallbacks.

    Values stored in the app take priority. Environment variables remain useful
    for portable/headless runs and for users who deliberately avoid saving keys.
    """
    settings = app_settings or SettingsManager()
    get = getattr(settings, "get", lambda section, key, default=None: default)
    env = os.environ if environment is None else environment
    stored = {
        "OPENAI_API_KEY": str(get("ai", "api_key", "") or "").strip(),
        "PEXELS_API_KEY": str(get("images", "pexels_api_key", "") or "").strip(),
        "PIXABAY_API_KEY": str(get("images", "pixabay_api_key", "") or "").strip(),
    }
    return {
        name: value or str(env.get(name, "") or "").strip()
        for name, value in stored.items()
    }


class ProviderCredentials:
    """Read API keys from app settings, an injected mapping, or the environment."""

    NAMES = {
        "openai": "OPENAI_API_KEY",
        "pexels": "PEXELS_API_KEY",
        "pixabay": "PIXABAY_API_KEY",
    }

    def __init__(self, values: Mapping[str, str] | None = None) -> None:
        self.values = credentials_from_app_settings() if values is None else values

    def get(self, provider: str, *, required: bool = True) -> str:
        variable = self.NAMES.get(provider)
        if variable is None:
            raise ValueError(f"unknown provider: {provider}")
        value = str(self.values.get(variable, "")).strip()
        if required and not value:
            raise ProviderSetupError(f"{variable} is not configured")
        return value

    def status(self) -> tuple[CredentialStatus, ...]:
        return tuple(
            CredentialStatus(name=name, configured=bool(self.get(name, required=False)), source=variable)
            for name, variable in self.NAMES.items()
        )


@dataclass(frozen=True)
class ConfiguredProviders:
    registry: ProviderRegistry
    asset_engine: AssetAcquisitionEngine
    settings: ProviderSettings


def _research_prompt(context: Any) -> str:
    return f"Research this topic for a factual short-form video: {context.topic}"


def _facts_prompt(context: Any) -> str:
    return f"Select the strongest verifiable facts from this research:\n{context.research}"


def _script_prompt(context: Any) -> str:
    return f"Write a concise narrated video script from these facts:\n{context.facts}"


def _image_prompt(context: Any) -> str:
    topic = str(getattr(context, "topic", "") or "").strip()
    script = str(getattr(context, "script", "") or "").strip()

    return (
        "Create one stock-photo search query for each visual scene "
        "in the script below.\n\n"

        f"Overall topic: {topic}\n\n"

        "Rules:\n"
        "- Each query must directly depict the specific idea being narrated.\n"
        "- Keep the main subject from the overall topic in every query when relevant.\n"
        "- Prefer literal, documentary, realistic photography.\n"
        "- Do not use abstract metaphors unless the script specifically requires one.\n"
        "- Do not substitute unrelated objects just because they share a keyword.\n"
        "- Include important nouns, locations, materials, weather, objects, "
        "or actions from that exact scene.\n"
        "- Queries should work well on Pexels or Pixabay.\n"
        "- Prefer portrait-friendly compositions when possible.\n"
        "- Do not include numbering, explanations, quotation marks, or headings.\n"
        "- Return exactly one search query per line.\n\n"

        "Examples of specificity:\n"
        "Bad: cold weather\n"
        "Good: Eiffel Tower Paris winter snow cold weather\n\n"
        "Bad: metal expands\n"
        "Good: heated iron metal expansion close up engineering\n\n"

        f"Script:\n{script}"
    )


def _imported_scene_searches(project: Mapping[str, Any] | None) -> list[str]:
    """Extract the first explicit Search query from each imported timeline scene."""
    if not isinstance(project, Mapping):
        return []

    notes = str(project.get("notes") or "").replace("\r\n", "\n")
    if not notes.strip():
        return []

    searches: list[str] = []
    seen: set[str] = set()
    lines = notes.splitlines()
    for index, line in enumerate(lines):
        if line.strip().casefold() != "search:":
            continue

        for candidate_line in lines[index + 1:]:
            candidate = candidate_line.strip(" -\t")
            lower = candidate.casefold()
            if not candidate:
                continue
            if lower in {"free sources:", "search:"} or lower.endswith(" sec"):
                break
            key = candidate.casefold()
            if key not in seen:
                searches.append(candidate)
                seen.add(key)
            break

    return searches


def _anchor_searches(prompts: list[str], context: Any) -> list[str]:
    """Keep searches tied to the project's subject/category without bloating them."""
    project = getattr(context, "project", None)
    category = ""
    if isinstance(project, Mapping):
        category = str(project.get("category") or "").strip()

    topic = str(getattr(context, "topic", "") or "").strip()
    anchor = category or topic
    anchored: list[str] = []

    for prompt in prompts:
        prompt = str(prompt or "").strip()
        if not prompt:
            continue
        if anchor and anchor.casefold() not in prompt.casefold():
            anchored.append(f"{anchor} {prompt}".strip())
        else:
            anchored.append(prompt)

    return anchored


def build_configured_providers(
    project_folder: str | Path,
    settings: ProviderSettings,
    *,
    credentials: ProviderCredentials | None = None,
    text_transport: Callable | None = None,
    speech_transport: Callable | None = None,
    pexels_transport: Callable | None = None,
    pixabay_transport: Callable | None = None,
    downloader: Callable | None = None,
) -> ConfiguredProviders:
    """Create real provider adapters and a ContentProductionEngine registry."""
    settings.validate()
    keys = credentials or ProviderCredentials()

    text_options: dict[str, Any] = {}
    speech_options: dict[str, Any] = {}
    if text_transport is not None:
        text_options["transport"] = text_transport
    if speech_transport is not None:
        speech_options["transport"] = speech_transport

    openai_key = keys.get("openai")
    providers: dict[str, Any] = {
        "research": OpenAITextProvider(
            openai_key,
            instructions="Research accurately and clearly.",
            prompt_builder=_research_prompt,
            model=settings.openai_model,
            **text_options,
        ),
        "facts": OpenAITextProvider(
            openai_key,
            instructions="Extract only strong factual claims.",
            prompt_builder=_facts_prompt,
            model=settings.openai_model,
            **text_options,
        ),
        "script": OpenAITextProvider(
            openai_key,
            instructions="Write engaging factual narration.",
            prompt_builder=_script_prompt,
            model=settings.openai_model,
            **text_options,
        ),
        "image_prompts": OpenAITextProvider(
            openai_key,
            instructions=(
                "Generate highly specific literal stock-photo search queries. "
                "Every query must visually match its exact narration scene and remain "
                "anchored to the video's main subject. Avoid generic, abstract, symbolic, "
                "or loosely related imagery. Prefer realistic documentary photography. "
                "Return only one search query per line. "
                "For abstract concepts such as heat, cold, expansion, measurement, or engineering, "
                "combine the concept with the video's main physical subject rather than searching "
                "for the concept by itself."
            ),
            prompt_builder=_image_prompt,
            model=settings.openai_model,
            **text_options,
        ),
    }
    if settings.voice_provider == "openai":
        providers["voice"] = OpenAISpeechProvider(openai_key, model=settings.openai_voice_model, voice=settings.openai_voice, **speech_options)

    asset_providers = []
    for name in settings.asset_providers:
        if name == "pexels":
            options = {} if pexels_transport is None else {"transport": pexels_transport}
            asset_providers.append(PexelsAssetProvider(keys.get("pexels"), **options))
        elif name == "pixabay":
            options = {} if pixabay_transport is None else {"transport": pixabay_transport}
            asset_providers.append(PixabayAssetProvider(keys.get("pixabay"), **options))
    if not asset_providers:
        raise ProviderSetupError("at least one asset provider must be selected")
    engine_options = {} if downloader is None else {"downloader": downloader}
    asset_engine = AssetAcquisitionEngine(asset_providers, **engine_options)
    destination = Path(project_folder) / "Assets" / "Acquired"
    providers["image_prompts"] = _acquisition_stage(
        providers["image_prompts"], asset_engine, destination, settings
    )
    return ConfiguredProviders(ProviderRegistry(providers), asset_engine, settings)


def _acquisition_stage(prompt_provider, engine, destination: Path, settings: ProviderSettings):
    acquire = make_asset_acquisition_provider(engine, destination, kind=settings.asset_kind)

    def run(context):
        imported = _imported_scene_searches(getattr(context, "project", None))
        if imported:
            prompts = imported
            if hasattr(context, "warnings"):
                context.warnings.append(
                    f"Using {len(imported)} imported scene search queries for asset selection"
                )
        else:
            raw = prompt_provider(context)
            prompts = [
                line.strip(" -\t")
                for line in str(raw).splitlines()
                if line.strip()
            ]

        context.image_prompts = _anchor_searches(prompts, context)
        return acquire(context)

    return run


def test_provider_credentials(
    settings: ProviderSettings,
    *,
    credentials: ProviderCredentials | None = None,
) -> tuple[CredentialStatus, ...]:
    """Return credential availability for settings-page status indicators."""
    settings.validate()
    keys = credentials or ProviderCredentials()
    required = {"openai", *settings.asset_providers}
    return tuple(status for status in keys.status() if status.name in required)


__all__ = [
    "ConfiguredProviders",
    "CredentialStatus",
    "ProviderCredentials",
    "ProviderSettings",
    "ProviderSettingsStore",
    "ProviderSetupError",
    "build_configured_providers",
    "credentials_from_app_settings",
    "test_provider_credentials",
]
