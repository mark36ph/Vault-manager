"""JSON-lines worker for the hybrid .NET desktop shell."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Mapping

PROTOCOL_VERSION = 1
ROOT = Path(__file__).resolve().parent.parent


def emit(payload: Mapping[str, Any]) -> None:
    print(json.dumps(dict(payload), ensure_ascii=False), flush=True)


def run_worker() -> int:
    emit(
        {
            "type": "ready",
            "protocol": PROTOCOL_VERSION,
            "python": sys.version.split()[0],
            "root": str(ROOT),
        }
    )

    for raw_line in sys.stdin:
        raw = raw_line.strip()
        if not raw:
            continue
        try:
            payload = json.loads(raw)
            if not isinstance(payload, Mapping):
                raise ValueError("command must be a JSON object")
            command = str(payload.get("command") or "").strip().casefold()
            request_id = payload.get("request_id")
            if command == "ping":
                emit(
                    {
                        "type": "pong",
                        "request_id": request_id,
                        "protocol": PROTOCOL_VERSION,
                    }
                )
            elif command == "status":
                emit(
                    {
                        "type": "status",
                        "request_id": request_id,
                        "root": str(ROOT),
                        "legacy_entry": str(ROOT / "main.py"),
                    }
                )
            elif command == "shutdown":
                emit({"type": "shutdown", "request_id": request_id})
                return 0
            else:
                raise ValueError(f"unknown command: {command or '<empty>'}")
        except (json.JSONDecodeError, ValueError) as error:
            emit({"type": "error", "message": str(error)})

    return 0


if __name__ == "__main__":
    raise SystemExit(run_worker())
