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


_HARD_NEGATIVES = {
    "none",
    "unrequested_earth",
    "unrequested_dragon_or_fantasy_creature",
    "unrequested_person",
    "unrequested_statue",
    "unrequested_animal",
    "unrequested_rocket_or_ufo",
    "unrequested_logo_or_symbol",
    "unrequested_generic_diagram",
    "other_obvious_unrelated_subject",
}


def _parse_mismatch(text: str) -> tuple[bool, float, str, float]:
    """Parse mismatch scoring plus the independent hard-negative classification."""
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

    obvious_mismatch = payload.get("obvious_mismatch")
    if not isinstance(obvious_mismatch, bool):
        raise AssetVisualVerificationError("visual verifier obvious_mismatch must be boolean")

    try:
        confidence = float(payload.get("confidence"))
    except (TypeError, ValueError) as error:
        raise AssetVisualVerificationError("visual verifier confidence must be numeric") from error
    if not 0.0 <= confidence <= 1.0:
        raise AssetVisualVerificationError("visual verifier confidence must be between 0 and 1")

    hard_negative = str(payload.get("hard_negative") or "").strip()
    if hard_negative not in _HARD_NEGATIVES:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid hard_negative: {hard_negative or '<empty>'}"
        )

    try:
        hard_negative_confidence = float(payload.get("hard_negative_confidence"))
    except (TypeError, ValueError) as error:
        raise AssetVisualVerificationError(
            "visual verifier hard_negative_confidence must be numeric"
        ) from error
    if not 0.0 <= hard_negative_confidence <= 1.0:
        raise AssetVisualVerificationError(
            "visual verifier hard_negative_confidence must be between 0 and 1"
        )

    return obvious_mismatch, confidence, hard_negative, hard_negative_confidence


class OpenAIImageRelevanceVerifier:
    """Use an image-capable OpenAI model as a mismatch veto plus hard-negative gate."""

    REJECT_CONFIDENCE = 0.90
    HARD_NEGATIVE_CONFIDENCE = 0.75

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
            "You are a conservative mismatch detector for factual short-form video stock imagery. "
            "The search/ranking system already chose this candidate. Your job is to identify obvious visual disasters "
            "without demanding proof that an ambiguous stock image is exactly the named subject. Treat filenames, tags, "
            "and stock metadata as hints, never as proof.\n\n"
            f"Scene search query: {scene_query}\n"
            f"Stock metadata title: {candidate_title}\n\n"
            "Return two independent judgments. First, obvious_mismatch says whether the whole image is clearly wrong for "
            "the scene. Second, hard_negative identifies a forbidden visible subject that is NOT requested by the scene.\n"
            "For hard_negative choose exactly one category. Use none when no forbidden subject is clearly visible. Categories:\n"
            "- unrequested_earth: recognizable Earth when Earth is not requested.\n"
            "- unrequested_dragon_or_fantasy_creature: dragon, monster, fantasy beast, or similar creature when not requested.\n"
            "- unrequested_person: a prominent unrelated person when people are not requested.\n"
            "- unrequested_statue: statue or sculpture when not requested.\n"
            "- unrequested_animal: prominent unrelated animal when not requested.\n"
            "- unrequested_rocket_or_ufo: rocket, spacecraft, or UFO when not requested.\n"
            "- unrequested_logo_or_symbol: logo, emblem, icon, decorative symbol, or mostly symbolic graphic when not requested.\n"
            "- unrequested_generic_diagram: generic chart, schematic, armillary/mechanical astronomy diagram, or infographic when not requested.\n"
            "- other_obvious_unrelated_subject: another unmistakable dominant subject that contradicts the requested scene.\n"
            "Set a hard-negative category only when that visible subject is actually unrequested. If the scene explicitly asks "
            "for that thing, use none.\n\n"
            "For Venus and similarly hard-to-identify scientific subjects, yellow/orange/cloudy planets, crescent planetary disks, "
            "hot rocky surfaces, atmospheric views, and scientifically plausible illustrations are not mismatches merely because "
            "their identity cannot be proven from pixels. Do not reject because rotation, orbit, retrograde motion, or sunrise "
            "direction is not visibly demonstrated in a still image.\n"
            "If unsure about the overall match, set obvious_mismatch=false. But if a forbidden subject such as a dragon is visibly "
            "present and not requested, report the corresponding hard_negative even if you are otherwise unsure about relevance."
        )

        body = {
            "model": self.model,
            "max_output_tokens": 800,
            "reasoning": {"effort": "minimal"},
            "text": {
                "verbosity": "low",
                "format": {
                    "type": "json_schema",
                    "name": "visual_mismatch_decision",
                    "description": "Visual mismatch decision with an independent hard-negative category.",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "obvious_mismatch": {"type": "boolean"},
                            "confidence": {
                                "type": "number",
                                "minimum": 0,
                                "maximum": 1,
                            },
                            "hard_negative": {
                                "type": "string",
                                "enum": sorted(_HARD_NEGATIVES),
                            },
                            "hard_negative_confidence": {
                                "type": "number",
                                "minimum": 0,
                                "maximum": 1,
                            },
                        },
                        "required": [
                            "obvious_mismatch",
                            "confidence",
                            "hard_negative",
                            "hard_negative_confidence",
                        ],
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
        (
            obvious_mismatch,
            confidence,
            hard_negative,
            hard_negative_confidence,
        ) = _parse_mismatch(_response_text(payload))

        if hard_negative != "none" and hard_negative_confidence >= self.HARD_NEGATIVE_CONFIDENCE:
            return False
        if obvious_mismatch and confidence >= self.REJECT_CONFIDENCE:
            return False
        return True


__all__ = [
    "AssetVisualVerificationError",
    "OpenAIImageRelevanceVerifier",
]
