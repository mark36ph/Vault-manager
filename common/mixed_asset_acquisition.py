"""Verified mixed image/video acquisition for production scenes.

The existing provider layer already knows how to search and download both images
and videos.  This module adds a production-only policy on top of it:

* search both media kinds for every scene;
* verify still images directly with the existing visual verifier;
* sample several frames from videos and verify those frames with the same gate;
* prefer a strongly verified literal video, while keeping verified stills as a
  safe fallback;
* trim selected stock videos to a short useful section and burn the same
  watermark/on-screen caption treatment used by Resolve still renders.

The implementation is intentionally provider-neutral and topic-neutral.
"""
from __future__ import annotations

from dataclasses import replace
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
from typing import Any, Iterable, Mapping, Sequence

from common.asset_acquisition import (
    AcquiredAsset,
    AssetAcquisitionEngine,
    AssetAcquisitionError,
    AssetCandidate,
    _candidate_key,
)


QUALITY_SCORE = {"weak": 0, "acceptable": 3, "preferred": 6}
STYLE_SCORE = {"decorative": -10, "representational": 1, "literal": 2}
VIDEO_BONUS = 1
SUBJECT_UNCERTAIN_PENALTY = 4
VERIFY_PER_KIND = 3
VIDEO_SAMPLE_FRACTIONS = (0.20, 0.50, 0.80)


def _creationflags() -> int:
    return getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _media_duration(path: Path) -> float:
    ffprobe = shutil.which("ffprobe")
    if not ffprobe:
        return 0.0
    result = subprocess.run(
        [
            ffprobe,
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        capture_output=True,
        text=True,
        timeout=30,
        creationflags=_creationflags(),
    )
    if result.returncode != 0:
        return 0.0
    try:
        return max(0.0, float(result.stdout.strip()))
    except ValueError:
        return 0.0


def _verifier_state(verifier: Any) -> tuple[int, str, str, bool, str]:
    quality = str(getattr(verifier, "last_quality", "preferred") or "preferred").lower()
    style = str(getattr(verifier, "last_style", "literal") or "literal").lower()
    uncertain = bool(getattr(verifier, "last_subject_uncertain", False))
    decision = str(getattr(verifier, "last_decision", "") or "").strip()
    quality = quality if quality in QUALITY_SCORE else "preferred"
    style = style if style in STYLE_SCORE else "literal"
    score = QUALITY_SCORE[quality] + STYLE_SCORE[style]
    if uncertain:
        score -= SUBJECT_UNCERTAIN_PENALTY
    return score, quality, style, uncertain, decision


def _image_decision(verifier: Any, query: str, asset: AcquiredAsset) -> tuple[bool, int, dict[str, Any]]:
    accepted = bool(verifier(query, asset))
    score, quality, style, uncertain, decision = _verifier_state(verifier)
    return accepted, score, {
        "quality": quality,
        "style": style,
        "uncertain": uncertain,
        "decision": decision,
    }


def _sample_video_frames(path: Path, duration: float, folder: Path) -> list[tuple[float, Path]]:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg or duration <= 0:
        return []
    sample_folder = folder / ".video_verify"
    sample_folder.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256(str(path.resolve()).encode("utf-8")).hexdigest()[:12]
    frames: list[tuple[float, Path]] = []
    for index, fraction in enumerate(VIDEO_SAMPLE_FRACTIONS):
        timestamp = min(max(0.05, duration * fraction), max(0.05, duration - 0.05))
        destination = sample_folder / f"{digest}_{index}.jpg"
        result = subprocess.run(
            [
                ffmpeg,
                "-y",
                "-ss",
                f"{timestamp:.3f}",
                "-i",
                str(path),
                "-frames:v",
                "1",
                "-vf",
                "scale='min(960,iw)':-2",
                "-q:v",
                "3",
                str(destination),
            ],
            capture_output=True,
            text=True,
            timeout=45,
            creationflags=_creationflags(),
        )
        if result.returncode == 0 and destination.is_file() and destination.stat().st_size > 0:
            frames.append((timestamp, destination))
        else:
            destination.unlink(missing_ok=True)
    return frames


def _video_decision(
    verifier: Any,
    query: str,
    asset: AcquiredAsset,
    folder: Path,
) -> tuple[bool, int, dict[str, Any]]:
    duration = float(asset.candidate.duration or 0.0) or _media_duration(Path(asset.path))
    frames = _sample_video_frames(Path(asset.path), duration, folder)
    if not frames:
        return False, -100, {"decision": "video frames could not be sampled"}

    accepted_frames: list[tuple[int, float, dict[str, Any]]] = []
    rejected = 0
    for timestamp, frame_path in frames:
        frame_candidate = replace(
            asset.candidate,
            id=f"{asset.candidate.id}:frame:{timestamp:.3f}",
            url=str(frame_path),
            kind="image",
            duration=0.0,
            title=f"{asset.candidate.title} sampled video frame".strip(),
        )
        frame_asset = AcquiredAsset(frame_candidate, frame_path, reused=True)
        try:
            accepted, score, detail = _image_decision(verifier, query, frame_asset)
        finally:
            frame_path.unlink(missing_ok=True)
        if accepted:
            accepted_frames.append((score, timestamp, detail))
        else:
            rejected += 1

    # A video must be relevant through most of the sampled section. This prevents
    # accepting a clip because one transient frame happened to match the query.
    if len(accepted_frames) < 2 or rejected > len(frames) // 2:
        return False, -100, {"decision": "most sampled video frames failed visual verification"}

    accepted_frames.sort(key=lambda item: item[0], reverse=True)
    best_score, best_time, detail = accepted_frames[0]
    average_score = round(sum(item[0] for item in accepted_frames) / len(accepted_frames))
    detail = dict(detail)
    detail.update(
        {
            "best_time": best_time,
            "verified_frame_count": len(accepted_frames),
            "video_duration": duration,
        }
    )
    return True, average_score + VIDEO_BONUS, detail


def _discard(asset: AcquiredAsset) -> None:
    if asset.reused:
        return
    try:
        Path(asset.path).unlink(missing_ok=True)
    except OSError:
        pass


def _verify_candidate(
    engine: AssetAcquisitionEngine,
    verifier: Any,
    query: str,
    candidate: AssetCandidate,
    folder: Path,
    index: int,
    total: int,
) -> tuple[AcquiredAsset | None, int, dict[str, Any]]:
    try:
        asset = engine._download_candidate(candidate, folder, index, total)  # noqa: SLF001
    except Exception as error:
        return None, -100, {"decision": f"download failed: {error}"}

    try:
        if candidate.kind == "video":
            accepted, score, detail = _video_decision(verifier, query, asset, folder)
        else:
            accepted, score, detail = _image_decision(verifier, query, asset)
    except Exception as error:
        accepted, score, detail = False, -100, {"decision": f"visual verification failed: {error}"}

    if not accepted:
        _discard(asset)
        return None, score, detail

    metadata = dict(candidate.metadata)
    metadata.update({f"verified_{key}": value for key, value in detail.items()})
    verified_candidate = replace(candidate, metadata=metadata)
    return AcquiredAsset(verified_candidate, Path(asset.path), asset.reused), score, detail


def _candidate_pool(
    engine: AssetAcquisitionEngine,
    query: str,
    *,
    limit: int,
    target_ratio: float | None,
    used: set[str],
) -> list[AssetCandidate]:
    collected: list[AssetCandidate] = []
    for kind in ("video", "image"):
        try:
            items = engine.search(
                query,
                kind=kind,
                limit=limit,
                target_ratio=target_ratio,
                require_subject=False,
            )
        except Exception:
            items = []
        per_kind = 0
        for candidate in items:
            key = _candidate_key(candidate)
            if key in used or candidate.url in used:
                continue
            collected.append(candidate)
            per_kind += 1
            if per_kind >= VERIFY_PER_KIND:
                break
    return collected


def acquire_mixed_many(
    engine: AssetAcquisitionEngine,
    verifier: Any,
    queries: Iterable[str],
    destination_folder: str | Path,
    *,
    limit: int = 20,
    target_ratio: float | None = None,
    unique: bool = False,
    **_options: Any,
) -> list[AcquiredAsset]:
    """Acquire the best verified image or video for each query."""
    folder = Path(destination_folder)
    folder.mkdir(parents=True, exist_ok=True)
    items = [str(query).strip() for query in queries if str(query).strip()]
    results: list[AcquiredAsset] = []
    used: set[str] = set()

    for query_index, query in enumerate(items, start=1):
        engine._progress("acquire", query_index, len(items), query)  # noqa: SLF001
        pool = _candidate_pool(
            engine,
            query,
            limit=limit,
            target_ratio=target_ratio,
            used=used if unique else set(),
        )
        if not pool:
            # Preserve the proven image-only path as a final fallback.
            fallback = engine.acquire(
                query,
                folder,
                kind="image",
                limit=limit,
                target_ratio=target_ratio,
                excluded=used if unique else None,
            )
            results.append(fallback)
            if unique:
                used.update({_candidate_key(fallback.candidate), fallback.candidate.url})
            continue

        best: AcquiredAsset | None = None
        best_score = -100
        best_detail: dict[str, Any] = {}
        for index, candidate in enumerate(pool, start=1):
            engine._progress(  # noqa: SLF001
                "verify",
                index,
                len(pool),
                f"Checking {candidate.kind}: {candidate.title or candidate.id}",
            )
            asset, score, detail = _verify_candidate(
                engine,
                verifier,
                query,
                candidate,
                folder,
                index,
                len(pool),
            )
            if asset is None:
                continue
            if best is None or score > best_score:
                if best is not None:
                    _discard(best)
                best = asset
                best_score = score
                best_detail = detail
            else:
                _discard(asset)

        if best is None:
            # If mixed verification found nothing, ask the existing verified
            # image acquisition path for its established fallback hierarchy.
            fallback = engine.acquire(
                query,
                folder,
                kind="image",
                limit=limit,
                target_ratio=target_ratio,
                excluded=used if unique else None,
            )
            best = fallback
            best_score = 0
            best_detail = {"decision": "mixed candidates exhausted; image fallback"}

        engine._progress(  # noqa: SLF001
            "verify",
            1,
            1,
            f"Selected verified {best.candidate.kind} (score {best_score}): {best_detail.get('decision', 'accepted')}",
        )
        results.append(best)
        if unique:
            used.update({_candidate_key(best.candidate), best.candidate.url})

    return results


def install_mixed_visual_acquisition(engine: AssetAcquisitionEngine, verifier: Any) -> AssetAcquisitionEngine:
    """Patch one configured engine so normal image production searches both media kinds."""
    original_acquire_many = engine.acquire_many

    def acquire_many(queries: Iterable[str], destination_folder: str | Path, *, kind: str = "image", **options: Any):
        if kind != "image":
            return original_acquire_many(queries, destination_folder, kind=kind, **options)
        return acquire_mixed_many(
            engine,
            verifier,
            queries,
            destination_folder,
            **options,
        )

    engine.acquire_many = acquire_many  # type: ignore[method-assign]
    return engine


def _scene_caption_specs(value: str) -> list[tuple[float, float, str]]:
    text = str(value or "").replace("\r\n", "\n").strip()
    if not text:
        return []
    pattern = re.compile(
        r"(?m)^\s*(\d+(?:\.\d+)?)\s*[–—-]\s*(\d+(?:\.\d+)?)\s*(?:sec|secs|seconds?)?\s*$"
    )
    matches = list(pattern.finditer(text))
    result: list[tuple[float, float, str]] = []
    for index, match in enumerate(matches):
        body_end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        caption = text[match.end():body_end].strip()
        start = float(match.group(1))
        end = float(match.group(2))
        if caption and end > start:
            result.append((start, end, caption))
    return result


def _remove_emojis(value: str) -> str:
    # Keep this deliberately broad and cheap. The Resolve renderer uses the same
    # policy: captions should be readable text rather than unsupported emoji.
    return "".join(ch for ch in str(value or "") if ord(ch) < 0x1F000).strip()


def _wrap_caption(value: str, width: int = 24) -> str:
    words = _remove_emojis(value).split()
    lines: list[str] = []
    current: list[str] = []
    for word in words:
        trial = " ".join([*current, word])
        if current and len(trial) > width:
            lines.append(" ".join(current))
            current = [word]
        else:
            current.append(word)
    if current:
        lines.append(" ".join(current))
    return "\n".join(lines)


def _escape_drawtext(value: str) -> str:
    text = str(value or "")
    for old, new in (
        ("\\", r"\\"),
        (":", r"\:"),
        ("'", r"\'"),
        ("%", r"\%"),
        ("[", r"\["),
        ("]", r"\]"),
        (",", r"\,"),
        (";", r"\;"),
    ):
        text = text.replace(old, new)
    return text.replace("\n", r"\n")


def _font_path() -> Path | None:
    for path in (
        Path("C:/Windows/Fonts/arialbd.ttf"),
        Path("C:/Windows/Fonts/seguisb.ttf"),
        Path("C:/Windows/Fonts/segoeui.ttf"),
        Path("C:/Windows/Fonts/arial.ttf"),
    ):
        if path.is_file():
            return path
    return None


def _prepare_video_scene(
    asset: AcquiredAsset,
    destination_folder: Path,
    *,
    desired_duration: float,
    caption: str,
    logo: Path,
) -> AcquiredAsset:
    ffmpeg = shutil.which("ffmpeg")
    font = _font_path()
    source = Path(asset.path).resolve()
    source_duration = float(asset.candidate.duration or 0.0) or _media_duration(source)
    if not ffmpeg or not font or source_duration <= 0:
        return asset

    # Give the timeline a little handle room so narration rescaling can shorten
    # the scene without ever running past the physical stock clip.
    output_duration = min(source_duration, max(2.0, desired_duration * 1.20))
    best_time = float(asset.candidate.metadata.get("verified_best_time") or source_duration / 2.0)
    start = max(0.0, min(source_duration - output_duration, best_time - output_duration / 2.0))

    caption_text = _escape_drawtext(_wrap_caption(caption))
    font_filter = str(font.resolve()).replace("\\", "/").replace(":", r"\:")
    digest = hashlib.sha256(
        f"{source}|{source.stat().st_mtime_ns}|{start:.3f}|{output_duration:.3f}|{caption_text}|mixed-v1".encode("utf-8")
    ).hexdigest()[:12]
    destination = destination_folder / f"video_scene_{digest}.mp4"
    if destination.is_file() and destination.stat().st_size > 0:
        candidate = replace(asset.candidate, duration=output_duration)
        return AcquiredAsset(candidate, destination, reused=True)

    filter_complex = (
        "[0:v]scale=1200:2134:force_original_aspect_ratio=increase,"
        "crop=1080:1920,setsar=1[scene];"
        "[1:v]format=rgba,scale=190:-1[logo];"
        "[scene][logo]overlay=W-w-35:H-h-35:repeatlast=1:shortest=0[branded];"
        f"[branded]drawtext=fontfile='{font_filter}':text='{caption_text}':"
        "fontcolor=white:fontsize=76:line_spacing=12:borderw=5:bordercolor=black:"
        "box=1:boxcolor=black@0.45:boxborderw=22:x=(w-text_w)/2:y=120[out]"
    )
    result = subprocess.run(
        [
            ffmpeg,
            "-y",
            "-ss",
            f"{start:.3f}",
            "-i",
            str(source),
            "-i",
            str(logo),
            "-filter_complex",
            filter_complex,
            "-map",
            "[out]",
            "-t",
            f"{output_duration:.3f}",
            "-r",
            "30",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-profile:v",
            "high",
            "-level",
            "4.1",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            "-an",
            str(destination),
        ],
        capture_output=True,
        text=True,
        timeout=180,
        creationflags=_creationflags(),
    )
    if result.returncode != 0 or not destination.is_file() or destination.stat().st_size <= 0:
        destination.unlink(missing_ok=True)
        return asset

    metadata = dict(asset.candidate.metadata)
    metadata["prepared_video_scene"] = True
    metadata["prepared_from"] = str(source)
    candidate = replace(asset.candidate, duration=output_duration, metadata=metadata)
    return AcquiredAsset(candidate, destination, reused=False)


def prepare_selected_videos(context: Any, assets: Sequence[AcquiredAsset]) -> list[AcquiredAsset]:
    """Trim, crop, watermark, and caption selected videos using imported scene timing."""
    project = getattr(context, "project", None)
    project = project if isinstance(project, Mapping) else {}
    on_screen = str(
        project.get("on_screen_text")
        or project.get("onscreen_text")
        or project.get("On-Screen Text")
        or ""
    )
    specs = _scene_caption_specs(on_screen)
    project_folder = Path(getattr(context, "project_folder"))
    destination_folder = project_folder / "Assets" / "Acquired"
    destination_folder.mkdir(parents=True, exist_ok=True)
    logo = Path(__file__).resolve().parent.parent / "assets" / "facts_logo.png"
    if not logo.is_file():
        return list(assets)

    prepared: list[AcquiredAsset] = []
    for index, asset in enumerate(assets):
        if asset.candidate.kind != "video":
            prepared.append(asset)
            continue
        if index < len(specs):
            start, end, caption = specs[index]
            duration = max(0.5, end - start)
        else:
            duration = 6.0
            caption = ""
        prepared.append(
            _prepare_video_scene(
                asset,
                destination_folder,
                desired_duration=duration,
                caption=caption,
                logo=logo,
            )
        )
    return prepared


__all__ = [
    "acquire_mixed_many",
    "install_mixed_visual_acquisition",
    "prepare_selected_videos",
]
