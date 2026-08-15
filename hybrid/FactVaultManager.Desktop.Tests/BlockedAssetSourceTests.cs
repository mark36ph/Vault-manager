namespace FactVaultManager.Desktop.Tests;

public sealed class BlockedAssetSourceTests
{
    [Fact]
    public void ThingiverseSourcePage_IsHardBlockedForEveryScene()
    {
        var candidate = Candidate(
            sourcePage: "https://www.thingiverse.com/thing:123456",
            url: "https://cdn.example.test/octopus.jpg",
            title: "Octopus model");

        Assert.Equal("thingiverse.com", NativeVisualSelectionPolicy.BlockedSourceReason(candidate));
        Assert.Contains(
            "blocked source",
            NativeVisualSelectionPolicy.SceneContradiction("octopus swimming underwater", candidate),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThingiverseMediaHost_IsHardBlockedEvenWithoutThingiverseMetadata()
    {
        var candidate = Candidate(
            sourcePage: "https://example.test/item/123",
            url: "https://cdn.thingiverse.com/assets/octopus.jpg",
            title: "Octopus");

        Assert.Equal("thingiverse.com", NativeVisualSelectionPolicy.BlockedSourceReason(candidate));
    }

    [Fact]
    public void ThingiverseMetadata_IsHardBlockedWhenProviderHidesOriginalHost()
    {
        var candidate = Candidate(
            sourcePage: "https://archive.example.test/item/123",
            url: "https://images.example.test/octopus.jpg",
            title: "3D printed octopus from Thingiverse");

        Assert.Equal("thingiverse.com", NativeVisualSelectionPolicy.BlockedSourceReason(candidate));
    }

    [Fact]
    public void OrdinaryFactualSource_IsNotBlocked()
    {
        var candidate = Candidate(
            sourcePage: "https://commons.wikimedia.org/wiki/File:Octopus.jpg",
            url: "https://upload.wikimedia.org/octopus.jpg",
            title: "Common octopus underwater");

        Assert.Equal("", NativeVisualSelectionPolicy.BlockedSourceReason(candidate));
        Assert.Equal("", NativeVisualSelectionPolicy.SceneContradiction("octopus swimming underwater", candidate));
    }

    private static NativeAssetCandidate Candidate(string sourcePage, string url, string title) =>
        new(
            "openverse",
            "asset-1",
            url,
            "image",
            title,
            1200,
            1800,
            0,
            1,
            "tester",
            "cc0",
            sourcePage);
}
