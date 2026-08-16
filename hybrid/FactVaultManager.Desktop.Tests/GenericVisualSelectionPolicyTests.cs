namespace FactVaultManager.Desktop.Tests;

public sealed class GenericVisualSelectionPolicyTests
{
    [Fact]
    public void LivingScene_WeakFoodTagAloneDoesNotRejectFactualCandidate()
    {
        var candidate = Candidate("Seal swimming underwater marine wildlife seafood");

        var conflict = NativeVisualSelectionPolicy.SceneContradiction(
            "seal swimming underwater",
            candidate);

        Assert.Empty(conflict);
    }

    [Fact]
    public void LivingScene_StrongOrMultipleWeakFoodCuesAreRejected()
    {
        var strong = Candidate("Grilled tuna served at a restaurant");
        var multipleWeak = Candidate("Tuna seafood food market stall");

        var strongConflict = NativeVisualSelectionPolicy.SceneContradiction(
            "tuna swimming underwater",
            strong);
        var weakConflict = NativeVisualSelectionPolicy.SceneContradiction(
            "tuna swimming underwater",
            multipleWeak);

        Assert.Contains("prepared-food", strongConflict, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared-food", weakConflict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitFoodQuery_DoesNotRejectFoodImagery()
    {
        var candidate = Candidate("Tuna seafood food market stall");

        var conflict = NativeVisualSelectionPolicy.SceneContradiction(
            "tuna seafood market food",
            candidate);

        Assert.Empty(conflict);
    }

    [Fact]
    public void FantasyAndDigitalArt_AreDecorativeCuesUnlessRequested()
    {
        var candidate = Candidate("Wolf running through forest fantasy digital art");

        var cue = NativeVisualSelectionPolicy.UnrequestedSyntheticRepresentation(
            "wolf running forest",
            candidate);
        var requestedCue = NativeVisualSelectionPolicy.UnrequestedSyntheticRepresentation(
            "wolf fantasy digital art",
            candidate);

        Assert.NotEmpty(cue);
        Assert.Empty(requestedCue);
    }

    [Fact]
    public void MutantHybridAndMonster_AreDecorativeCuesUnlessRequested()
    {
        Assert.Equal(
            "hybrid",
            NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
                "elephant wildlife habitat",
                "elephant octopus mutant hybrid animal"));

        Assert.Equal(
            "monster",
            NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
                "deep sea animal underwater",
                "deep sea monster creature"));

        Assert.Empty(
            NativeVerifiedAssetAcquisitionEngine.UnrequestedSyntheticRepresentation(
                "hybrid animal biology",
                "hybrid animal biology"));
    }

    private static NativeAssetCandidate Candidate(string title) =>
        new(
            "pexels",
            Guid.NewGuid().ToString("N"),
            "https://example.invalid/image.jpg",
            "image",
            title,
            1080,
            1920,
            0,
            1,
            "",
            "CC0",
            "https://example.invalid/source");
}
