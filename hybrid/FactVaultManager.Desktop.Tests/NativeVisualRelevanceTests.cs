using System.Reflection;

namespace FactVaultManager.Desktop.Tests;

public sealed class NativeVisualRelevanceTests
{
    [Fact]
    public void ImportedVisualQuery_IgnoresCategoryAndAnchorsRealTopicSubject()
    {
        var project = new DesktopProject(
            1,
            "Wombat Poop Is Cube-Shaped",
            "Ancient Civilizations",
            "Completed",
            "Completed/Wombat Poop Is Cube-Shaped",
            "2026-01-01 00:00",
            "script",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            false);
        var context = new NativeProductionContext
        {
            Project = project,
            ProjectFolder = Path.GetTempPath(),
            AppSettings = new AppSettingsModel(),
            Topic = project.Title,
            Script = project.Script,
        };

        var method = typeof(NativeProductionOrchestrator).GetMethod(
            "AnchorImportedQuery",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var query = (string)method!.Invoke(
            null,
            new object[] { "Ancient Civilizations wombat close up Australia wildlife", context })!;

        Assert.Equal("wombat close up Australia wildlife", query);
        Assert.DoesNotContain(project.Category, query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationQuery_UsesExplicitSubjectAnchorWithoutDuplicatingSubject()
    {
        var query = NativeVerifiedAssetAcquisitionEngine.BuildVerificationQuery(
            "wombat close up Australia wildlife",
            "wombat");

        Assert.Equal("subject wombat close up Australia wildlife", query);
        Assert.Equal("wombat", NativeNamedSubjectVerifier.ExplicitSubjectPhrase(query));
    }

    [Fact]
    public async Task ExplicitSubject_IsRejectedWhenNeitherSubjectNorSceneEvidenceIsVisible()
    {
        var baseVerifier = new StubVerifier(VerificationResult(subjectVisible: false, sceneEvidenceVisible: false));
        var verifier = new NativeNamedSubjectVerifier(baseVerifier);

        var result = await verifier.VerifyAsync(
            "subject wombat close up Australia wildlife",
            TestAsset());

        Assert.False(result.Accepted);
        Assert.Contains("explicit subject missing", result.Decision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitSubject_AllowsDistinctiveSceneEvidenceWhenSubjectIsNotVisible()
    {
        var baseVerifier = new StubVerifier(VerificationResult(subjectVisible: false, sceneEvidenceVisible: true));
        var verifier = new NativeNamedSubjectVerifier(baseVerifier);

        var result = await verifier.VerifyAsync(
            "subject wombat cube shaped droppings ground Australia",
            TestAsset());

        Assert.True(result.Accepted);
    }

    private static NativeAssetVerificationResult VerificationResult(bool subjectVisible, bool sceneEvidenceVisible) => new(
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
        subjectVisible,
        sceneEvidenceVisible,
        false,
        0,
        "visually_recognizable");

    private static NativeAcquiredAsset TestAsset() => new(
        new NativeAssetCandidate(
            "test",
            "1",
            "https://example.invalid/asset.jpg",
            "image",
            "test asset",
            100,
            100,
            0,
            0,
            "",
            "",
            ""),
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
