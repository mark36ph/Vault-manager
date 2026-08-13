"""Runtime paths shared by development checkouts and installed builds."""
from __future__ import annotations

import os
from pathlib import Path

DATA_DIR_ENV = "FACTVAULT_DATA_DIR"


def data_dir() -> Path:
    """Return the writable application data directory.

    Development keeps the historical ``data`` folder by default. Installed builds
    set ``FACTVAULT_DATA_DIR`` before Python starts so updates never replace user
    settings or the SQLite database.
    """
    override = str(os.environ.get(DATA_DIR_ENV, "") or "").strip()
    return Path(override).expanduser() if override else Path("data")


def data_path(*parts: str) -> Path:
    return data_dir().joinpath(*parts)


__all__ = ["DATA_DIR_ENV", "data_dir", "data_path"]
