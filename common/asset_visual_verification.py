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
        if brand in {
            b"avif", b"avis", b"heic", b"heix", b"hevc", b"hevx", b"mif1", b"msf1"
        }:
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


def _required_bool(payload: Mapping[str, Any], key: str) -> bool:
    value = payload.get(key)
    if not isinstance(value, bool):
        raise AssetVisualVerificationError(f"visual verifier {key} must be boolean")
    return value


def _required_confidence(payload: Mapping[str, Any], key: str) -> float:
    try:
        value = float(payload.get(key))
    except (TypeError, ValueError) as error:
        raise AssetVisualVerificationError(f"visual verifier {key} must be numeric") from error
    if not 0.0 <= value <= 1.0:
        raise AssetVisualVerificationError(f"visual verifier {key} must be between 0 and 1")
    return value


def _parse_mismatch(
    text: str,
) -> tuple[bool, float, bool, float, str, float, str, str, bool, bool, bool, float]:
    """Parse mismatch, quality, and mandatory pixel-level subject judgments."""
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

    obvious_mismatch = _required_bool(payload, "obvious_mismatch")
    confidence = _required_confidence(payload, "confidence")
    physical_contradiction = _required_bool(payload, "physical_contradiction")
    physical_contradiction_confidence = _required_confidence(
        payload, "physical_contradiction_confidence"
    )

    hard_negative = str(payload.get("hard_negative") or "").strip()
    if hard_negative not in _HARD_NEGATIVES:
        raise AssetVisualVerificationError(
            f"visual verifier returned invalid hard_negative: {hard_negative or '<empty>'}"
        )
    hard_negative_confidence = _required_confidence(payload, "hard_negative_confidence")

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

    requested_subject_visible = _required_bool(payload, "requested_subject_visible")
    requested_scene_evidence_visible = _required_bool(
        payload, "requested_scene_evidence_visible"
    )
    explicit_subject_contradiction = _required_bool(
        payload, "explicit_subject_contradiction"
    )
    explicit_subject_confidence = _required_confidence(
        payload, "explicit_subject_confidence"
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
        requested_subject_visible,
        requested_scene_evidence_visible,
        explicit_subject_contradiction,
        explicit_subject_confidence,
    )


class OpenAIImageRelevanceVerifier:
    """Use an image-capable OpenAI model as a topic-neutral mismatch and quality gate."""

    REJECT_CONFIDENCE = 0.90
    PHYSICAL_CONTRADICTION_CONFIDENCE = 0.82
    SUBJECT_UNCERTAIN_CONFIDENCE = 0.45
    HARD_NEGATIVE_CONFIDENCE = 0.85
    WRONG_NAMED_SUBJECT_CONFIDENCE = 0.72
    DECORATIVE_PERSON_CONFIDENCE = 0.70
    SOFT_FORMAT_CONFIDENCE = 0.97
    EXPLICIT_SUBJECT_CONTRADICTION_CONFIDENCE = 0.55

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
        self.last_subject_uncertain = False
        self.last_requested_subject_visible = False
        self.last_requested_scene_evidence_visible = False
        self.last_explicit_subject_contradiction = False
        self.last_explicit_subject_confidence = 0.0
        if not self.api_key:
            raise ValueError("OpenAI API key is required for visual verification")
        if not self.model:
            raise ValueError("visual verification model is required")

    def __call__(self, query: str, asset: AcquiredAsset) -> bool:
        path = Path(asset.path)
        raw_query = str(query or "")
        explicit_subject_gate = "EXPLICIT-SUBJECT VISUAL REQUIREMENT:" in raw_query
        self.last_decision = "not checked"
        self.last_quality = "preferred"
        self.last_style = "literal"
        self.last_subject_uncertain = False
        self.last_requested_subject_visible = False
        self.last_requested_scene_evidence_visible = False
        self.last_explicit_subject_contradiction = False
        self.last_explicit_subject_confidence = 0.0

        if asset.candidate.kind != "image":
            self.last_decision = "non-image asset"
            return True
        if not path.is_file() or path.stat().st_size <= 0:
            self.last_decision = "missing or empty image"
            return False

        scene_query = " ".join(raw_query.split()).strip()
        candidate_title = " ".join(str(asset.candidate.title or "").split()).strip()
        instruction = (
            "You are a topic-neutral visual mismatch and factual-quality detector for short-form factual video stock imagery. "
            "This must work for any subject: science, history, geography, animals, technology, people, places, objects, "
            "transport, architecture, medicine, nature, or other factual topics. The scene search query is the source of truth. "
            "Never assume a fixed video topic.\n\n"
            f"Scene search query: {scene_query}\n"
            f"Stock metadata title: {candidate_title}\n\n"
            "The search/ranking system already chose this candidate. Treat filenames, tags, URLs, search terms, and stock metadata as hints, never as proof. "
            "Judge visible pixels first. Veto clear contradictions and unrelated dominant subjects while separately rating factual usefulness and visual style.\n\n"
            "Return the normal mismatch/quality judgments plus FOUR explicit pixel-level subject judgments on every request:\n"
            "- requested_subject_visible: true only when the requested concrete subject itself is visibly present or, for a named subject that cannot be uniquely proven from pixels, the visible object/place is genuinely plausible for that requested subject. Do not use metadata to make this true.\n"
            "- requested_scene_evidence_visible: true only when the visible image directly shows distinctive scene-specific evidence requested by the query, such as a product, trace, result, body part, habitat detail, material, or other concrete derivative that can legitimately satisfy the scene even when the anchor subject is outside the frame. Generic scenery or thematic similarity is not scene-specific evidence.\n"
            "- explicit_subject_contradiction: true when the visible dominant content is unrelated to or incompatible with the requested concrete subject/scene, especially when neither the requested subject nor credible requested scene-specific evidence is visible.\n"
            "- explicit_subject_confidence: confidence from 0 to 1 in that explicit subject contradiction judgment. If explicit_subject_contradiction is false, give confidence in the absence of a contradiction.\n\n"
            "When the scene query contains an EXPLICIT-SUBJECT VISUAL REQUIREMENT, be strict: if neither requested_subject_visible nor requested_scene_evidence_visible is true, the candidate must not be treated as a safe subject match. Ancient ruins cannot satisfy an animal query merely because metadata contains the animal name.\n\n"
            "Before judging the match, infer the intended meaning and semantic class of the requested subject from the FULL scene query, especially category/type words such as planet, animal, river, company, vehicle, person, landmark, plant, machine, or place. "
            "A shared name or keyword is not evidence that two subjects are the same entity. If the candidate visibly belongs to a different meaning of the same word, use wrong_named_subject with high confidence. General examples: a Venus flytrap for the planet Venus; a Jaguar car for a jaguar animal; the Amazon company/logo for the Amazon River; a Mercury-branded vehicle for the planet Mercury.\n\n"
            "physical_contradiction is specifically about visible defining features that conflict with a concrete named or typed subject in the query. Actively compare defining visible traits rather than accepting broad category similarity. Examples: a tiger's stripes for a lion; a propeller biplane for a modern jet; a Gothic cathedral for a glass skyscraper; a wheeled vehicle for a tracked tank. Do not set physical_contradiction merely because an image is generic, incomplete, stylized, reconstructed, or because an abstract fact/action is not directly visible.\n\n"
            "For hard_negative choose exactly one category. Use none when no forbidden subject is clearly visible. Categories:\n"
            "- wrong_named_subject: a different identifiable entity/class or different semantic meaning.\n"
            "- unrequested_fantasy_creature: dragon, monster, mythical beast, or fantasy creature when not requested.\n"
            "- unrequested_person: a prominent human figure when people are not requested.\n"
            "- unrequested_statue_or_sculpture: statue, bust, monument sculpture, or artwork standing in for a real subject when not requested.\n"
            "- unrequested_animal: a prominent animal when animals are not requested.\n"
            "- unrequested_vehicle_or_spacecraft: vehicle, aircraft, ship, train, rocket, spacecraft, or UFO when not requested.\n"
            "- unrequested_logo_or_symbol: logo, emblem, icon, decorative symbol, or mostly symbolic graphic when a literal visual is expected.\n"
            "- unrequested_generic_diagram: generic chart, schematic, infographic, mechanical model, or diagram when the scene does not request one.\n"
            "- other_obvious_unrelated_subject: another unmistakable dominant subject that contradicts the requested scene.\n\n"
            "Rate visual_quality independently from relevance: preferred, acceptable, or weak.\n"
            "Rate visual_style independently: literal, representational, or decorative.\n\n"
            "Apply these general rules across all topics:\n"
            "- For a concrete named or typed subject, actively compare visible defining traits and semantic class against the full query before deciding the image is acceptable. Broad lexical similarity alone is not enough.\n"
            "- If a named subject is difficult or impossible to uniquely identify from pixels alone, keep a scientifically, historically, or physically plausible representation unless a visible feature clearly contradicts the query.\n"
            "- A still image does not have to demonstrate an abstract action, duration, comparison, cause, motion, direction, measurement, or process when the underlying subject is appropriate.\n"
            "- Reject a clearly different identifiable named subject. Examples: Big Ben is not the Eiffel Tower; a tiger is not a lion; a motorcycle is not a bicycle; Earth is not Mars; a modern jet is not a World War I biplane.\n"
            "- visual_quality describes usefulness; visual_style describes literal/representational/decorative treatment."
        )

        required_fields = [
            "obvious_mismatch",
            "confidence",
            "physical_contradiction",
            "physical_contradiction_confidence",
            "hard_negative",
            "hard_negative_confidence",
            "visual_quality",
            "visual_style",
            "requested_subject_visible",
            "requested_scene_evidence_visible",
            "explicit_subject_contradiction",
            "explicit_subject_confidence",
        ]
        body = {
            "model": self.model,
            "max_output_tokens": 800,
            "reasoning": {"effort": "minimal"},
            "text": {
                "verbosity": "low",
                "format": {
                    "type": "json_schema",
                    "name": "visual_mismatch_decision",
                    "description": "Topic-neutral mismatch, explicit visual subject, quality, and factual-style classification.",
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
                            "requested_subject_visible": {"type": "boolean"},
                            "requested_scene_evidence_visible": {"type": "boolean"},
                            "explicit_subject_contradiction": {"type": "boolean"},
                            "explicit_subject_confidence": {"type": "number", "minimum": 0, "maximum": 1},
                        },
                        "required": required_fields,
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
            requested_subject_visible,
            requested_scene_evidence_visible,
            explicit_subject_contradiction,
            explicit_subject_confidence,
        ) = _parse_mismatch(_response_text(payload))

        self.last_quality = visual_quality
        self.last_style = visual_style
        self.last_requested_subject_visible = requested_subject_visible
        self.last_requested_scene_evidence_visible = requested_scene_evidence_visible
        self.last_explicit_subject_contradiction = explicit_subject_contradiction
        self.last_explicit_subject_confidence = explicit_subject_confidence
        self.last_subject_uncertain = bool(
            physical_contradiction
            and self.SUBJECT_UNCERTAIN_CONFIDENCE
            <= physical_contradiction_confidence
            < self.PHYSICAL_CONTRADICTION_CONFIDENCE
        )

        if explicit_subject_gate:
            if not requested_subject_visible and not requested_scene_evidence_visible:
                self.last_decision = (
                    "explicit subject missing from pixels: "
                    f"subject_visible={requested_subject_visible}, "
                    f"scene_evidence_visible={requested_scene_evidence_visible}, "
                    f"contradiction={explicit_subject_contradiction}/{explicit_subject_confidence:.2f}"
                )
                return False
            if (
                explicit_subject_contradiction
                and explicit_subject_confidence >= self.EXPLICIT_SUBJECT_CONTRADICTION_CONFIDENCE
            ):
                self.last_decision = (
                    "explicit subject contradiction "
                    f"({explicit_subject_confidence:.2f}, threshold {self.EXPLICIT_SUBJECT_CONTRADICTION_CONFIDENCE:.2f})"
                )
                return False

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
        if hard_negative == "wrong_named_subject":
            threshold = self.WRONG_NAMED_SUBJECT_CONFIDENCE
        elif hard_negative == "unrequested_person" and visual_style == "decorative":
            threshold = self.DECORATIVE_PERSON_CONFIDENCE
        elif hard_negative in {"unrequested_logo_or_symbol", "unrequested_generic_diagram"}:
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

        uncertainty = ", subject_uncertain" if self.last_subject_uncertain else ""
        self.last_decision = (
            f"kept: mismatch={obvious_mismatch}/{confidence:.2f}, "
            f"physical_contradiction={physical_contradiction}/{physical_contradiction_confidence:.2f}, "
            f"hard_negative={hard_negative}/{hard_negative_confidence:.2f}, "
            f"subject_visible={requested_subject_visible}, "
            f"scene_evidence_visible={requested_scene_evidence_visible}, "
            f"explicit_contradiction={explicit_subject_contradiction}/{explicit_subject_confidence:.2f}, "
            f"quality={visual_quality}, style={visual_style}{uncertainty}"
        )
        return True


__all__ = [
    "AssetVisualVerificationError",
    "OpenAIImageRelevanceVerifier",
]
