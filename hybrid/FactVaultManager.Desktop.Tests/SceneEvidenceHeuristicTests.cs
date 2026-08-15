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
}
