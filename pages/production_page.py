from __future__ import annotations

import os
import time
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
from common.resolve_production import ResolveProductionService
from pages.base_page import BasePage

STAGE_LABELS = {
    "research": "Research",
    "facts": "Select Facts",
    "script": "Write Script",
    "image_prompts": "Find Visuals",
    "voice": "Generate Voice",
    "timeline": "Build Timeline",
    "resolve": "Create Resolve Export",
}

CARD_FG = ("#FFFFFF", "#171A20")
CARD_BORDER = ("#E4E7EC", "#292D36")
MUTED_TEXT = ("#667085", "#8F96A3")
SOFT_HOVER = ("#F2F4F7", "#252A33")
READY_TEXT = ("#027A48", "#75E0A7")
WARNING_TEXT = ("#B54708", "#FEC84B")


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


def format_elapsed(seconds: float) -> str:
    seconds = max(0, int(seconds))
    minutes, seconds = divmod(seconds, 60)
    hours, minutes = divmod(minutes, 60)
    if hours:
        return f"{hours:d}:{minutes:02d}:{seconds:02d}"
    return f"{minutes:02d}:{seconds:02d}"


def progress_percent(progress: float) -> str:
    return f"{round(max(0.0, min(1.0, progress)) * 100):d}%"


def should_mark_project_completed(
    project: Mapping[str, Any] | None,
    state: ProductionViewState,
) -> bool:
    """Return True only for a fully successful In Progress production run."""
    if project is None or str(project.get("status") or "") != "In Progress":
        return False
    if state.running or state.error or state.result is None:
        return False
    return bool(state.stages) and all(stage.status == "complete" for stage in state.stages)


class ProductionPage(BasePage):
    """Visible desktop page for configuring and monitoring production."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Production")
        self.app = app
        self.projects = [dict(project) for project in self.pm.get_all_projects()]
        self.project_lookup = {project_choice(project): project for project in self.projects}
        self.controller: ProductionUIController | None = None
        self.stage_rows: dict[str, tuple[ctk.CTkLabel, ctk.CTkLabel]] = {}
        self.run_started_at: float | None = None
        self.last_log_key: tuple[Any, ...] | None = None
        self.build()

    def build(self):
        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=26, pady=(22, 2))

        intro = ctk.CTkLabel(
            self,
            text="Build media, narration and timeline assets, then export to DaVinci Resolve Free.",
            font=("Segoe UI", 13),
            text_color=MUTED_TEXT,
            anchor="w",
        )
        intro.pack(fill="x", padx=26, pady=(0, 16))

        self.content.pack_configure(padx=26, pady=(0, 22))

        body = ctk.CTkFrame(self.content, fg_color="transparent")
        body.pack(fill="both", expand=True)
        body.grid_columnconfigure(0, weight=4, minsize=330)
        body.grid_columnconfigure(1, weight=7, minsize=500)
        body.grid_rowconfigure(0, weight=1)

        self._build_controls(body)
        self._build_progress(body)
        self._load_selected_project()

    def _section_label(self, parent, text):
        label = ctk.CTkLabel(
            parent,
            text=text,
            font=("Segoe UI", 12, "bold"),
            text_color=MUTED_TEXT,
            anchor="w",
        )
        label.pack(fill="x", padx=14, pady=(12, 6))
        return label

    def _build_controls(self, parent):
        panel = ctk.CTkScrollableFrame(
            parent,
            corner_radius=10,
            fg_color=CARD_FG,
            border_width=1,
            border_color=CARD_BORDER,
            label_text="Setup",
            label_font=("Segoe UI", 14, "bold"),
        )
        panel.grid(row=0, column=0, sticky="nsew", padx=(0, 6))

        self._section_label(panel, "PROJECT")
        choices = list(self.project_lookup) or ["No projects available"]
        self.project_menu = ctk.CTkOptionMenu(
            panel,
            values=choices,
            height=34,
            corner_radius=7,
            command=lambda _: self._load_selected_project(),
        )
        self.project_menu.pack(fill="x", padx=14, pady=(0, 6))

        self.topic_entry = ctk.CTkEntry(
            panel,
            placeholder_text="Video topic",
            height=34,
            corner_radius=7,
            border_width=1,
        )
        self.topic_entry.pack(fill="x", padx=14, pady=(0, 8))

        self.project_status_label = ctk.CTkLabel(
            panel,
            text="Select a project to check production readiness.",
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
            anchor="w",
            justify="left",
            wraplength=300,
        )
        self.project_status_label.pack(fill="x", padx=14, pady=(0, 4))

        self._section_label(panel, "MEDIA")
        provider_box = ctk.CTkFrame(panel, fg_color="transparent")
        provider_box.pack(fill="x", padx=14, pady=(0, 4))

        self.use_pexels = ctk.BooleanVar(value=True)
        self.use_pixabay = ctk.BooleanVar(value=True)
        self.pexels_checkbox = ctk.CTkCheckBox(
            provider_box,
            text="Pexels",
            variable=self.use_pexels,
            checkbox_width=18,
            checkbox_height=18,
            font=("Segoe UI", 13),
            command=self._settings_changed,
        )
        self.pexels_checkbox.pack(side="left", padx=(0, 18))
        self.pixabay_checkbox = ctk.CTkCheckBox(
            provider_box,
            text="Pixabay",
            variable=self.use_pixabay,
            checkbox_width=18,
            checkbox_height=18,
            font=("Segoe UI", 13),
            command=self._settings_changed,
        )
        self.pixabay_checkbox.pack(side="left")

        self.asset_kind = ctk.CTkSegmentedButton(
            panel,
            values=["image", "video"],
            height=32,
            corner_radius=7,
            font=("Segoe UI", 12),
            command=lambda _value: self._settings_changed(),
        )
        self.asset_kind.set("image")
        self.asset_kind.pack(fill="x", padx=14, pady=(8, 6))

        self.voice_enabled = ctk.BooleanVar(value=True)
        self.voice_checkbox = ctk.CTkCheckBox(
            panel,
            text="Generate OpenAI narration",
            variable=self.voice_enabled,
            checkbox_width=18,
            checkbox_height=18,
            font=("Segoe UI", 13),
            command=self._settings_changed,
        )
        self.voice_checkbox.pack(anchor="w", padx=14, pady=(4, 8))

        self._section_label(panel, "PROVIDER STATUS")
        credentials = ctk.CTkFrame(
            panel,
            corner_radius=8,
            fg_color=("#F8F9FB", "#1D2128"),
            border_width=1,
            border_color=CARD_BORDER,
        )
        credentials.pack(fill="x", padx=14, pady=(0, 8))
        self.credential_label = ctk.CTkLabel(
            credentials,
            text="Checking credentials...",
            justify="left",
            anchor="w",
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
        )
        self.credential_label.pack(fill="x", padx=12, pady=10)

        self._section_label(panel, "ACTIONS")
        buttons = ctk.CTkFrame(panel, fg_color="transparent")
        buttons.pack(fill="x", padx=14, pady=(0, 16))

        self.start_button = ctk.CTkButton(
            buttons,
            text="▶ Produce Video",
            height=38,
            corner_radius=7,
            font=("Segoe UI", 13, "bold"),
            command=self.start_production,
        )
        self.start_button.pack(fill="x", pady=(0, 5))

        secondary = {
            "height": 34,
            "corner_radius": 7,
            "fg_color": "transparent",
            "border_width": 1,
            "border_color": CARD_BORDER,
            "hover_color": SOFT_HOVER,
            "text_color": ("#344054", "#D0D5DD"),
            "font": ("Segoe UI", 12),
        }

        self.resume_button = ctk.CTkButton(
            buttons,
            text="↻ Resume Production",
            command=self.resume_production,
            **secondary,
        )
        self.resume_button.pack(fill="x", pady=3)

        self.export_resolve_button = ctk.CTkButton(
            buttons,
            text="⬆ Create Resolve Export",
            command=self.export_to_resolve_free,
            **secondary,
        )
        self.export_resolve_button.pack(fill="x", pady=3)

        self.cancel_button = ctk.CTkButton(
            buttons,
            text="■ Cancel",
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#FDA29B", "#7A3A3A"),
            hover_color=("#FEF3F2", "#442626"),
            text_color=("#B42318", "#FDA29B"),
            font=("Segoe UI", 12),
            command=self.cancel_production,
            state="disabled",
        )
        self.cancel_button.pack(fill="x", pady=3)

        self.open_button = ctk.CTkButton(
            buttons,
            text="📂 Open Project Folder",
            command=self.open_project_folder,
            **secondary,
        )
        self.open_button.pack(fill="x", pady=3)

    def _build_progress(self, parent):
        panel = ctk.CTkFrame(
            parent,
            corner_radius=10,
            fg_color=CARD_FG,
            border_width=1,
            border_color=CARD_BORDER,
        )
        panel.grid(row=0, column=1, sticky="nsew", padx=(6, 0))
        panel.grid_columnconfigure(0, weight=1)
        panel.grid_rowconfigure(2, weight=3)
        panel.grid_rowconfigure(3, weight=2)

        overview = ctk.CTkFrame(panel, fg_color="transparent")
        overview.grid(row=0, column=0, sticky="ew", padx=16, pady=(14, 6))
        overview.grid_columnconfigure(0, weight=1)

        self.status_label = ctk.CTkLabel(
            overview,
            text="Ready",
            font=("Segoe UI", 17, "bold"),
            anchor="w",
        )
        self.status_label.grid(row=0, column=0, sticky="w")

        self.percent_label = ctk.CTkLabel(
            overview,
            text="0%",
            font=("Segoe UI", 18, "bold"),
            text_color=("#175CD3", "#AFCBFF"),
        )
        self.percent_label.grid(row=0, column=1, sticky="e")

        self.elapsed_label = ctk.CTkLabel(
            overview,
            text="Elapsed 00:00",
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
        )
        self.elapsed_label.grid(row=1, column=0, sticky="w", pady=(2, 0))

        self.progress = ctk.CTkProgressBar(panel, height=8, corner_radius=4)
        self.progress.set(0)
        self.progress.grid(row=1, column=0, sticky="ew", padx=16, pady=(0, 12))

        stages = ctk.CTkScrollableFrame(
            panel,
            label_text="Workflow",
            label_font=("Segoe UI", 13, "bold"),
            corner_radius=8,
            fg_color=("#F8F9FB", "#1D2128"),
            border_width=1,
            border_color=CARD_BORDER,
        )
        stages.grid(row=2, column=0, sticky="nsew", padx=16, pady=(0, 8))

        for stage in STAGES:
            row = ctk.CTkFrame(
                stages,
                height=38,
                corner_radius=6,
                fg_color="transparent",
            )
            row.pack(fill="x", pady=1)
            row.pack_propagate(False)

            icon = ctk.CTkLabel(
                row,
                text="○",
                width=28,
                font=("Segoe UI", 15, "bold"),
                text_color=MUTED_TEXT,
            )
            icon.pack(side="left", padx=(8, 2))

            ctk.CTkLabel(
                row,
                text=STAGE_LABELS[stage],
                font=("Segoe UI", 12, "bold"),
                anchor="w",
            ).pack(side="left")

            detail = ctk.CTkLabel(
                row,
                text="Waiting",
                font=("Segoe UI", 11),
                text_color=MUTED_TEXT,
                anchor="e",
            )
            detail.pack(side="right", padx=10)
            self.stage_rows[stage] = (icon, detail)

        log_frame = ctk.CTkFrame(
            panel,
            corner_radius=8,
            fg_color=("#F8F9FB", "#1D2128"),
            border_width=1,
            border_color=CARD_BORDER,
        )
        log_frame.grid(row=3, column=0, sticky="nsew", padx=16, pady=(0, 16))

        log_header = ctk.CTkFrame(log_frame, fg_color="transparent")
        log_header.pack(fill="x", padx=10, pady=(8, 4))
        ctk.CTkLabel(
            log_header,
            text="Production log",
            font=("Segoe UI", 12, "bold"),
            anchor="w",
        ).pack(side="left")
        ctk.CTkButton(
            log_header,
            text="Clear",
            width=54,
            height=24,
            fg_color="transparent",
            border_width=1,
            border_color=CARD_BORDER,
            text_color=("#344054", "#D0D5DD"),
            hover_color=SOFT_HOVER,
            command=self._clear_log,
        ).pack(side="right")

        self.log_box = ctk.CTkTextbox(
            log_frame,
            height=130,
            font=("Consolas", 11),
            wrap="word",
            corner_radius=6,
            border_width=0,
        )
        self.log_box.pack(fill="both", expand=True, padx=8, pady=(0, 8))
        self.log_box.insert("end", "Ready to start production.\n")
        self.log_box.configure(state="disabled")

    def _selected_project(self) -> Mapping[str, Any] | None:
        return self.project_lookup.get(self.project_menu.get())

    def _project_folder(self) -> Path | None:
        project = self._selected_project()
        if project is None:
            return None
        try:
            return Path(self.pm.resolve_project_folder(project))
        except Exception:
            try:
                return Path(self.pm.get_project_folder(project))
            except Exception:
                return None

    def _settings_changed(self):
        if self.controller is not None and self.controller.state.running:
            return
        try:
            settings = self._provider_settings()
            settings.validate()
        except Exception as error:
            self.credential_label.configure(text=f"Setup incomplete: {error}")
            self.start_button.configure(state="disabled")
            return
        self._refresh_credentials(settings)

    def _set_setup_enabled(self, enabled: bool):
        state = "normal" if enabled else "disabled"
        self.project_menu.configure(state=state)
        self.topic_entry.configure(state=state)
        self.pexels_checkbox.configure(state=state)
        self.pixabay_checkbox.configure(state=state)
        self.asset_kind.configure(state=state)
        self.voice_checkbox.configure(state=state)
        self.open_button.configure(state="normal" if self._project_folder() is not None else "disabled")

    def _load_selected_project(self):
        project = self._selected_project()
        folder = self._project_folder()
        if project is None or folder is None:
            self.credential_label.configure(text="Create a project before starting production.")
            self.project_status_label.configure(
                text="No valid project selected.",
                text_color=WARNING_TEXT,
            )
            self.start_button.configure(state="disabled")
            self.resume_button.configure(state="disabled")
            self.export_resolve_button.configure(state="disabled")
            self.open_button.configure(state="disabled")
            return

        self.topic_entry.delete(0, "end")
        self.topic_entry.insert(0, str(project.get("topic") or project.get("title") or ""))

        if not folder.exists():
            self.project_status_label.configure(
                text=f"Project folder not found: {folder}",
                text_color=WARNING_TEXT,
            )
            self.start_button.configure(state="disabled")
            self.resume_button.configure(state="disabled")
            self.export_resolve_button.configure(state="disabled")
            self.open_button.configure(state="disabled")
            return

        self.open_button.configure(state="normal")
        try:
            saved = ProviderSettingsStore(folder).load()
        except Exception as error:
            self.project_status_label.configure(
                text=f"Could not load saved production settings: {error}",
                text_color=WARNING_TEXT,
            )
            self.start_button.configure(state="disabled")
            return

        self.use_pexels.set("pexels" in saved.asset_providers)
        self.use_pixabay.set("pixabay" in saved.asset_providers)
        self.asset_kind.set(saved.asset_kind)
        self.voice_enabled.set(saved.voice_provider != "none")
        self._refresh_credentials(saved)

        checkpoint_exists = (folder / "production_checkpoint.json").is_file()
        timeline_exists = (folder / "timeline.json").is_file()
        self.resume_button.configure(state="normal" if checkpoint_exists else "disabled")
        self.export_resolve_button.configure(state="normal" if timeline_exists else "disabled")

        readiness = ["Project folder ready"]
        if checkpoint_exists:
            readiness.append("resume available")
        if timeline_exists:
            readiness.append("Resolve export available")
        self.project_status_label.configure(
            text=" • ".join(readiness),
            text_color=READY_TEXT,
        )

    def _provider_settings(self) -> ProviderSettings:
        return ProviderSettings(
            asset_providers=selected_asset_providers(
                self.use_pexels.get(),
                self.use_pixabay.get(),
            ),
            asset_kind=self.asset_kind.get(),
            voice_provider="openai" if self.voice_enabled.get() else "none",
        )

    def _refresh_credentials(self, settings: ProviderSettings):
        try:
            statuses = test_provider_credentials(
                settings,
                credentials=ProviderCredentials(),
            )
            lines = [f"{'✓' if item.configured else '✗'} {item.source}" for item in statuses]
            ready = bool(statuses) and all(item.configured for item in statuses)
            self.credential_label.configure(
                text="\n".join(lines) if lines else "No providers selected.",
                text_color=READY_TEXT if ready else WARNING_TEXT,
            )
            self.start_button.configure(state="normal" if ready else "disabled")
        except Exception as error:
            self.credential_label.configure(
                text=f"Provider setup error: {error}",
                text_color=WARNING_TEXT,
            )
            self.start_button.configure(state="disabled")

    def _make_controller(
        self,
        folder: Path,
        provider_settings: ProviderSettings,
    ) -> ProductionUIController:
        configured = build_configured_providers(folder, provider_settings)
        return ProductionUIController(
            configured.registry,
            state_callback=self._queue_state,
        )

    def _queue_state(self, state: ProductionViewState):
        self.after(0, lambda: self._apply_state(state))

    def _clear_log(self):
        self.log_box.configure(state="normal")
        self.log_box.delete("1.0", "end")
        self.log_box.configure(state="disabled")
        self.last_log_key = None

    def _append_log(self, text: str):
        self.log_box.configure(state="normal")
        self.log_box.insert("end", f"{time.strftime('%H:%M:%S')}  {text}\n")
        self.log_box.see("end")
        self.log_box.configure(state="disabled")

    def _mark_selected_project_completed(self, state: ProductionViewState):
        project = self._selected_project()
        if not should_mark_project_completed(project, state):
            return

        old_choice = self.project_menu.get()
        try:
            updated = dict(
                self.pm.change_project_status(
                    int(project["id"]),
                    "Completed",
                )
            )
        except Exception as error:
            self.status_label.configure(text="Production complete — status update failed")
            self._append_log(f"Could not mark project Completed: {error}")
            messagebox.showerror(
                "Project Status",
                (
                    "Production finished successfully, but the project could not be moved "
                    f"to Completed.\n\n{error}"
                ),
                parent=self,
            )
            return

        project_id = int(updated["id"])
        self.projects = [
            updated if int(item.get("id", -1)) == project_id else item
            for item in self.projects
        ]
        new_choice = project_choice(updated)
        self.project_lookup.pop(old_choice, None)
        self.project_lookup[new_choice] = updated
        self.project_menu.configure(values=list(self.project_lookup))
        self.project_menu.set(new_choice)
        self._append_log("Project status changed to Completed.")

    def _apply_state(self, state: ProductionViewState):
        message = state.message if not state.error else f"{state.message}: {state.error}"
        self.status_label.configure(text=message)
        self.progress.set(state.progress)
        self.percent_label.configure(text=progress_percent(state.progress))
        self.start_button.configure(state="normal" if state.can_start else "disabled")
        self.resume_button.configure(state="normal" if state.can_resume else "disabled")
        self.cancel_button.configure(state="normal" if state.can_cancel else "disabled")
        self._set_setup_enabled(not state.running)

        icons = {
            "pending": "○",
            "running": "▶",
            "complete": "✓",
            "failed": "✗",
            "cancelled": "■",
        }
        for stage in state.stages:
            icon, detail = self.stage_rows[stage.name]
            icon.configure(text=icons.get(stage.status, "○"))
            detail.configure(text=stage.message or stage.status.title())

        log_key = (
            state.current_stage,
            state.message,
            state.error,
            state.running,
            state.progress,
        )
        if log_key != self.last_log_key:
            self._append_log(message)
            self.last_log_key = log_key

        if not state.running and self.run_started_at is not None:
            elapsed = format_elapsed(time.monotonic() - self.run_started_at)
            prefix = "Stopped after" if state.error else "Completed in"
            self.elapsed_label.configure(text=f"{prefix} {elapsed}")
            self.run_started_at = None
            self._mark_selected_project_completed(state)
            self.after(0, self._load_selected_project)

    def _tick_elapsed(self):
        if (
            self.run_started_at is not None
            and self.controller is not None
            and self.controller.state.running
        ):
            self.elapsed_label.configure(
                text=f"Elapsed {format_elapsed(time.monotonic() - self.run_started_at)}"
            )
            self.after(1000, self._tick_elapsed)

    def _start(self, *, resume: bool):
        project = self._selected_project()
        folder = self._project_folder()
        if project is None or folder is None:
            messagebox.showerror("Production", "Select a valid project.", parent=self)
            return
        if not folder.exists():
            messagebox.showerror(
                "Production",
                f"The project folder could not be found:\n\n{folder}",
                parent=self,
            )
            return

        topic = self.topic_entry.get().strip()
        if not topic:
            messagebox.showerror("Production", "Enter a topic.", parent=self)
            self.topic_entry.focus_set()
            return

        if self.controller is not None and self.controller.state.running:
            messagebox.showwarning(
                "Production",
                "Production is already running for this page.",
                parent=self,
            )
            return

        try:
            provider_settings = self._provider_settings()
            provider_settings.validate()
            ProviderSettingsStore(folder).save(provider_settings)

            if self.controller is not None:
                self.controller.close()

            self.controller = self._make_controller(folder, provider_settings)
            self.run_started_at = time.monotonic()
            self.last_log_key = None
            self.elapsed_label.configure(text="Elapsed 00:00")
            self._set_setup_enabled(False)
            self.start_button.configure(state="disabled")
            self.resume_button.configure(state="disabled")
            self.cancel_button.configure(state="normal")
            self._append_log(
                f"{'Resuming' if resume else 'Starting'} production for: {topic}"
            )
            self._tick_elapsed()

            action = self.controller.resume if resume else self.controller.start
            options = {
                "topic": topic,
                "launch_resolve": False,
            }

            if not resume:
                options["start_at"] = "image_prompts"

            action(
                project,
                folder,
                production_settings_from_app(self.app.settings),
                **options,
            )
        except (ProviderSetupError, ValueError, OSError) as error:
            self.run_started_at = None
            self._set_setup_enabled(True)
            self.cancel_button.configure(state="disabled")
            self._append_log(f"Production setup failed: {error}")
            messagebox.showerror("Production Setup", str(error), parent=self)
            self._load_selected_project()

    def start_production(self):
        self._start(resume=False)

    def resume_production(self):
        self._start(resume=True)

    def export_to_resolve_free(self):
        project = self._selected_project()
        folder = self._project_folder()
        if project is None or folder is None:
            messagebox.showerror("Resolve Export", "Select a valid project.", parent=self)
            return

        timeline_path = folder / "timeline.json"
        if not timeline_path.is_file():
            messagebox.showerror(
                "Resolve Export",
                "This project does not have a completed timeline yet.",
                parent=self,
            )
            return

        self.export_resolve_button.configure(
            state="disabled",
            text="Creating export...",
        )
        self.status_label.configure(text="Creating Resolve export...")
        self._append_log("Creating Resolve export...")
        self.update_idletasks()

        try:
            result = ResolveProductionService().run(
                project,
                folder,
                production_settings_from_app(self.app.settings),
                materialize=False,
                launch=False,
            )
        except Exception as error:
            self.status_label.configure(text="Resolve export failed")
            self._append_log(f"Resolve export failed: {error}")
            messagebox.showerror("Resolve Export", str(error), parent=self)
        else:
            self.status_label.configure(text="Resolve export ready")
            self._append_log(f"Resolve FCPXML created: {result.fcpxml.path}")
            messagebox.showinfo(
                "Resolve Export",
                (
                    "FCPXML created successfully.\n\n"
                    f"{result.fcpxml.path}\n\n"
                    "Import this file in DaVinci Resolve using File > Import > Timeline."
                ),
                parent=self,
            )
            try:
                os.startfile(result.fcpxml.path.parent)
            except Exception:
                pass
        finally:
            self.export_resolve_button.configure(
                state="normal",
                text="⬆ Create Resolve Export",
            )

    def cancel_production(self):
        if self.controller is None or not self.controller.state.running:
            return
        self.cancel_button.configure(state="disabled", text="Cancelling...")
        self._append_log("Cancellation requested...")
        try:
            self.controller.cancel()
        finally:
            self.cancel_button.configure(text="■ Cancel")

    def open_project_folder(self):
        folder = self._project_folder()
        if folder is None:
            messagebox.showerror("Project Folder", "Select a valid project.", parent=self)
            return
        if not folder.exists():
            messagebox.showerror(
                "Project Folder",
                f"The project folder could not be found:\n\n{folder}",
                parent=self,
            )
            return
        try:
            os.startfile(folder)
        except Exception as error:
            messagebox.showerror("Project Folder", str(error), parent=self)

    def destroy(self):
        if self.controller is not None:
            self.controller.close()
        super().destroy()


__all__ = [
    "ProductionPage",
    "format_elapsed",
    "production_settings_from_app",
    "progress_percent",
    "project_choice",
    "selected_asset_providers",
    "should_mark_project_completed",
]
