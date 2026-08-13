"""JSON-lines worker for the hybrid .NET desktop shell."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from threading import Lock
from typing import Any, Mapping

PROTOCOL_VERSION = 2
ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from hybrid.production_runtime import HybridProductionRuntime

_EMIT_LOCK = Lock()


def emit(payload: Mapping[str, Any]) -> None:
    with _EMIT_LOCK:
        print(json.dumps(dict(payload), ensure_ascii=False), flush=True)


def run_worker() -> int:
    runtime = HybridProductionRuntime(emit)
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
        request_id = None
        try:
            payload = json.loads(raw)
            if not isinstance(payload, Mapping):
                raise ValueError("command must be a JSON object")
            command = str(payload.get("command") or "").strip().casefold()
            request_id = payload.get("request_id")

            if command == "ping":
                emit({"type": "pong", "request_id": request_id, "protocol": PROTOCOL_VERSION})
            elif command == "status":
                emit(
                    {
                        "type": "status",
                        "request_id": request_id,
                        "root": str(ROOT),
                        "legacy_entry": str(ROOT / "main.py"),
                        "production_running": runtime.running,
                    }
                )
            elif command == "list_projects":
                emit({"type": "projects", "request_id": request_id, "projects": runtime.list_projects()})
            elif command == "start_production":
                runtime.start(payload)
                emit({"type": "accepted", "request_id": request_id, "command": command})
            elif command == "cancel_production":
                emit({"type": "cancel_requested", "request_id": request_id, "accepted": runtime.cancel()})
            elif command == "shutdown":
                if runtime.running:
                    raise ValueError("cannot shut down the worker while production is running")
                emit({"type": "shutdown", "request_id": request_id})
                return 0
            else:
                raise ValueError(f"unknown command: {command or '<empty>'}")
        except (json.JSONDecodeError, ValueError, OSError, RuntimeError) as error:
            emit({"type": "error", "request_id": request_id, "message": str(error)})

    return 0


if __name__ == "__main__":
    raise SystemExit(run_worker())
