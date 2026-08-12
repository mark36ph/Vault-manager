"""Concrete provider adapters for OpenAI, Pexels, and Pixabay.

The adapters use only the Python standard library and accept injectable HTTP
transports so production code can use real services while tests stay offline.
"""
from __future__ import annotations

import json
import os
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence
import urllib.error
from common.asset_acquisition import AssetCandidate
import hashlib
import re

JsonTransport = Callable[[urllib.request.Request], Mapping[str, Any]]
BytesTransport = Callable[[urllib.request.Request], bytes]


class ProviderIntegrationError(RuntimeError):
    """Raised when a configured external provider returns unusable data."""

def _json_transport(request: urllib.request.Request) -> Mapping[str, Any]:
    try:
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        body = ""
        try:
            body = error.read().decode("utf-8", errors="replace")
        except Exception:
            pass

        raise ProviderIntegrationError(
            f"HTTP {error.code}\n"
            f"URL: {request.full_url}\n"
            f"Response:\n{body}"
        ) from error
    except Exception as error:
        raise ProviderIntegrationError(str(error)) from error

    if not isinstance(payload, Mapping):
        raise ProviderIntegrationError("provider response must be a JSON object")

    return payload


def _bytes_transport(request: urllib.request.Request) -> bytes:
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            return response.read()
    except Exception as error:
        raise ProviderIntegrationError(str(error)) from error


def _required(value: str | None, name: str) -> str:
    text = str(value or "").strip()
    if not text:
        raise ValueError(f"{name} is required")
    return text


def _json_request(
    url: str,
    *,
    headers: Mapping[str, str] | None = None,
    body: Mapping[str, Any] | None = None,
) -> urllib.request.Request:
    data = None if body is None else json.dumps(body).encode("utf-8")

    merged = {
        "Accept": "application/json",
        "User-Agent": "FactVaultManager/1.0 (+desktop media downloader)",
        **dict(headers or {}),
    }

    if data is not None:
        merged["Content-Type"] = "application/json"

    return urllib.request.Request(url, data=data, headers=merged)

_STOP_WORDS = {
    "a",
    "an",
    "and",
    "the",
    "of",
    "for",
    "to",
    "in",
    "on",
    "with",
    "from",
    "by",
    "at",
    "close",
    "up",
    "photo",
    "photography",
    "portrait",
    "vertical",
    "realistic",
}


def _search_keywords(value: str) -> list[str]:
    words = re.findall(
        r"[a-zA-Z0-9]+",
        str(value or "").lower(),
    )

    result: list[str] = []

    for word in words:
        if len(word) < 3:
            continue

        if word in _STOP_WORDS:
            continue

        if word not in result:
            result.append(word)

    return result


def _candidate_is_relevant(
    query: str,
    candidate_text: str,
) -> bool:
    query_words = _search_keywords(query)
    candidate_words = set(
        _search_keywords(candidate_text)
    )

    if not query_words:
        return True

    query_word_set = set(query_words)
    overlap = query_word_set & candidate_words

    if len(overlap) < 1:
        return False

    anchor_words = set(query_words[:2])

    if anchor_words and not (
        anchor_words & candidate_words
    ):
        return False

    return True
    
class PexelsAssetProvider:
    name = "pexels"

    def __init__(self, api_key: str, *, transport: JsonTransport = _json_transport) -> None:
        self.api_key = _required(api_key, "Pexels API key")
        self.transport = transport

    def search(self, query: str, *, kind: str, limit: int) -> Sequence[AssetCandidate]:
        query = _required(query, "query")
        if kind not in {"image", "video"}:
            return []
        endpoint = "https://api.pexels.com/v1/search" if kind == "image" else "https://api.pexels.com/v1/videos/search"
        params = urllib.parse.urlencode({"query": query, "per_page": max(1, min(int(limit), 80)), "orientation": "portrait"})
        payload = self.transport(_json_request(f"{endpoint}?{params}", headers={"Authorization": self.api_key}))
        items = payload.get("photos" if kind == "image" else "videos", [])
        results: list[AssetCandidate] = []
        for item in items if isinstance(items, list) else []:
            if not isinstance(item, Mapping):
                continue
            candidate_text = ""
            if kind == "image":
                sources = item.get("src") if isinstance(item.get("src"), Mapping) else {}
                media_url = str(sources.get("portrait") or sources.get("large2x") or sources.get("original") or "")
                width, height, duration = int(item.get("width") or 0), int(item.get("height") or 0), 0.0
                credit = str(item.get("photographer") or "")
                candidate_text = " ".join(
                    [
                        str(item.get("alt") or ""),
                        str(item.get("url") or ""),
                    ]
                )
            else:
                files = [entry for entry in item.get("video_files", []) if isinstance(entry, Mapping) and entry.get("link")]
                files.sort(key=lambda entry: int(entry.get("width") or 0) * int(entry.get("height") or 0), reverse=True)
                selected = files[0] if files else {}
                media_url = str(selected.get("link") or "")
                width, height = int(selected.get("width") or item.get("width") or 0), int(selected.get("height") or item.get("height") or 0)
                duration = float(item.get("duration") or 0)
                user = item.get("user") if isinstance(item.get("user"), Mapping) else {}
                credit = str(user.get("name") or "")
                candidate_text = " ".join(
                    [
                        str(item.get("alt") or ""),
                        str(item.get("url") or ""),
                    ]
                )

            if not _candidate_is_relevant(query, candidate_text):
                continue

            if media_url:
                results.append(AssetCandidate(provider=self.name, id=str(item.get("id") or media_url), url=media_url, kind=kind, title=str(item.get("alt") or item.get("url") or ""), width=width, height=height, duration=duration, score=float(item.get("liked") or 0), credit=credit, license="Pexels License", metadata={"source_page": str(item.get("url") or "")}))
        return results


class PixabayAssetProvider:
    name = "pixabay"

    def __init__(self, api_key: str, *, transport: JsonTransport = _json_transport) -> None:
        self.api_key = _required(api_key, "Pixabay API key")
        self.transport = transport

    def search(self, query: str, *, kind: str, limit: int) -> Sequence[AssetCandidate]:
        query = _required(query, "query")
        if kind not in {"image", "video"}:
            return []
        endpoint = "https://pixabay.com/api/" if kind == "image" else "https://pixabay.com/api/videos/"
        params = urllib.parse.urlencode({"key": self.api_key, "q": query[:100], "per_page": max(3, min(int(limit), 200)), "safesearch": "true", "orientation": "vertical"})
        payload = self.transport(_json_request(f"{endpoint}?{params}"))
        results: list[AssetCandidate] = []
        for item in payload.get("hits", []) if isinstance(
            payload.get("hits"), list
        ) else []:
            if not isinstance(item, Mapping):
                continue

            if kind == "image":
                media_url = str(
                    item.get("largeImageURL")
                    or item.get("webformatURL")
                    or ""
                )

                width = int(
                    item.get("imageWidth")
                    or item.get("webformatWidth")
                    or 0
                )

                height = int(
                    item.get("imageHeight")
                    or item.get("webformatHeight")
                    or 0
                )

                duration = 0.0

            else:
                versions = (
                    item.get("videos")
                    if isinstance(item.get("videos"), Mapping)
                    else {}
                )

                choices = [
                    entry
                    for entry in versions.values()
                    if isinstance(entry, Mapping)
                    and entry.get("url")
                ]

                choices.sort(
                    key=lambda entry: (
                        int(entry.get("width") or 0)
                        * int(entry.get("height") or 0)
                    ),
                    reverse=True,
                )

                selected = choices[0] if choices else {}

                media_url = str(selected.get("url") or "")
                width = int(selected.get("width") or 0)
                height = int(selected.get("height") or 0)
                duration = float(item.get("duration") or 0)

            candidate_text = " ".join(
                [
                    str(item.get("tags") or ""),
                    str(item.get("pageURL") or ""),
                ]
            )

            if not _candidate_is_relevant(
                query,
                candidate_text,
            ):
                continue

            if media_url:
                results.append(
                    AssetCandidate(
                        provider=self.name,
                        id=str(item.get("id") or media_url),
                        url=media_url,
                        kind=kind,
                        title=str(item.get("tags") or query),
                        width=width,
                        height=height,
                        duration=duration,
                        score=(
                            float(item.get("likes") or 0)
                            + float(item.get("downloads") or 0)
                            / 1000.0
                        ),
                        credit=str(item.get("user") or ""),
                        license="Pixabay Content License",
                        metadata={
                            "source_page": str(
                                item.get("pageURL") or ""
                            )
                        },
                    )
                )

        return results


class OpenAITextProvider:
    """Callable content-production provider backed by the Responses API."""

    def __init__(self, api_key: str, *, instructions: str, prompt_builder: Callable[[Any], str], model: str = "gpt-5-mini", transport: JsonTransport = _json_transport) -> None:
        self.api_key = _required(api_key, "OpenAI API key")
        self.instructions = _required(instructions, "instructions")
        self.prompt_builder = prompt_builder
        self.model = _required(model, "model")
        self.transport = transport

    def __call__(self, context: Any) -> str:
        prompt = _required(self.prompt_builder(context), "provider prompt")
        request = _json_request("https://api.openai.com/v1/responses", headers={"Authorization": f"Bearer {self.api_key}"}, body={"model": self.model, "instructions": self.instructions, "input": prompt})
        payload = self.transport(request)
        text = payload.get("output_text")
        if not text:
            chunks: list[str] = []
            for output in payload.get("output", []) if isinstance(payload.get("output"), list) else []:
                for content in output.get("content", []) if isinstance(output, Mapping) else []:
                    if isinstance(content, Mapping) and content.get("text"):
                        chunks.append(str(content["text"]))
            text = "\n".join(chunks)
        if not str(text or "").strip():
            raise ProviderIntegrationError("OpenAI response did not contain text")
        return str(text).strip()


class OpenAISpeechProvider:
    """Callable voice provider that writes narration audio into the project."""

    def __init__(self, api_key: str, *, model: str = "gpt-4o-mini-tts", voice: str = "alloy", response_format: str = "mp3", transport: BytesTransport = _bytes_transport) -> None:
        self.api_key = _required(api_key, "OpenAI API key")
        self.model = model
        self.voice = voice
        self.response_format = response_format
        self.transport = transport

    def __call__(self, context: Any) -> str:
        script = _required(getattr(context, "script", ""), "script")
        suffix = "." + self.response_format.lstrip(".")

        script_hash = hashlib.sha256(
            script.encode("utf-8")
        ).hexdigest()[:12]

        voice_folder = Path(context.project_folder) / "Voice"
        voice_folder.mkdir(parents=True, exist_ok=True)

        destination = voice_folder / f"narration_{script_hash}{suffix}"

        script_copy = voice_folder / f"narration_{script_hash}.txt"
        script_copy.write_text(script, encoding="utf-8")
        request = _json_request("https://api.openai.com/v1/audio/speech", headers={"Authorization": f"Bearer {self.api_key}"}, body={"model": self.model, "voice": self.voice, "input": script, "response_format": self.response_format})
        data = self.transport(request)
        if not data:
            raise ProviderIntegrationError("OpenAI speech response was empty")
        temporary = destination.with_suffix(destination.suffix + ".part")
        temporary.write_bytes(data)
        temporary.replace(destination)

        outro_destination = voice_folder / "fact_unlocked.mp3"

        outro_request = _json_request(
            "https://api.openai.com/v1/audio/speech",
            headers={
                "Authorization": f"Bearer {self.api_key}",
            },
            body={
                "model": self.model,
                "voice": self.voice,
                "input": "Fact unlocked. Follow for more",
                "response_format": "mp3",
            },
        )

        outro_data = self.transport(outro_request)

        if not outro_data:
            raise ProviderIntegrationError(
                "OpenAI outro speech response was empty"
            )

        outro_temporary = outro_destination.with_suffix(".mp3.part")
        outro_temporary.write_bytes(outro_data)
        outro_temporary.replace(outro_destination)

        return str(destination)


@dataclass(frozen=True)
class ProviderEnvironment:
    openai_api_key: str = ""
    pexels_api_key: str = ""
    pixabay_api_key: str = ""

    @classmethod
    def from_env(cls, env: Mapping[str, str] | None = None) -> "ProviderEnvironment":
        values = os.environ if env is None else env
        return cls(openai_api_key=str(values.get("OPENAI_API_KEY", "")), pexels_api_key=str(values.get("PEXELS_API_KEY", "")), pixabay_api_key=str(values.get("PIXABAY_API_KEY", "")))


__all__ = ["OpenAISpeechProvider", "OpenAITextProvider", "PexelsAssetProvider", "PixabayAssetProvider", "ProviderEnvironment", "ProviderIntegrationError"]
