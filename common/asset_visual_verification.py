"""Visual relevance verification for downloaded production images."""
from __future__ import annotations

import base64
import json
import mimetypes
import urllib.request
from pathlib import Path
from typing import Any, Callable, Mapping

from common.asset_acquisition import AcquiredAsset

JsonTransport = Callable[[urllib.request.Request], Mapping[str, Any]]


class AssetVisualVerificationError(RuntimeError):
    """Raised when the visual verification service returns an unusable response."""


def _json_transport(request: urllib.request.Request) -> Mapping[str, Any]:
    with urllib.request.urlopen(request, timeout=45) as response:
        payload = json.loads(response.read().decode("utf-8"))
    if not isinstance(payload, Mapping):
        raise AssetVisualVerificationError("visual verifier response must be a JSON object")
    return payload


def _response_text(payload: Mapping[str, Any]) -> str:
    direct = str(payload.get("output_text") or "").strip()
    if direct:
        return direct

    chunks: list[str] = []
    for output in payload.get("output", []) if isinstance(payload.get("output"), list) else []:
        if not isinstance(output, Mapping):
            continue
        for content in output.get("content", []) if isinstance(output.get("content"), list) else []:
            if isinstance(content, Mapping) and content.get("text"):
                chunks.append(str(content["text"]))
    text = "\n".join(chunks).strip()
    if text:
        return text

    status = str(payload.get("status") or "").strip()
    incomplete = payload.get("incomplete_details")
    if status == "incomplete":
        reason = ""
        if isinstance(incomplete, Mapping):
            reason = str(incomplete.get("reason") or "").strip()
        raise AssetVisualVerificationError(
            "visual verifier response was incomplete"
            + (f": {reason}" if reason else "")
        )
    return ""


def _image_data_url(path: Path) -> str:
    mime_type = mimetypes.guess_type(path.name)[0] or "image/jpeg"
    if not mime_type.startswith("image/"):
        raise AssetVisualVerificationError(f"unsupported visual-verification file type: {path.suffix}")
    encoded = base64.b64encode(path.read_bytes()).decode("ascii")
    return f"data:{mime_type};base64,{encoded}"


def _parse_decision(text: str) -> tuple[str, float]:
    """Parse the verifier's compact strict JSON decision payload."""
    raw = str(text or "").strip()
    if raw.startswith("```"):
        lines = raw.splitlines()
        if lines and lines[0].startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        raw = "\n".join(lines).strip()

    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as error:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid JSON: {raw or '<empty>'}"
        ) from error

    if not isinstance(payload, Mapping):
        raise AssetVisualVerificationError("visual verifier decision must be a JSON object")

    decision = str(payload.get("decision") or "").strip().upper()
    if decision not in {"ACCEPT", "REJECT", "UNCERTAIN"}:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid decision: {decision or '<empty>'}"
        )

    try:
        confidence = float(payload.get("confidence"))
    except (TypeError, ValueError) as error:
        raise AssetVisualVerificationError("visual verifier confidence must be numeric") from error
    if not 0.0 <= confidence <= 1.0:
        raise AssetVisualVerificationError("visual verifier confidence must be between 0 and 1")

    return decision, confidence


class OpenAIImageRelevanceVerifier:
    """Use an image-capable OpenAI model as a strict post-download relevance gate."""

    ACCEPT_CONFIDENCE = 0.80

    def __init__(
        self,
        api_key: str,
        *,
        model: str = "gpt-5-mini",
        transport: JsonTransport = _json_transport,
    ) -> None:
        self.api_key = str(api_key or "").strip()
        self.model = str(model or "gpt-5-mini").strip()
        self.transport = transport
        if not self.api_key:
            raise ValueError("OpenAI API key is required for visual verification")
        if not self.model:
            raise ValueError("visual verification model is required")

    def __call__(self, query: str, asset: AcquiredAsset) -> bool:
        path = Path(asset.path)
        if asset.candidate.kind != "image":
            return True
        if not path.is_file() or path.stat().st_size <= 0:
            return False

        scene_query = " ".join(str(query or "").split()).strip()
        candidate_title = " ".join(str(asset.candidate.title or "").split()).strip()
        instruction = (
            "You are a visual quality gate for factual short-form video stock imagery. "
            "Judge what is visibly present in the image. Treat filenames, tags, and stock metadata as hints, "
            "never as proof. The goal is to reject clearly wrong imagery without rejecting useful, plausible stock visuals.\n\n"
            f"Scene search query: {scene_query}\n"
            f"Stock metadata title: {candidate_title}\n\n"
            "Rules:\n"
            "- ACCEPT when the image is a useful literal or scientifically/physically plausible visual for the scene and is consistent with the named subject.\n"
            "- A subject does NOT need to be uniquely identifiable from pixels alone when that is unrealistic for stock imagery. For planets, moons, microscopic objects, deep-space objects, ancient events, and similar subjects, accept a plausible representation that is consistent with the requested subject and does not visibly contradict it.\n"
            "- Example: for Venus, a yellow/orange/cloud-covered rocky planet, crescent planet, hot planetary surface, or plausible Venus illustration can be ACCEPTED even if the image itself cannot prove it is Venus.\n"
            "- REJECT when the image visibly depicts a different identifiable subject or an obvious mismatch. Earth with blue oceans/continents is not Venus.\n"
            "- REJECT fantasy creatures, dragons, monsters, unrelated people, statues, logos, decorative symbols, generic diagrams, generic rockets/UFOs, unrelated animals, and unrelated objects unless the scene explicitly asks for them.\n"
            "- Symbolic or abstract artwork should be REJECTED when a literal visual is reasonably available.\n"
            "- If the image is broadly compatible with the named subject and has no visible contradiction, prefer ACCEPT over UNCERTAIN.\n"
            "- Use UNCERTAIN only when there is a real visual conflict or ambiguity that could make the image misleading.\n"
            "- Confidence measures confidence in the decision, not image quality. Use ACCEPT only when confidence is at least 0.80.\n"
            "- Keep the decision concise; no explanation is needed."
        )

        body = {
            "model": self.model,
            # max_output_tokens includes hidden reasoning tokens for reasoning models.
            # Keep reasoning minimal and leave ample room for the tiny JSON object.
            "max_output_tokens": 800,
            "reasoning": {"effort": "minimal"},
            "text": {
                "verbosity": "low",
                "format": {
                    "type": "json_schema",
                    "name": "visual_relevance_decision",
                    "description": "Compact strict visual relevance decision for a stock image.",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "decision": {
                                "type": "string",
                                "enum": ["ACCEPT", "REJECT", "UNCERTAIN"],
                            },
                            "confidence": {
                                "type": "number",
                                "minimum": 0,
                                "maximum": 1,
                            },
                        },
                        "required": ["decision", "confidence"],
                        "additionalProperties": False,
                    },
                },
            },
            "input": [
                {
                    "role": "user",
                    "content": [
                        {"type": "input_text", "text": instruction},
                        {
                            "type": "input_image",
                            "image_url": _image_data_url(path),
                            "detail": "high",
                        },
                    ],
                }
            ],
        }
        request = urllib.request.Request(
            "https://api.openai.com/v1/responses",
            data=json.dumps(body).encode("utf-8"),
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
                "Accept": "application/json",
                "User-Agent": "FactVaultManager/1.0",
            },
            method="POST",
        )
        payload = self.transport(request)
        decision, confidence = _parse_decision(_response_text(payload))
        return decision == "ACCEPT" and confidence >= self.ACCEPT_CONFIDENCE


__all__ = [
    "AssetVisualVerificationError",
    "OpenAIImageRelevanceVerifier",
]
