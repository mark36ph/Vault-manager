namespace FactVaultManager.Desktop.Tests;

public sealed class ScientificEvidenceMetadataVerifierTests
{
    [Fact]
    public async Task InternalScientificEvidence_WithoutMetadataSupportIsDemotedToSubjectOnly()
    {
        var verifier = new NativeScientificEvidenceMetadataVerifier(new StubVerifier(ConfirmedEvidence()));
        var asset = Asset("Octopuses-Burnie-20150304-002");

        var result = await verifier.VerifyAsync(
            "subject octopus gills anatomy illustration",
            asset);

        Assert.True(result.Accepted);
        Assert.True(result.RequestedSubjectVisible);
        Assert.False(result.RequestedSceneEvidenceVisible);
        Assert.Contains("metadata or source support", result.Decision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InternalScientificEvidence_WithSourceSupportKeepsEvidenceCredit()
    {
        var verifier = new NativeScientificEvidenceMetadataVerifier(new StubVerifier(ConfirmedEvidence()));
        var asset = Asset(
            "Octopuses-Burnie-20150304-002",
            credit: "Smithsonian gills anatomy reference");

        var result = await verifier.VerifyAsync(
            "subject octopus gills anatomy illustration",
            asset);

        Assert.True(result.RequestedSceneEvidenceVisible);
    }

    [Fact]
    public async Task ObservableBehavior_CanBeConfirmedFromPixelsWithoutMetadataSupport()
    {
        var verifier = new NativeScientificEvidenceMetadataVerifier(new StubVerifier(ConfirmedEvidence()));
        var asset = Asset("Octopus wildlife portrait");

        var result = await verifier.VerifyAsync(
            "subject octopus swimming underwater",
            asset);

        Assert.True(result.RequestedSceneEvidenceVisible);
    }

    [Fact]
    public async Task ColorMetadata_DoesNotSupportInternalBloodClaim()
    {
        var verifier = new NativeScientificEvidenceMetadataVerifier(new StubVerifier(ConfirmedEvidence()));
        var asset = Asset("A vibrant octopus resting on a bright blue surface");

        var result = await verifier.VerifyAsync(
            "subject octopus blue blood hemocyanin",
            asset);

        Assert.False(result.RequestedSceneEvidenceVisible);
        var evidenceWords = NativeScientificEvidenceMetadataVerifier.ScientificEvidenceWords(
            "subject octopus blue blood hemocyanin");
        Assert.DoesNotContain("blue", evidenceWords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("blood", evidenceWords, StringComparer.OrdinalIgnoreCase);
    }

    private static NativeAssetVerificationResult ConfirmedEvidence() => new(
        true,
        "kept",
        "preferred",
        "literal",
        false,
        false,
        0,
        false,
        0,
        "none",
        0,
        true,
        true,
        false,
        0,
        "visually_recognizable");

    private static NativeAcquiredAsset Asset(string title, string credit = "") => new(
        new NativeAssetCandidate(
            "test",
            Guid.NewGuid().ToString("N"),
            "https://example.invalid/image.jpg",
            "image",
            title,
            1080,
            1920,
            0,
            1,
            credit,
            "CC0",
            "https://example.invalid/source"),
        "unused.jpg",
        true);

    private sealed class StubVerifier : INativeAssetVerifier
    {
        private readonly NativeAssetVerificationResult _result;

        public StubVerifier(NativeAssetVerificationResult result) => _result = result;

        public Task<NativeAssetVerificationResult> VerifyAsync(
            string query,
            NativeAcquiredAsset asset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
