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

    [Fact]
    public async Task SceneEvidence_IsPreferredOverHigherQualitySubjectOnlyMatch()
    {
        var candidates = new[]
        {
            TestCandidate("1", 100),
            TestCandidate("2", 1),
        };
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(candidates) }, client);
        var verifier = new StubVerifier(asset =>
            asset.Candidate.Id == "1"
                ? VerificationResult(subjectVisible: true, sceneEvidenceVisible: false, quality: "preferred")
                : VerificationResult(subjectVisible: true, sceneEvidenceVisible: true, quality: "acceptable"));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var result = await verified.AcquireAsync(
                "wombat droppings ground wildlife Australia",
                folder,
                attempts: 2,
                requiredSubject: "wombat");

            Assert.Equal("2", result.Candidate.Id);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ExcludedAsset_IsNotReintroducedWhenNoFreshCandidateExists()
    {
        var candidate = TestCandidate("1", 100);
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var folder = TestFolder();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "test:1",
            candidate.Url,
        };

        try
        {
            var error = await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
                engine.AcquireAsync(
                    "wombat wildlife",
                    folder,
                    attempts: 1,
                    excluded: excluded));

            Assert.Contains("no unexcluded image assets", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private static NativeAssetVerificationResult VerificationResult(
        bool subjectVisible,
        bool sceneEvidenceVisible,
        string quality = "preferred",
        string style = "literal") => new(
        true,
        "kept",
        quality,
        style,
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
        TestCandidate("1", 0),
        "unused.jpg",
        true);

    private static NativeAssetCandidate TestCandidate(string id, double score) => new(
        "test",
        id,
        $"https://example.invalid/{id}.jpg",
        "image",
        "wombat droppings ground wildlife Australia",
        100,
        100,
        0,
        score,
        "",
        "",
        "");

    private static string TestFolder() =>
        Path.Combine(Path.GetTempPath(), "FactVaultManager-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class StubVerifier : INativeAssetVerifier
    {
        private readonly Func<NativeAcquiredAsset, NativeAssetVerificationResult> _result;

        public StubVerifier(NativeAssetVerificationResult result) : this(_ => result) { }

        public StubVerifier(Func<NativeAcquiredAsset, NativeAssetVerificationResult> result) =>
            _result = result;

        public Task<NativeAssetVerificationResult> VerifyAsync(
            string query,
            NativeAcquiredAsset asset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result(asset));
    }

    private sealed class StubProvider : INativeAssetProvider
    {
        private readonly IReadOnlyList<NativeAssetCandidate> _candidates;

        public StubProvider(IReadOnlyList<NativeAssetCandidate> candidates) =>
            _candidates = candidates;

        public string Name => "test";

        public Task<IReadOnlyList<NativeAssetCandidate>> SearchAsync(
            string query,
            string kind,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NativeAssetCandidate>>(_candidates.Take(limit).ToArray());
    }

    private sealed class StubDownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }),
            };
            return Task.FromResult(response);
        }
    }
}
