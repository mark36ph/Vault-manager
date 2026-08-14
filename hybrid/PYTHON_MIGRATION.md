# Python to C# Migration

Goal: make the desktop application fully C# while preserving current production behavior during the transition.

## Already owned by C#

- Main desktop shell and navigation
- Dashboard
- Projects browser/cards
- Project editor
- New Fact / ChatGPT paste workflow
- Direct SQLite project/settings access
- Media Library
- Asset Review
- Production UI, progress display and controls
- Provider settings UI
- Project status/file operations
- Installed-data migration and updater UI
- Production project catalog/readiness (`list_projects` retired from Python)
- Production executor connection/ready handshake (`ping` and `status` retired from Python)
- Native Pexels/Pixabay search clients and configured-provider registry (not yet switched into production execution)

## Runtime Python still in use

The C# shell currently starts `hybrid/python_worker.py`, which delegates production execution to `hybrid/production_runtime.py`. Project discovery/readiness and worker connection status are now handled natively by C#.

### Worker/executor

- `hybrid/python_worker.py`
- `hybrid/production_runtime.py`
- Remaining Python worker commands: start production, Resolve export, cancel, shutdown

### Production orchestration

- `common/production_ui.py`
- production registry/stage orchestration used by the configured providers
- checkpoint/resume/cancellation state

### OpenAI/media providers

- `common/provider_setup.py`
- OpenAI text/TTS provider code reached through provider setup
- Python Pexels/Pixabay integrations are still used by production until the C# asset-acquisition path is wired in
- Native C# equivalents now exist in `NativeAssetProviders.cs` and `NativeAssetProviderRegistry.cs`

### Asset acquisition and verification

- `common/asset_visual_verification.py`
- `common/named_subject_verification.py`
- `common/verified_asset_acquisition.py`
- `common/mixed_asset_acquisition.py`
- `common/named_asset_hierarchy.py`

### Timeline / Resolve

- `common/resolve_production.py`
- `common/fcpxml_paths.py`
- timeline/assembly helpers reached by the production registry

### Legacy Python UI/reference

The `pages/`, `widgets/`, and Python desktop UI entry-point code are no longer the target UI. Keep them as reference until their behavior has been checked against the C# shell, then remove them once the C# migration is complete.

## Migration order

1. **Project catalog/readiness — COMPLETE** — C# `ProductionProjectCatalog` now supplies the Production page. Python `list_projects` and `HybridProductionRuntime.list_projects()` have been removed.
2. **Worker protocol shell — COMPLETE** — C# now owns executor connection/readiness. Python `ping`, `status`, protocol handshake and ready event have been removed; Python is limited to production execution/control commands.
3. **Provider settings and HTTP clients — IN PROGRESS** — native Pexels and Pixabay search clients plus a settings-backed provider registry are now in C#. Production still uses the Python provider path until native asset acquisition is connected. OpenAI HTTP clients remain to be ported.
4. **Voice/TTS** — move OpenAI narration generation to C# and preserve existing voice file/checkpoint conventions.
5. **Asset acquisition** — port provider querying, ranking, download, image/video preparation and format handling, then switch production from Python Pexels/Pixabay to the native clients.
6. **Visual/named-subject verification** — port the current topic-neutral verifier behavior and uncertainty/fallback rules with parity tests.
7. **Timeline/FFmpeg** — port production assembly, inline captions and FFmpeg process control while preserving current output files.
8. **Resolve/FCPXML** — port export generation and path rebasing to C#.
9. **Production orchestration** — replace Python stage registry/controller, cancellation, checkpoint/resume and progress events with C# equivalents.
10. **Remove Python worker/runtime** — only after reproduce/resume/export behavior matches the established production baseline.
11. **Remove legacy Python UI** — after confirming no production module imports UI code.

## Migration rule

Do not delete or bypass a Python production implementation until the corresponding C# implementation has been exercised against real projects. During migration, keep file names, project-folder structure, checkpoint files, timeline data and Resolve output compatible so existing projects remain usable.
