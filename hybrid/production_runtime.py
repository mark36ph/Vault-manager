"""Production runtime used by the hybrid .NET worker."""
from __future__ import annotations

from pathlib import Path
from typing import Any, Callable, Mapping

from common.asset_visual_verification import OpenAIImageRelevanceVerifier
from common.fcpxml_paths import rebase_fcpxml_media_paths
from common.mixed_asset_acquisition import install_mixed_visual_acquisition, prepare_selected_videos
from common.named_asset_hierarchy import install_named_asset_hierarchy
from common.named_subject_verification import NamedSubjectVerifier
from common.production_ui import ProductionUIController, ProductionViewState
from common.provider_setup import ProviderCredentials, ProviderSettingsStore, build_configured_providers
from common.settings_manager import SettingsManager
from common.verified_asset_acquisition import install_visual_verification
from project_manager import ProjectManager

Emit = Callable[[Mapping[str, Any]], None]


class HybridProductionRuntime:
    def __init__(self, emit: Emit) -> None:
        self.emit = emit
        self.controller: ProductionUIController | None = None
        self.project_id: int | None = None
        self.project_status = ""
        self.project_folder: Path | None = None

    @property
    def running(self) -> bool:
        return bool(self.controller is not None and self.controller.state.running)

    @staticmethod
    def _project_payload(project: Mapping[str, Any], folder: Path) -> dict[str, Any]:
        return {
            "id": int(project["id"]),
            "title": str(project.get("title") or ""),
            "status": str(project.get("status") or ""),
            "category": str(project.get("category") or ""),
            "folder": str(folder),
            "folder_exists": folder.is_dir(),
            "checkpoint_exists": (folder / "production_checkpoint.json").is_file(),
            "timeline_exists": (folder / "timeline.json").is_file(),
        }

    def list_projects(self) -> list[dict[str, Any]]:
        manager = ProjectManager()
        try:
            items = []
            for row in manager.get_all_projects():
                project = dict(row)
                if str(project.get("status") or "") not in {"In Progress", "Completed"}:
                    continue
                folder = Path(manager.resolve_project_folder(project))
                items.append(self._project_payload(project, folder))
            return items
        finally:
            manager.close()

    @staticmethod
    def _find_project(project_id: int) -> tuple[dict[str, Any], Path]:
        manager = ProjectManager()
        try:
            row = manager.db.get_project(project_id)
            if row is None:
                raise ValueError(f"project {project_id} was not found")
            project = dict(row)
            return project, Path(manager.resolve_project_folder(project))
        finally:
            manager.close()

    @staticmethod
    def _production_settings() -> dict[str, Any]:
        settings = SettingsManager()
        return {
            "timeline_width": int(settings.get("resolve", "timeline_width", 1080)),
            "timeline_height": int(settings.get("resolve", "timeline_height", 1920)),
            "frame_rate": float(settings.get("resolve", "frame_rate", 30)),
        }

    @staticmethod
    def _configured_registry(folder: Path):
        provider_settings = ProviderSettingsStore(folder).load()
        configured = build_configured_providers(folder, provider_settings)

        if str(provider_settings.asset_kind) == "image":
            credentials = ProviderCredentials()
            base_verifier = OpenAIImageRelevanceVerifier(
                credentials.get("openai"),
                model=str(provider_settings.openai_model or "gpt-5-mini"),
            )
            verifier = NamedSubjectVerifier(base_verifier)
            install_visual_verification(configured.asset_engine, verifier)
            install_mixed_visual_acquisition(configured.asset_engine, verifier)
            install_named_asset_hierarchy()

            original_image_stage = configured.registry.require("image_prompts")

            def mixed_image_stage(context):
                assets = original_image_stage(context)
                return prepare_selected_videos(context, assets)

            configured.registry.register("image_prompts", mixed_image_stage)

        return configured.registry

    @staticmethod
    def _state_payload(state: ProductionViewState) -> dict[str, Any]:
        return {
            "type": "production_state",
            "running": state.running,
            "progress": state.progress,
            "current_stage": state.current_stage,
            "message": state.message,
            "error": state.error,
            "can_start": state.can_start,
            "can_resume": state.can_resume,
            "can_cancel": state.can_cancel,
            "completed": list(state.completed),
            "stages": [
                {
                    "name": stage.name,
                    "label": stage.label,
                    "status": stage.status,
                    "message": stage.message,
                }
                for stage in state.stages
            ],
        }

    def _mark_completed_if_needed(self, state: ProductionViewState) -> None:
        if state.running or state.error or state.result is None:
            return
        if self.project_id is None or self.project_status != "In Progress":
            return
        if not state.stages or not all(stage.status == "complete" for stage in state.stages):
            return

        manager = ProjectManager()
        try:
            current = manager.db.get_project(self.project_id)
            if current is None or str(current["status"] or "") != "In Progress":
                return
            old_folder = Path(manager.resolve_project_folder(current))
            updated = dict(manager.change_project_status(self.project_id, "Completed"))
            new_folder = Path(manager.resolve_project_folder(updated))
            rebase_fcpxml_media_paths(new_folder, old_folder, new_folder)
            self.project_status = "Completed"
            self.project_folder = new_folder
            self.emit({"type": "project_updated", "project": self._project_payload(updated, new_folder)})
        finally:
            manager.close()

    def _on_state(self, state: ProductionViewState) -> None:
        self.emit(self._state_payload(state))
        try:
            self._mark_completed_if_needed(state)
        except Exception as error:
            self.emit({"type": "warning", "message": f"production completed but status update failed: {error}"})

    def start(self, payload: Mapping[str, Any]) -> None:
        if self.running:
            raise ValueError("production is already running")

        try:
            project_id = int(payload.get("project_id"))
        except (TypeError, ValueError) as error:
            raise ValueError("project_id must be an integer") from error

        mode = str(payload.get("mode") or "produce").strip().casefold()
        if mode not in {"produce", "reproduce", "resume"}:
            raise ValueError("mode must be produce, reproduce, or resume")

        project, folder = self._find_project(project_id)
        if not folder.is_dir():
            raise ValueError(f"project folder was not found: {folder}")

        status = str(project.get("status") or "")
        if mode == "reproduce" and status != "Completed":
            raise ValueError("reproduce is only available for Completed projects")
        if mode == "produce" and status != "In Progress":
            raise ValueError("produce is only available for In Progress projects")
        if mode == "resume" and not (folder / "production_checkpoint.json").is_file():
            raise ValueError("this project has no production checkpoint to resume")

        registry = self._configured_registry(folder)
        if self.controller is not None:
            self.controller.close()
        self.controller = ProductionUIController(registry, state_callback=self._on_state)
        self.project_id = project_id
        self.project_status = status
        self.project_folder = folder

        topic = str(payload.get("topic") or project.get("topic") or project.get("title") or "").strip()
        if not topic:
            raise ValueError("project topic is empty")

        options = {"topic": topic, "launch_resolve": False}
        if mode != "resume":
            options["start_at"] = "image_prompts"

        self.emit(
            {
                "type": "production_started",
                "project_id": project_id,
                "title": str(project.get("title") or ""),
                "mode": mode,
                "folder": str(folder),
            }
        )
        settings = self._production_settings()
        if mode == "resume":
            self.controller.resume(project, folder, settings, **options)
        else:
            self.controller.start(project, folder, settings, **options)

    def cancel(self) -> bool:
        return bool(self.controller is not None and self.controller.cancel())
