namespace FactVaultManager.Desktop.Tests;

public sealed class ProductionProgressEstimatorTests
{
    [Fact]
    public void Calculate_NeverMovesBackward()
    {
        var progress = ProductionProgressEstimator.Calculate("research", 0.9, 0);
        var afterLowerStageValue = ProductionProgressEstimator.Calculate("research", 0.2, progress);
        var afterNextStageStarts = ProductionProgressEstimator.Calculate("facts", 0, afterLowerStageValue);

        Assert.Equal(progress, afterLowerStageValue);
        Assert.True(afterNextStageStarts >= afterLowerStageValue);
    }

    [Fact]
    public void Calculate_TracksWholeWorkflowFromZeroToOneHundred()
    {
        Assert.Equal(0, ProductionProgressEstimator.Calculate("research", 0, 0));
        Assert.Equal(23, ProductionProgressEstimator.Calculate("image_prompts", 0, 0));
        Assert.Equal(83, ProductionProgressEstimator.Calculate("image_prompts", 1, 0));
        Assert.Equal(100, ProductionProgressEstimator.Calculate("resolve", 1, 0));
    }

    [Fact]
    public void Calculate_UnknownStagePreservesCurrentProgress()
    {
        Assert.Equal(42, ProductionProgressEstimator.Calculate("unknown", 0.5, 42));
    }
}
