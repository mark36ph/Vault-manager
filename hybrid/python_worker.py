"""JSON-lines production executor for the .NET desktop shell."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from threading import Lock
from typing import Any, Mapping

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

            if command == "start_production":
                runtime.start(payload)
                emit({"type": "accepted", "request_id": request_id, "command": command})
            elif command == "export_resolve":
                result = runtime.export_resolve(payload)
                emit({**result, "request_id": request_id})
            elif command == "cancel_production":
                emit({"type": "cancel_requested", "request_id": request_id, "accepted": runtime.cancel()})
            elif command == "shutdown":
                if runtime.running:
                    raise ValueError("cannot shut down the worker while production is running")
                emit({"type": "shutdown", "request_id": request_id})
                return 0
            else:
                raise ValueError(f"unknown production command: {command or '<empty>'}")
        except (json.JSONDecodeError, ValueError, OSError, RuntimeError) as error:
            emit({"type": "error", "request_id": request_id, "message": str(error)})

    return 0


if __name__ == "__main__":
    raise SystemExit(run_worker())
