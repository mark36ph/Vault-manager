import importlib
import os
import platform
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class ResolveStatus:
    installed: bool
    application_path: Path | None
    scripting_module_available: bool
    connected: bool
    product_name: str = ""
    version: str = ""
    message: str = ""


def candidate_application_paths(system_name=None):
    system_name = system_name or platform.system()
    if system_name == "Windows":
        return [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files"))
            / "Blackmagic Design"
            / "DaVinci Resolve"
            / "Resolve.exe",
        ]
    if system_name == "Darwin":
        return [Path("/Applications/DaVinci Resolve/DaVinci Resolve.app")]
    return [Path("/opt/resolve/bin/resolve")]


def find_resolve_application(configured_path="", system_name=None):
    configured = Path(str(configured_path or "").strip()).expanduser()
    if str(configured_path or "").strip() and configured.exists():
        return configured
    for candidate in candidate_application_paths(system_name):
        if candidate.exists():
            return candidate
    return None


def candidate_scripting_paths(system_name=None):
    system_name = system_name or platform.system()
    if system_name == "Windows":
        program_data = Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData"))
        return [
            program_data
            / "Blackmagic Design"
            / "DaVinci Resolve"
            / "Support"
            / "Developer"
            / "Scripting"
            / "Modules",
        ]
    if system_name == "Darwin":
        return [
            Path("/Library/Application Support/Blackmagic Design/DaVinci Resolve/Developer/Scripting/Modules")
        ]
    return [Path("/opt/resolve/Developer/Scripting/Modules")]


def ensure_scripting_module_path(configured_path="", system_name=None):
    paths = []
    if str(configured_path or "").strip():
        paths.append(Path(configured_path).expanduser())
    paths.extend(candidate_scripting_paths(system_name))
    for path in paths:
        if path.exists():
            path_text = str(path)
            if path_text not in sys.path:
                sys.path.insert(0, path_text)
            return path
    return None


def load_scripting_module(configured_module_path=""):
    ensure_scripting_module_path(configured_module_path)
    try:
        return importlib.import_module("DaVinciResolveScript")
    except (ImportError, OSError):
        return None


def connect_to_resolve(configured_module_path=""):
    module = load_scripting_module(configured_module_path)
    if module is None:
        return None
    try:
        return module.scriptapp("Resolve")
    except Exception:
        return None


def inspect_resolve(configured_application_path="", configured_module_path=""):
    application_path = find_resolve_application(configured_application_path)
    module = load_scripting_module(configured_module_path)
    resolve = None
    if module is not None:
        try:
            resolve = module.scriptapp("Resolve")
        except Exception:
            resolve = None

    product_name = ""
    version = ""
    if resolve is not None:
        try:
            product_name = str(resolve.GetProductName() or "")
        except Exception:
            pass
        try:
            version = str(resolve.GetVersionString() or "")
        except Exception:
            pass

    if resolve is not None:
        message = "Connected to DaVinci Resolve."
    elif module is not None:
        message = "Resolve scripting is available, but Resolve is not currently connected."
    elif application_path is not None:
        message = "Resolve is installed, but its Python scripting module was not found."
    else:
        message = "DaVinci Resolve was not detected."

    return ResolveStatus(
        installed=application_path is not None,
        application_path=application_path,
        scripting_module_available=module is not None,
        connected=resolve is not None,
        product_name=product_name,
        version=version,
        message=message,
    )


__all__ = [
    "ResolveStatus",
    "candidate_application_paths",
    "candidate_scripting_paths",
    "connect_to_resolve",
    "ensure_scripting_module_path",
    "find_resolve_application",
    "inspect_resolve",
    "load_scripting_module",
]
