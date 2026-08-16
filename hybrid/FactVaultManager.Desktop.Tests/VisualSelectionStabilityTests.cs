namespace FactVaultManager.Desktop.Tests;

public sealed class VisualSelectionStabilityTests
{
    [Fact]
    public void LoneKrakenTag_DoesNotDowngradeWithoutFantasySupport()
    {
        var factualCue = NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
            "octopus swimming underwater",
            "octopus marine organism seafood swimming tentacle squid kraken underwater zoo");
        var fantasyCue = NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
            "octopus swimming underwater",
            "octopus kraken fantasy underwater creature");

        Assert.Empty(factualCue);
        Assert.Equal("fantasy", fantasyCue);
    }

    [Fact]
    public async Task VerifiedScientificReferenceIllustration_RemainsRepresentational()
    {
        var candidate = Candidate(
            "science",
            "American marine biology bulletin Fig 42 octopus crawling illustration Smithsonian Libraries");
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var verifier = new StubVerifier(VerificationResult(
            subjectVisible: true,
            sceneEvidenceVisible: true,
            quality: "acceptable",
            style: "literal"));
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            var result = await verified.AcquireAsync(
                "octopus crawling ocean floor",
                folder,
                attempts: 1,
                requiredSubject: "octopus");

            Assert.Equal("science", result.Candidate.Id);
            Assert.Equal("representational", verified.LastSelectedStyle);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task StableStatueFinding_IsReusedAcrossSceneChecks()
    {
        var candidate = Candidate("seal", "seal swimming underwater marine wildlife");
        using var client = new HttpClient(new StubDownloadHandler());
        using var engine = new NativeAssetAcquisitionEngine(new[] { new StubProvider(new[] { candidate }) }, client);
        var calls = 0;
        var verifier = new StubVerifier((_, _) =>
        {
            calls++;
            return calls == 1
                ? VerificationResult(
                    subjectVisible: true,
                    sceneEvidenceVisible: true,
                    accepted: false,
                    decision: "hard negative unrequested_statue_or_sculpture",
                    hardNegative: "unrequested_statue_or_sculpture",
                    hardNegativeConfidence: 0.90)
                : VerificationResult(subjectVisible: true, sceneEvidenceVisible: true);
        });
        var verified = new NativeVerifiedAssetAcquisitionEngine(engine, verifier);
        var folder = TestFolder();

        try
        {
            await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
                verified.AcquireAsync(
                    "seal swimming underwater",
                    folder,
                    attempts: 1,
                    requiredSubject: "seal"));

            await Assert.ThrowsAsync<NativeAssetAcquisitionException>(() =>
                verified.AcquireAsync(
                    "seal swimming underwater",
                    folder,
                    attempts: 1,
                    requiredSubject: "seal"));

            Assert.Equal(1, calls);
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
        string decision = "kept",
        string hardNegative = "none",
        double hardNegativeConfidence = 0) => new(
        accepted,
        decision,
        quality,
        style,
        false,
        false,
        0,
        false,
        0,
        hardNegative,
        hardNegativeConfidence,
        subjectVisible,
        sceneEvidenceVisible,
        false,
        0,
        "visually_recognizable");

    private static NativeAssetCandidate Candidate(string id, string title) => new(
        "test",
        id,
        $"https://example.invalid/{id}.jpg",
        "image",
        title,
        1080,
        1920,
        0,
        100,
        "",
        "CC0",
        $"https://example.invalid/source/{id}");

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
