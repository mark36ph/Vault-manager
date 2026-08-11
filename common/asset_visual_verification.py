"""Visual relevance verification for downloaded production images."""
from __future__ import annotations

import base64
import json
import mimetypes
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Callable, Mapping

from common.asset_acquisition import AcquiredAsset

JsonTransport = Callable[[urllib.request.Request], Mapping[str, Any]]


class AssetVisualVerificationError(RuntimeError):
    """Raised when the visual verification service returns an unusable response."""


def _http_error_message(error: urllib.error.HTTPError) -> str:
    """Return the useful OpenAI error body instead of only ``HTTP Error 400``."""
    try:
        raw = error.read().decode("utf-8", errors="replace").strip()
    except Exception:
        raw = ""
    if not raw:
        return str(error.reason or error)
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return raw[:500]
    if isinstance(payload, Mapping):
        detail = payload.get("error")
        if isinstance(detail, Mapping):
            message = str(detail.get("message") or "").strip()
            if message:
                return message
    return raw[:500]


def _json_transport(request: urllib.request.Request) -> Mapping[str, Any]:
    try:
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        raise AssetVisualVerificationError(
            f"OpenAI visual verifier HTTP {error.code}: {_http_error_message(error)}"
        ) from error
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


def _image_mime_type(path: Path, data: bytes) -> str:
    """Prefer actual image bytes so a mislabeled AVIF/HEIC is not sent as JPEG."""
    if data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    if data.startswith((b"GIF87a", b"GIF89a")):
        return "image/gif"
    if len(data) >= 12 and data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp"
    if len(data) >= 12 and data[4:8] == b"ftyp":
        brand = data[8:12].lower()
        if brand in {b"avif", b"avis", b"heic", b"heix", b"hevc", b"hevx", b"mif1", b"msf1"}:
            raise AssetVisualVerificationError(
                f"unsupported downloaded image format for OpenAI vision: {brand.decode('ascii', errors='replace')}"
            )
    mime_type = mimetypes.guess_type(path.name)[0] or "image/jpeg"
    if mime_type not in {"image/jpeg", "image/png", "image/gif", "image/webp"}:
        raise AssetVisualVerificationError(
            f"unsupported visual-verification file type: {mime_type}"
        )
    return mime_type


def _image_data_url(path: Path) -> str:
    data = path.read_bytes()
    mime_type = _image_mime_type(path, data)
    encoded = base64.b64encode(data).decode("ascii")
    return f"data:{mime_type};base64,{encoded}"


_HARD_NEGATIVES = {
    "none",
    "wrong_named_subject",
    "unrequested_fantasy_creature",
    "unrequested_person",
    "unrequested_statue_or_sculpture",
    "unrequested_animal",
    "unrequested_vehicle_or_spacecraft",
    "unrequested_logo_or_symbol",
    "unrequested_generic_diagram",
    "other_obvious_unrelated_subject",
}

_VISUAL_QUALITY = {"preferred", "acceptable", "weak"}
_VISUAL_STYLE = {"literal", "representational", "decorative"}


def _parse_mismatch(text: str) -> tuple[bool, float, bool, float, str, float, str, str]:
    """Parse mismatch, physical contradiction, hard-negative, quality, and style."""
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

    physical_contradiction = payload.get("physical_contradiction")
    if not isinstance(physical_contradiction, bool):
        raise AssetVisualVerificationError("visual verifier physical_contradiction must be boolean")

    try:
        physical_contradiction_confidence = float(payload.get("physical_contradiction_confidence"))
    except (TypeError, ValueError) as error:
        raise AssetVisualVerificationError(
            "visual verifier physical_contradiction_confidence must be numeric"
        ) from error
    if not 0.0 <= physical_contradiction_confidence <= 1.0:
        raise AssetVisualVerificationError(
            "visual verifier physical_contradiction_confidence must be between 0 and 1"
        )

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

    visual_quality = str(payload.get("visual_quality") or "").strip()
    if visual_quality not in _VISUAL_QUALITY:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid visual_quality: {visual_quality or '<empty>'}"
        )

    visual_style = str(payload.get("visual_style") or "").strip()
    if visual_style not in _VISUAL_STYLE:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid visual_style: {visual_style or '<empty>'}"
        )

    return (
        obvious_mismatch,
        confidence,
        physical_contradiction,
        physical_contradiction_confidence,
        hard_negative,
        hard_negative_confidence,
        visual_quality,
        visual_style,
    )


class OpenAIImageRelevanceVerifier:
    """Use an image-capable OpenAI model as a topic-neutral mismatch and quality gate."""

    REJECT_CONFIDENCE = 0.90
    PHYSICAL_CONTRADICTION_CONFIDENCE = 0.90
    HARD_NEGATIVE_CONFIDENCE = 0.85
    SOFT_FORMAT_CONFIDENCE = 0.97

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
        self.last_decision = "not checked"
        self.last_quality = "preferred"
        self.last_style = "literal"
        if not self.api_key:
            raise ValueError("OpenAI API key is required for visual verification")
        if not self.model:
            raise ValueError("visual verification model is required")

    def __call__(self, query: str, asset: AcquiredAsset) -> bool:
        path = Path(asset.path)
        self.last_decision = "not checked"
        self.last_quality = "preferred"
        self.last_style = "literal"
        if asset.candidate.kind != "image":
            self.last_decision = "non-image asset"
            return True
        if not path.is_file() or path.stat().st_size <= 0:
            self.last_decision = "missing or empty image"
            return False

        scene_query = " ".join(str(query or "").split()).strip()
        candidate_title = " ".join(str(asset.candidate.title or "").split()).strip()
        instruction = (
            "You are a topic-neutral visual mismatch and factual-quality detector for short-form factual video stock imagery. "
            "This must work for any subject: science, history, geography, animals, technology, people, places, objects, "
            "transport, architecture, medicine, nature, or other factual topics. The scene search query is the source of truth. "
            "Never assume a fixed video topic.\n\n"
            f"Scene search query: {scene_query}\n"
            f"Stock metadata title: {candidate_title}\n\n"
            "The search/ranking system already chose this candidate. Do not demand impossible visual proof. Veto clear contradictions "
            "and unrelated dominant subjects, while separately rating factual usefulness and visual style. Treat filenames, tags, and "
            "stock metadata as hints, never as proof.\n\n"
            "Return five judgments: obvious_mismatch, physical_contradiction, hard_negative, visual_quality, and visual_style.\n"
            "physical_contradiction is specifically about visible defining features that conflict with a concrete named or typed subject in the query. "
            "Set it true only when the image provides enough visual evidence to distinguish the requested subject and one or more defining visible traits contradict it. "
            "Examples across domains: a smooth gas giant shown for a rocky cratered planet; a suspension bridge shown for a stone arch bridge; a tiger's stripes shown for a lion; "
            "a propeller biplane shown for a modern jet; a Gothic cathedral shown for a glass skyscraper; a wheeled vehicle shown for a tracked tank. "
            "Do not set physical_contradiction merely because an image is generic, incomplete, stylized, reconstructed, or because a fact/action is not directly visible.\n\n"
            "For hard_negative choose exactly one category. Use none when no forbidden subject is clearly visible. Categories:\n"
            "- wrong_named_subject: the query names a concrete entity or class and the image visibly shows a different identifiable one. "
            "Examples include the wrong planet, landmark, person, animal species, vehicle, building, machine, food, flag, location, or object.\n"
            "- unrequested_fantasy_creature: dragon, monster, mythical beast, or fantasy creature when not requested.\n"
            "- unrequested_person: a prominent person when people are not requested by the scene.\n"
            "- unrequested_statue_or_sculpture: statue, bust, monument sculpture, or artwork standing in for a real subject when not requested.\n"
            "- unrequested_animal: a prominent animal when animals are not requested by the scene.\n"
            "- unrequested_vehicle_or_spacecraft: car, aircraft, ship, train, rocket, spacecraft, or UFO when not requested.\n"
            "- unrequested_logo_or_symbol: logo, emblem, icon, decorative symbol, or mostly symbolic graphic when a literal visual is expected.\n"
            "- unrequested_generic_diagram: generic chart, schematic, infographic, mechanical model, or diagram when the scene does not request one.\n"
            "- other_obvious_unrelated_subject: another unmistakable dominant subject that contradicts the requested scene.\n"
            "Set a hard-negative category only when that subject is inconsistent with or unrequested by the scene. If the query asks for it, use none.\n\n"
            "Rate visual_quality independently from relevance:\n"
            "- preferred: clear, compelling, useful visual that directly supports the factual scene.\n"
            "- acceptable: relevant and usable, but less direct, less clear, or less visually strong.\n"
            "- weak: relevant enough to keep only as a fallback, such as placeholder-like, generic, cluttered, or low-information imagery.\n\n"
            "Rate visual_style independently from quality:\n"
            "- literal: photo, documentary image, scientific observation, real object/place/person/animal, or realistic direct depiction of the requested subject.\n"
            "- representational: useful reconstruction, archival artwork, map, diagram specifically requested by the scene, scientific illustration, microscopic rendering, or other explanatory representation.\n"
            "- decorative: logo-like composition, generic icons/symbols, unrelated infographic styling, fantasy/concept-art treatment, ornamental graphic, or aesthetically themed image that does not directly depict the factual subject.\n"
            "If a literal visual is realistically possible and the candidate is mostly symbolic, logo-like, generic diagrammatic, or concept-art decoration, use decorative even if it is loosely relevant. "
            "If a diagram, map, chart, artwork, or symbolic representation is explicitly requested by the query, it may be representational instead.\n\n"
            "Apply these general rules across all topics:\n"
            "- For a concrete named or typed subject, actively compare visible defining traits against the query before deciding the image is acceptable. Broad category similarity alone is not enough when the visual clearly identifies a conflicting subject.\n"
            "- If a named subject is difficult or impossible to uniquely identify from pixels alone, keep a scientifically, historically, or physically plausible representation unless a visible feature clearly contradicts the query.\n"
            "- A still image does not have to demonstrate an abstract action, duration, comparison, cause, motion, direction, measurement, or process when the underlying subject is appropriate.\n"
            "- Reject a clearly different identifiable named subject. Examples: Big Ben is not the Eiffel Tower; a tiger is not a lion; a motorcycle is not a bicycle; Earth is not Mars; a modern jet is not a World War I biplane.\n"
            "- Do not reject merely because the image is a reasonable reconstruction, microscopic view, astronomical view, ancient-event depiction, or other representation that cannot be verified uniquely from pixels.\n"
            "- If unsure about the overall match, set obvious_mismatch=false and physical_contradiction=false. But still classify quality and style based on what is visibly present."
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
                    "description": "Topic-neutral mismatch, physical contradiction, hard-negative, quality, and factual-style classification.",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "obvious_mismatch": {"type": "boolean"},
                            "confidence": {"type": "number", "minimum": 0, "maximum": 1},
                            "physical_contradiction": {"type": "boolean"},
                            "physical_contradiction_confidence": {"type": "number", "minimum": 0, "maximum": 1},
                            "hard_negative": {"type": "string", "enum": sorted(_HARD_NEGATIVES)},
                            "hard_negative_confidence": {"type": "number", "minimum": 0, "maximum": 1},
                            "visual_quality": {"type": "string", "enum": sorted(_VISUAL_QUALITY)},
                            "visual_style": {"type": "string", "enum": sorted(_VISUAL_STYLE)},
                        },
                        "required": [
                            "obvious_mismatch",
                            "confidence",
                            "physical_contradiction",
                            "physical_contradiction_confidence",
                            "hard_negative",
                            "hard_negative_confidence",
                            "visual_quality",
                            "visual_style",
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
                        {"type": "input_image", "image_url": _image_data_url(path), "detail": "high"},
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
            physical_contradiction,
            physical_contradiction_confidence,
            hard_negative,
            hard_negative_confidence,
            visual_quality,
            visual_style,
        ) = _parse_mismatch(_response_text(payload))
        self.last_quality = visual_quality
        self.last_style = visual_style

        if (
            physical_contradiction
            and physical_contradiction_confidence >= self.PHYSICAL_CONTRADICTION_CONFIDENCE
        ):
            self.last_decision = (
                "physical contradiction "
                f"({physical_contradiction_confidence:.2f}, threshold {self.PHYSICAL_CONTRADICTION_CONFIDENCE:.2f})"
            )
            return False

        threshold = self.HARD_NEGATIVE_CONFIDENCE
        if hard_negative in {"unrequested_logo_or_symbol", "unrequested_generic_diagram"}:
            threshold = self.SOFT_FORMAT_CONFIDENCE

        if hard_negative != "none" and hard_negative_confidence >= threshold:
            self.last_decision = (
                f"hard negative {hard_negative} ({hard_negative_confidence:.2f}, threshold {threshold:.2f})"
            )
            return False
        if obvious_mismatch and confidence >= self.REJECT_CONFIDENCE:
            self.last_decision = (
                f"obvious mismatch ({confidence:.2f}, threshold {self.REJECT_CONFIDENCE:.2f})"
            )
            return False

        self.last_decision = (
            f"kept: mismatch={obvious_mismatch}/{confidence:.2f}, "
            f"physical_contradiction={physical_contradiction}/{physical_contradiction_confidence:.2f}, "
            f"hard_negative={hard_negative}/{hard_negative_confidence:.2f}, "
            f"quality={visual_quality}, style={visual_style}"
        )
        return True


__all__ = [
    "AssetVisualVerificationError",
    "OpenAIImageRelevanceVerifier",
]
