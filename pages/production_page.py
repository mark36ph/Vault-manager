from __future__ import annotations

import os
from pathlib import Path
from tkinter import messagebox
from typing import Any, Mapping

import customtkinter as ctk

from common.content_production import STAGES
from common.production_ui import ProductionUIController, ProductionViewState
from common.provider_setup import (
    ProviderCredentials,
    ProviderSettings,
    ProviderSettingsStore,
    ProviderSetupError,
    build_configured_providers,
    test_provider_credentials,
)
from pages.base_page import BasePage


STAGE_LABELS = {
    "research": "Research",
    "facts": "Select Facts",
    "script": "Write Script",
    "image_prompts": "Find Visuals",
    "voice": "Generate Voice",
    "timeline": "Build Timeline",
    "resolve": "Build Resolve Package",
}


def project_choice(project: Mapping[str, Any]) -> str:
    return f"{project.get('title', 'Untitled')}  •  {project.get('status', 'Unknown')}"


def selected_asset_providers(use_pexels: bool, use_pixabay: bool) -> tuple[str, ...]:
    providers = []
    if use_pexels:
        providers.append("pexels")
    if use_pixabay:
        providers.append("pixabay")
    return tuple(providers)


def production_settings_from_app(app_settings: Any) -> dict[str, Any]:
    get = getattr(app_settings, "get", lambda section, key, default=None: default)
    return {
        "timeline_width": int(get("resolve", "timeline_width", 1080)),
        "timeline_height": int(get("resolve", "timeline_height", 1920)),
        "frame_rate": float(get("resolve", "frame_rate", 30)),
    }


class ProductionPage(BasePage):
    """Visible desktop page for configuring and running end-to-end production."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Production")
        self.app = app
        self.projects = list(self.pm.get_all_projects())
        self.project_lookup = {project_choice(project): project for project in self.projects}
        self.controller: ProductionUIController | None = None
        self.stage_rows: dict[str, tuple[ctk.CTkLabel, ctk.CTkLabel]] = {}
        self.build()

    def build(self):
        intro = ctk.CTkLabel(
            self.content,
            text="Create a complete factual video from one topic.",
            font=("Segoe UI", 18),
            text_color="gray70",
        )
        intro.pack(anchor="w", pady=(0, 16))

        body = ctk.CTkFrame(self.content, fg_color="transparent")
        body.pack(fill="both", expand=True)
        body.grid_columnconfigure(0, weight=1)
        body.grid_columnconfigure(1, weight=1)
        body.grid_rowconfigure(0, weight=1)

        self._build_controls(body)
        self._build_progress(body)
        self._load_selected_project()

    def _build_controls(self, parent):
        panel = ctk.CTkScrollableFrame(parent)
        panel.grid(row=0, column=0, sticky="nsew", padx=(0, 8))

        ctk.CTkLabel(panel, text="Production Setup", font=("Segoe UI", 22, "bold")).pack(
            anchor="w", padx=20, pady=(20, 12)
        )

        choices = list(self.project_lookup) or ["No projects available"]
        self.project_menu = ctk.CTkOptionMenu(panel, values=choices, command=lambda _: self._load_selected_project())
        self.project_menu.pack(fill="x", padx=20, pady=6)

        self.topic_entry = ctk.CTkEntry(panel, placeholder_text="Video topic")
        self.topic_entry.pack(fill="x", padx=20, pady=6)

        provider_box = ctk.CTkFrame(panel)
        provider_box.pack(fill="x", padx=20, pady=(12, 6))
        ctk.CTkLabel(provider_box, text="Media providers", font=("Segoe UI", 16, "bold")).pack(
            anchor="w", padx=14, pady=(12, 4)
        )
        self.use_pexels = ctk.BooleanVar(value=True)
        self.use_pixabay = ctk.BooleanVar(value=True)
        ctk.CTkCheckBox(provider_box, text="Pexels", variable=self.use_pexels).pack(anchor="w", padx=14, pady=4)
        ctk.CTkCheckBox(provider_box, text="Pixabay", variable=self.use_pixabay).pack(anchor="w", padx=14, pady=(4, 12))

        self.asset_kind = ctk.CTkSegmentedButton(panel, values=["image", "video"])
        self.asset_kind.set("image")
        self.asset_kind.pack(fill="x", padx=20, pady=6)

        self.voice_enabled = ctk.BooleanVar(value=True)
        ctk.CTkCheckBox(panel, text="Generate OpenAI narration", variable=self.voice_enabled).pack(
            anchor="w", padx=20, pady=8
        )
        self.launch_resolve = ctk.BooleanVar(value=False)
        ctk.CTkCheckBox(panel, text="Launch Resolve when complete", variable=self.launch_resolve).pack(
            anchor="w", padx=20, pady=8
        )

        self.credential_label = ctk.CTkLabel(panel, text="Checking credentials...", justify="left")
        self.credential_label.pack(anchor="w", padx=20, pady=(10, 4))

        buttons = ctk.CTkFrame(panel, fg_color="transparent")
        buttons.pack(fill="x", padx=20, pady=(12, 20))
        self.start_button = ctk.CTkButton(buttons, text="▶ Produce Video", height=42, command=self.start_production)
        self.start_button.pack(fill="x", pady=4)
        self.resume_button = ctk.CTkButton(buttons, text="↻ Resume Production", command=self.resume_production)
        self.resume_button.pack(fill="x", pady=4)
        self.cancel_button = ctk.CTkButton(buttons, text="Cancel", fg_color="#8B2E2E", command=self.cancel_production)
        self.cancel_button.pack(fill="x", pady=4)
        self.open_button = ctk.CTkButton(buttons, text="Open Project Folder", command=self.open_project_folder)
        self.open_button.pack(fill="x", pady=4)

    def _build_progress(self, parent):
        panel = ctk.CTkFrame(parent)
        panel.grid(row=0, column=1, sticky="nsew", padx=(8, 0))

        ctk.CTkLabel(panel, text="Production Progress", font=("Segoe UI", 22, "bold")).pack(
            anchor="w", padx=20, pady=(20, 8)
        )
        self.status_label = ctk.CTkLabel(panel, text="Ready", font=("Segoe UI", 16))
        self.status_label.pack(anchor="w", padx=20, pady=(0, 8))
        self.progress = ctk.CTkProgressBar(panel)
        self.progress.set(0)
        self.progress.pack(fill="x", padx=20, pady=(0, 16))

        stages = ctk.CTkScrollableFrame(panel)
        stages.pack(fill="both", expand=True, padx=20, pady=(0, 20))
        for stage in STAGES:
            row = ctk.CTkFrame(stages)
            row.pack(fill="x", pady=5)
            icon = ctk.CTkLabel(row, text="○", width=32, font=("Segoe UI", 18, "bold"))
            icon.pack(side="left", padx=(10, 4), pady=10)
            label = ctk.CTkLabel(row, text=STAGE_LABELS[stage], font=("Segoe UI", 15, "bold"))
            label.pack(side="left", pady=10)
            detail = ctk.CTkLabel(row, text="Waiting", text_color="gray65")
            detail.pack(side="right", padx=12, pady=10)
            self.stage_rows[stage] = (icon, detail)

    def _selected_project(self) -> Mapping[str, Any] | None:
        return self.project_lookup.get(self.project_menu.get())

    def _project_folder(self) -> Path | None:
        project = self._selected_project()
        if project is None:
            return None
        folder = project.get("folder")
        if folder:
            return Path(folder)
        try:
            return Path(self.pm.get_project_folder(project))
        except Exception:
            return None

    def _load_selected_project(self):
        project = self._selected_project()
        folder = self._project_folder()
        if project is None or folder is None:
            self.credential_label.configure(text="Create a project before starting production.")
            self.start_button.configure(state="disabled")
            self.resume_button.configure(state="disabled")
            return
        self.topic_entry.delete(0, "end")
        self.topic_entry.insert(0, str(project.get("topic") or project.get("title") or ""))
        saved = ProviderSettingsStore(folder).load()
        self.use_pexels.set("pexels" in saved.asset_providers)
        self.use_pixabay.set("pixabay" in saved.asset_providers)
        self.asset_kind.set(saved.asset_kind)
        self.voice_enabled.set(saved.voice_provider != "none")
        self._refresh_credentials(saved)
        self.resume_button.configure(state="normal" if (folder / "production_checkpoint.json").is_file() else "disabled")

    def _provider_settings(self) -> ProviderSettings:
        providers = selected_asset_providers(self.use_pexels.get(), self.use_pixabay.get())
        return ProviderSettings(
            asset_providers=providers,
            asset_kind=self.asset_kind.get(),
            voice_provider="openai" if self.voice_enabled.get() else "none",
        )

    def _refresh_credentials(self, settings: ProviderSettings):
        try:
            statuses = test_provider_credentials(settings, credentials=ProviderCredentials())
            lines = [f"{'✓' if item.configured else '✗'} {item.source}" for item in statuses]
            ready = all(item.configured for item in statuses)
            self.credential_label.configure(text="Credentials\n" + "\n".join(lines))
            self.start_button.configure(state="normal" if ready else "disabled")
        except Exception as error:
            self.credential_label.configure(text=f"Provider setup error: {error}")
            self.start_button.configure(state="disabled")

    def _make_controller(self, folder: Path, provider_settings: ProviderSettings) -> ProductionUIController:
        configured = build_configured_providers(folder, provider_settings)
        return ProductionUIController(configured.registry, state_callback=self._queue_state)

    def _queue_state(self, state: ProductionViewState):
        self.after(0, lambda: self._apply_state(state))

    def _apply_state(self, state: ProductionViewState):
        self.status_label.configure(text=state.message if not state.error else f"{state.message}: {state.error}")
        self.progress.set(state.progress)
        self.start_button.configure(state="normal" if state.can_start else "disabled")
        self.resume_button.configure(state="normal" if state.can_resume else "disabled")
        self.cancel_button.configure(state="normal" if state.can_cancel else "disabled")
        icons = {"pending": "○", "running": "▶", "complete": "✓", "failed": "✗", "cancelled": "■"}
        for stage in state.stages:
            icon, detail = self.stage_rows[stage.name]
            icon.configure(text=icons.get(stage.status, "○"))
            detail.configure(text=stage.message or stage.status.title())

    def _start(self, *, resume: bool):
        project = self._selected_project()
        folder = self._project_folder()
        if project is None or folder is None:
            messagebox.showerror("Production", "Select a valid project.")
            return
        topic = self.topic_entry.get().strip()
        if not topic:
            messagebox.showerror("Production", "Enter a topic.")
            return
        try:
            provider_settings = self._provider_settings()
            provider_settings.validate()
            ProviderSettingsStore(folder).save(provider_settings)
            if self.controller is not None:
                self.controller.close()
            self.controller = self._make_controller(folder, provider_settings)
            settings = production_settings_from_app(self.app.settings)
            action = self.controller.resume if resume else self.controller.start
            action(project, folder, settings, topic=topic, launch_resolve=self.launch_resolve.get())
        except (ProviderSetupError, ValueError, OSError) as error:
            messagebox.showerror("Production Setup", str(error))

    def start_production(self):
        self._start(resume=False)

    def resume_production(self):
        self._start(resume=True)

    def cancel_production(self):
        if self.controller is not None:
            self.controller.cancel()

    def open_project_folder(self):
        folder = self._project_folder()
        if folder is None:
            return
        try:
            os.startfile(folder)
        except Exception as error:
            messagebox.showerror("Project Folder", str(error))

    def destroy(self):
        if self.controller is not None:
            self.controller.close()
        super().destroy()


__all__ = [
    "ProductionPage",
    "production_settings_from_app",
    "project_choice",
    "selected_asset_providers",
]
