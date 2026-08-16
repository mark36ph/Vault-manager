namespace FactVaultManager.Desktop.Tests;

public sealed class SceneEvidenceHeuristicTests
{
    [Fact]
    public void Body_IsGenericFramingRatherThanDistinctiveEvidence()
    {
        Assert.Equal(
            "",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(
                "wombat body close up wildlife",
                "wombat"));

        Assert.Equal(
            "droppings",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidenceSubject(
                "wombat droppings ground wildlife Australia",
                "wombat"));
    }

    [Fact]
    public void SceneEvidencePhrase_PreservesBehaviorContext()
    {
        Assert.Equal(
            "swimming underwater reef",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidencePhrase(
                "sea turtle sea turtle swimming underwater reef",
                "sea turtle"));

        Assert.Equal(
            "subject sea turtle swimming underwater reef",
            NativeVerifiedAssetAcquisitionEngine.BuildEvidenceVerificationQuery(
                "sea turtle sea turtle swimming underwater reef",
                "sea turtle",
                "swimming underwater reef"));
    }

    [Fact]
    public void SceneEvidencePhrase_PreservesScientificContextButLeavesStandaloneTraceAlone()
    {
        Assert.Equal(
            "blue blood protein",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidencePhrase(
                "crab blue blood protein",
                "crab"));

        Assert.Equal(
            "subject crab blue blood protein",
            NativeVerifiedAssetAcquisitionEngine.BuildEvidenceVerificationQuery(
                "crab blue blood protein",
                "crab",
                "blue blood protein"));

        Assert.Equal(
            "droppings",
            NativeVerifiedAssetAcquisitionEngine.SceneEvidencePhrase(
                "wombat droppings ground wildlife Australia",
                "wombat"));
    }
}
