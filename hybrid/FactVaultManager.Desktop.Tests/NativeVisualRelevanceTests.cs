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
    public void SceneEvidenceSubject_RequiresDistinctiveLeadRatherThanGenericAction()
    {
        Assert.Equal(
            "droppings",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(
                "wombat droppings ground wildlife Australia",
                "wombat"));
        Assert.Equal(
            "",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(
                "wombat walking rocky ground Australia",
                "wombat"));
        Assert.Equal(
            "",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(
                "wombat close up Australia wildlife",
                "wombat"));
    }

    [Fact]
    public async Task ClaimedSceneEvidence_IsIndependentlyRecheckedAsExplicitVisualSubject()
    {
        var candidate = TestCandidate("1", 100);
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var seenQueries = new List<string>();
        var verifier = new StubVerifier((query, _) =>
        {
            seenQueries.Add(query);
            return query.StartsWith("subject droppings", StringComparison.OrdinalIgnoreCase)
                ? VerificationResult(subjectVisible: false, sceneEvidenceVisible: false, accepted: false, decision: "droppings absent")
                : VerificationResult(subjectVisible: true, sceneEvidenceVisible: true);
        });
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var result = await verified.AcquireAsync(
                "wombat droppings ground wildlife Australia",
                folder,
                attempts: 1,
                requiredSubject: "wombat");

            Assert.Equal("1", result.Candidate.Id);
            Assert.Contains(seenQueries, query =>
                query.StartsWith("subject droppings", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task AcquireMany_AllowsLastResortReuseInsteadOfFailingProduction()
    {
        var candidate = TestCandidate("1", 100);
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var verifier = new StubVerifier(VerificationResult(subjectVisible: true, sceneEvidenceVisible: false));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var results = await verified.AcquireManyAsync(
                new[] { "wombat close up wildlife", "wombat close up wildlife" },
                folder,
                attempts: 1,
                unique: true,
                requiredSubject: "wombat");

            Assert.Equal(2, results.Count);
            Assert.Equal("1", results[0].Candidate.Id);
            Assert.Equal("1", results[1].Candidate.Id);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task AcquireMany_PrefersFactualReuseOverFreshDecorativeFallback()
    {
        var candidates = new[]
        {
            TestCandidate("literal", 100),
            TestCandidate("decorative", 1),
        };
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(candidates) }, client);
        var verifier = new StubVerifier(asset =>
            asset.Candidate.Id == "decorative"
                ? VerificationResult(subjectVisible: true, sceneEvidenceVisible: false, style: "decorative")
                : VerificationResult(subjectVisible: true, sceneEvidenceVisible: false, style: "literal"));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var results = await verified.AcquireManyAsync(
                new[] { "wombat close up wildlife", "wombat close up wildlife" },
                folder,
                attempts: 2,
                unique: true,
                requiredSubject: "wombat");

            Assert.Equal(2, results.Count);
            Assert.Equal("literal", results[0].Candidate.Id);
            Assert.Equal("literal", results[1].Candidate.Id);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task UnrequestedPuppet_IsDowngradedBehindRealSubjectImage()
    {
        var candidates = new[]
        {
            TestCandidate("puppet", 100, "wombat walking rocky ground puppet creature"),
            TestCandidate("real", 1, "wombat walking rocky ground Australia wildlife"),
        };
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(candidates) }, client);
        var verifier = new StubVerifier(VerificationResult(
            subjectVisible: true,
            sceneEvidenceVisible: false,
            style: "representational"));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var result = await verified.AcquireAsync(
                "wombat walking rocky ground Australia",
                folder,
                attempts: 2,
                requiredSubject: "wombat");

            Assert.Equal("real", result.Candidate.Id);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RequestedPuppet_RemainsRepresentationalRatherThanDecorative()
    {
        var candidate = TestCandidate("puppet", 100, "wombat puppet creature walking on rocky ground");
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var verifier = new StubVerifier(VerificationResult(
            subjectVisible: true,
            sceneEvidenceVisible: false,
            style: "representational"));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var result = await verified.AcquireAsync(
                "wombat puppet walking rocky ground",
                folder,
                attempts: 1,
                requiredSubject: "wombat");

            Assert.Equal("puppet", result.Candidate.Id);
            Assert.Equal("representational", verified.LastSelectedStyle);
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
        string style = "literal",
        bool accepted = true,
        string decision = "kept") => new(
        accepted,
        decision,
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

    private static NativeAssetCandidate TestCandidate(string id, double score, string? title = null) => new(
        "test",
        id,
        $"https://example.invalid/{id}.jpg",
        "image",
        title ?? "wombat droppings ground wildlife Australia",
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
        private readonly Func<string, NativeAcquiredAsset, NativeAssetVerificationResult> _result;

        public StubVerifier(NativeAssetVerificationResult result) : this((_, _) => result) { }

        public StubVerifier(Func<NativeAcquiredAsset, NativeAssetVerificationResult> result) :
            this((_, asset) => result(asset)) { }

        public StubVerifier(Func<string, NativeAcquiredAsset, NativeAssetVerificationResult> result) =>
            _result = result;

        public Task<NativeAssetVerificationResult> VerifyAsync(
            string query,
            NativeAcquiredAsset asset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result(query, asset));
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
