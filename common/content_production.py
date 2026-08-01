"""Typed, resumable content-production orchestration."""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Mapping

from common.production_assembly import assemble_timeline, json_safe
from common.resolve_production import ResolveProductionResult, ResolveProductionService
from timeline import SceneBuilder, Timeline

Provider = Callable[["ProductionContext"], Any]
ProgressCallback = Callable[[str, int, int, str], None]

STAGES = (
    "research",
    "facts",
    "script",
    "image_prompts",
    "voice",
    "timeline",
    "resolve",
)


class ContentProductionError(RuntimeError):
    """Raised when a content-production stage cannot complete."""


@dataclass
class ProductionContext:
    project: dict[str, Any]
    project_folder: Path
    settings: dict[str, Any]
    topic: str = ""
    research: Any = None
    facts: Any = None
    script: str = ""
    image_prompts: Any = None
    voice: Any = None
    timeline: Timeline | None = None
    resolve: ResolveProductionResult | None = None
    completed_stages: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def value(self, stage: str) -> Any:
        return getattr(self, stage)


@dataclass(frozen=True)
class ContentProductionResult:
    context: ProductionContext
    started_at: str
    completed: tuple[str, ...]

    @property
    def succeeded(self) -> bool:
        return len(self.completed) == len(STAGES)


class ProviderRegistry:
    def __init__(self, providers: Mapping[str, Provider] | None = None) -> None:
        self._providers: dict[str, Provider] = {}
        for name, provider in dict(providers or {}).items():
            self.register(name, provider)

    def register(self, name: str, provider: Provider) -> None:
        if name not in STAGES:
            raise ValueError(f"unknown production provider: {name}")
        if not callable(provider):
            raise TypeError("provider must be callable")
        self._providers[name] = provider

    def get(self, name: str) -> Provider | None:
        return self._providers.get(name)

    def require(self, name: str) -> Provider:
        provider = self.get(name)
        if provider is None:
            raise ContentProductionError(f"provider is not configured: {name}")
        return provider


class ProductionCheckpointStore:
    """Persist resumable production state using only JSON-safe values."""

    FILENAME = "production_checkpoint.json"

    def __init__(self, project_folder: str | Path) -> None:
        self.path = Path(project_folder) / self.FILENAME

    def save(self, context: ProductionContext) -> Path:
        payload = json_safe({
            "topic": context.topic,
            "research": context.research,
            "facts": context.facts,
            "script": context.script,
            "image_prompts": context.image_prompts,
            "voice": context.voice,
            "completed_stages": list(context.completed_stages),
            "warnings": list(context.warnings),
        })
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        temporary.replace(self.path)
        return self.path

    def load_into(self, context: ProductionContext) -> ProductionContext:
        if not self.path.is_file():
            return context
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise ContentProductionError(f"could not read production checkpoint: {self.path}") from error
        for name in ("topic", "research", "facts", "script", "image_prompts", "voice"):
            if name in payload:
                setattr(context, name, payload[name])
        context.completed_stages = [name for name in payload.get("completed_stages", []) if name in STAGES]
        context.warnings = list(payload.get("warnings", []))
        return context

    def clear(self) -> None:
        self.path.unlink(missing_ok=True)


class ContentProductionEngine:
    def __init__(
        self,
        providers: ProviderRegistry | Mapping[str, Provider],
        *,
        scene_builder: SceneBuilder | None = None,
        resolve_service: ResolveProductionService | None = None,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self.providers = providers if isinstance(providers, ProviderRegistry) else ProviderRegistry(providers)
        self.scene_builder = scene_builder or SceneBuilder()
        self.resolve_service = resolve_service or ResolveProductionService()
        self.progress_callback = progress_callback

    def _progress(self, stage: str, index: int, message: str) -> None:
        if self.progress_callback is not None:
            self.progress_callback(stage, index, len(STAGES), message)

    def _run_stage(self, stage: str, context: ProductionContext, *, launch_resolve: bool) -> Any:
        provider = self.providers.get(stage)
        if provider is not None:
            return provider(context)
        if stage == "voice":
            context.warnings.append("Narration generation is disabled")
            return None
        if stage == "timeline":
            if not context.script.strip():
                raise ContentProductionError("timeline stage requires a script")
            timeline = self.scene_builder.build(
                context.script,
                name=str(context.project.get("title") or context.topic or "Fact Vault Video"),
            )
            return assemble_timeline(timeline, context.image_prompts, context.voice)
        if stage == "resolve":
            if context.timeline is None:
                raise ContentProductionError("resolve stage requires a timeline")
            return self.resolve_service.run(
                context.project,
                context.project_folder,
                context.settings,
                timeline=context.timeline,
                launch=launch_resolve,
            )
        raise ContentProductionError(f"provider is not configured: {stage}")

    def run(
        self,
        project: Mapping[str, Any],
        project_folder: str | Path,
        settings: Mapping[str, Any],
        *,
        topic: str | None = None,
        resume: bool = True,
        start_at: str | None = None,
        stop_after: str | None = None,
        launch_resolve: bool = False,
    ) -> ContentProductionResult:
        if not isinstance(project, Mapping):
            raise TypeError("project must be a mapping")
        if start_at is not None and start_at not in STAGES:
            raise ValueError(f"unknown start stage: {start_at}")
        if stop_after is not None and stop_after not in STAGES:
            raise ValueError(f"unknown stop stage: {stop_after}")
        folder = Path(project_folder)
        if not folder.is_dir():
            raise FileNotFoundError(folder)
        started_at = datetime.now(timezone.utc).isoformat()
        context = ProductionContext(
            project=dict(project),
            project_folder=folder,
            settings=dict(settings),
            topic=str(topic or project.get("topic") or project.get("title") or ""),
        )
        checkpoints = ProductionCheckpointStore(folder)
        if resume:
            checkpoints.load_into(context)

        start_index = STAGES.index(start_at) if start_at else 0
        if start_at:
            context.completed_stages = [name for name in context.completed_stages if STAGES.index(name) < start_index]

        for index, stage in enumerate(STAGES, start=1):
            if index - 1 < start_index or stage in context.completed_stages:
                continue
            self._progress(stage, index, f"Running {stage.replace('_', ' ')}")
            try:
                value = self._run_stage(stage, context, launch_resolve=launch_resolve)
            except Exception as error:
                checkpoints.save(context)
                if isinstance(error, ContentProductionError):
                    raise
                raise ContentProductionError(f"stage {stage} failed: {error}") from error
            setattr(context, stage, value)
            context.completed_stages.append(stage)
            checkpoints.save(context)
            if stage == stop_after:
                break

        if len(context.completed_stages) == len(STAGES):
            checkpoints.clear()
        return ContentProductionResult(
            context=context,
            started_at=started_at,
            completed=tuple(context.completed_stages),
        )


def build_content_production(
    project: Mapping[str, Any],
    project_folder: str | Path,
    settings: Mapping[str, Any],
    providers: ProviderRegistry | Mapping[str, Provider],
    **options: Any,
) -> ContentProductionResult:
    return ContentProductionEngine(providers).run(project, project_folder, settings, **options)


__all__ = [
    "ContentProductionEngine",
    "ContentProductionError",
    "ContentProductionResult",
    "ProductionCheckpointStore",
    "ProductionContext",
    "ProviderRegistry",
    "STAGES",
    "build_content_production",
]
