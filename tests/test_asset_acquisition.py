from pathlib import Path

import pytest

from common.asset_acquisition import (
    AssetAcquisitionEngine,
    AssetAcquisitionError,
    AssetCandidate,
    make_asset_acquisition_provider,
)


class Provider:
    def __init__(self, name, results=(), error=None):
        self.name = name
        self.results = list(results)
        self.error = error
        self.calls = []

    def search(self, query, *, kind, limit):
        self.calls.append((query, kind, limit))
        if self.error:
            raise self.error
        return self.results


def candidate(id="one", *, score=1, url="https://example.test/a.jpg", width=100, height=200, kind="image", provider="stock"):
    return AssetCandidate(provider=provider, id=id, url=url, title=id, score=score, width=width, height=height, kind=kind)


def test_requires_provider():
    with pytest.raises(ValueError, match="provider"):
        AssetAcquisitionEngine([])


def test_search_calls_provider():
    provider = Provider("stock", [candidate()])
    results = AssetAcquisitionEngine([provider]).search("ocean", limit=5)
    assert len(results) == 1
    assert provider.calls == [("ocean", "image", 5)]


def test_search_rejects_empty_query():
    with pytest.raises(ValueError, match="query"):
        AssetAcquisitionEngine([Provider("stock")]).search(" ")


def test_search_falls_back_after_provider_error():
    good = Provider("good", [candidate(provider="good")])
    results = AssetAcquisitionEngine([Provider("bad", error=RuntimeError("offline")), good]).search("ocean")
    assert results[0].provider == "good"


def test_all_provider_errors_are_reported():
    with pytest.raises(AssetAcquisitionError, match="offline"):
        AssetAcquisitionEngine([Provider("bad", error=RuntimeError("offline"))]).search("ocean")


def test_rank_prefers_score():
    ranked = AssetAcquisitionEngine.rank([candidate("low", score=1), candidate("high", score=9, url="https://x/high.jpg")])
    assert ranked[0].id == "high"


def test_rank_uses_resolution_as_tiebreaker():
    ranked = AssetAcquisitionEngine.rank([
        candidate("small", width=100, height=100),
        candidate("large", width=1000, height=1000, url="https://x/large.jpg"),
    ])
    assert ranked[0].id == "large"


def test_rank_rewards_target_aspect_ratio():
    portrait = candidate("portrait", score=1, width=900, height=1600)
    square = candidate("square", score=1, width=1000, height=1000, url="https://x/square.jpg")
    assert AssetAcquisitionEngine.rank([square, portrait], target_ratio=9 / 16)[0].id == "portrait"


def test_rank_deduplicates_url():
    duplicate = candidate("two", score=5)
    ranked = AssetAcquisitionEngine.rank([candidate(score=1), duplicate])
    assert len(ranked) == 1
    assert ranked[0].id == "two"


def test_search_filters_wrong_kind():
    provider = Provider("stock", [candidate(kind="video")])
    assert AssetAcquisitionEngine([provider]).search("ocean", kind="image") == []


def test_acquire_downloads_best_candidate(tmp_path):
    writes = []

    def download(url, path):
        writes.append(url)
        path.write_bytes(b"media")

    provider = Provider("stock", [candidate("best", score=10)])
    result = AssetAcquisitionEngine([provider], downloader=download).acquire("ocean", tmp_path)
    assert result.path.read_bytes() == b"media"
    assert result.reused is False
    assert writes == ["https://example.test/a.jpg"]


def test_acquire_reuses_cached_file(tmp_path):
    item = candidate()
    engine = AssetAcquisitionEngine([Provider("stock", [item])], downloader=lambda *_: pytest.fail("download called"))
    cached = engine._destination(item, tmp_path)
    cached.write_bytes(b"cached")
    result = engine.acquire("ocean", tmp_path)
    assert result.reused is True
    assert result.path == cached


def test_acquire_falls_back_after_download_failure(tmp_path):
    attempts = []

    def download(url, path):
        attempts.append(url)
        if "bad" in url:
            raise OSError("failed")
        path.write_bytes(b"ok")

    provider = Provider("stock", [
        candidate("bad", score=9, url="https://x/bad.jpg"),
        candidate("good", score=8, url="https://x/good.jpg"),
    ])
    result = AssetAcquisitionEngine([provider], downloader=download).acquire("ocean", tmp_path, attempts=2)
    assert result.candidate.id == "good"
    assert attempts == ["https://x/bad.jpg", "https://x/good.jpg"]


def test_failed_partial_download_is_removed(tmp_path):
    def download(url, path):
        path.write_bytes(b"partial")
        raise OSError("broken")

    engine = AssetAcquisitionEngine([Provider("stock", [candidate()])], downloader=download)
    with pytest.raises(AssetAcquisitionError):
        engine.acquire("ocean", tmp_path)
    assert not list(tmp_path.glob("*.part"))


def test_acquire_many_preserves_query_order(tmp_path):
    provider = Provider("stock", [candidate()])
    engine = AssetAcquisitionEngine([provider], downloader=lambda url, path: path.write_bytes(b"x"))
    results = engine.acquire_many(["one", "two"], tmp_path)
    assert len(results) == 2
    assert [call[0] for call in provider.calls] == ["one", "two"]


def test_progress_reports_search_and_download(tmp_path):
    events = []
    provider = Provider("stock", [candidate()])
    engine = AssetAcquisitionEngine(
        [provider],
        downloader=lambda url, path: path.write_bytes(b"x"),
        progress_callback=lambda *event: events.append(event),
    )
    engine.acquire("ocean", tmp_path)
    assert events[0][0] == "search"
    assert any(event[0] == "download" for event in events)


def test_content_production_provider_uses_prompts(tmp_path):
    provider = Provider("stock", [candidate()])
    engine = AssetAcquisitionEngine([provider], downloader=lambda url, path: path.write_bytes(b"x"))
    service = make_asset_acquisition_provider(engine, tmp_path)

    class Context:
        image_prompts = ["ocean", "waves"]
        timeline = None

    result = service(Context())
    assert len(result) == 2
