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
    return "\n".join(chunks).strip()


def _image_data_url(path: Path) -> str:
    mime_type = mimetypes.guess_type(path.name)[0] or "image/jpeg"
    if not mime_type.startswith("image/"):
        raise AssetVisualVerificationError(f"unsupported visual-verification file type: {path.suffix}")
    encoded = base64.b64encode(path.read_bytes()).decode("ascii")
    return f"data:{mime_type};base64,{encoded}"


class OpenAIImageRelevanceVerifier:
    """Use an image-capable OpenAI model as a strict post-download relevance gate."""

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
            "Act as a strict visual relevance gate for factual short-form video stock imagery. "
            "Judge what is visibly present in the image, not just its filename or metadata. "
            "Return exactly ACCEPT or REJECT.\n\n"
            f"Scene search query: {scene_query}\n"
            f"Stock metadata title: {candidate_title}\n\n"
            "ACCEPT when the image visibly depicts the concrete subject in the query and is a reasonable "
            "visual for the scene. A generic but clearly correct view of the named subject is acceptable "
            "when the exact action is difficult to photograph. REJECT when the named subject is absent, "
            "when another subject is substituted, or when the image is merely category-related. For example, "
            "an Earth image, UFO, rocket, statue, animal, logo, or decorative illustration must be rejected "
            "for a Venus scene unless that object is explicitly requested by the scene query."
        )

        body = {
            "model": self.model,
            "max_output_tokens": 8,
            "input": [
                {
                    "role": "user",
                    "content": [
                        {"type": "input_text", "text": instruction},
                        {
                            "type": "input_image",
                            "image_url": _image_data_url(path),
                            "detail": "low",
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
        answer = _response_text(payload).strip().upper()
        if answer.startswith("ACCEPT"):
            return True
        if answer.startswith("REJECT"):
            return False
        raise AssetVisualVerificationError(
            f"visual verifier returned an unexpected response: {answer or '<empty>'}"
        )


__all__ = [
    "AssetVisualVerificationError",
    "OpenAIImageRelevanceVerifier",
]
