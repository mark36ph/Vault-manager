from common.asset_acquisition import AcquiredAsset, AssetCandidate
from common.verified_asset_acquisition import VerifiedAssetAcquisitionEngine


def test_rejected_reused_asset_is_not_deleted(tmp_path):
    path = tmp_path / "shared.jpg"
    path.write_bytes(b"existing-selected-visual")
    candidate = AssetCandidate(
        provider="stock",
        id="shared",
        url="https://example.test/shared.jpg",
        title="Shared visual",
        kind="image",
    )
    asset = AcquiredAsset(candidate=candidate, path=path, reused=True)

    VerifiedAssetAcquisitionEngine._discard_rejected_asset(asset)

    assert path.is_file()
    assert path.read_bytes() == b"existing-selected-visual"


def test_rejected_new_download_is_still_deleted(tmp_path):
    path = tmp_path / "new.jpg"
    path.write_bytes(b"temporary-candidate")
    candidate = AssetCandidate(
        provider="stock",
        id="new",
        url="https://example.test/new.jpg",
        title="New visual",
        kind="image",
    )
    asset = AcquiredAsset(candidate=candidate, path=path, reused=False)

    VerifiedAssetAcquisitionEngine._discard_rejected_asset(asset)

    assert not path.exists()
