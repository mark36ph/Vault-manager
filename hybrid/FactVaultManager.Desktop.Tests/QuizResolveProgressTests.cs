namespace FactVaultManager.Desktop.Tests;

public sealed class QuizResolveProgressTests
{
    [Fact]
    public void Estimate_StartsAtFivePercent_AndCapsBelowCompletion()
    {
        Assert.Equal(5, QuizResolveProgressEstimator.Estimate(TimeSpan.Zero));
        Assert.Equal(QuizResolveProgressEstimator.RunningCapPercent, QuizResolveProgressEstimator.Estimate(TimeSpan.FromMinutes(20)));
        Assert.True(QuizResolveProgressEstimator.Estimate(TimeSpan.FromMinutes(20)) < 100);
    }

    [Fact]
    public void Estimate_IncreasesMonotonicallyWhileExportRuns()
    {
        var samples = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5),
        };

        var values = samples.Select(QuizResolveProgressEstimator.Estimate).ToArray();

        Assert.True(values.Zip(values.Skip(1), (left, right) => right >= left).All(increasing => increasing));
    }

    [Theory]
    [InlineData(5, "Preparing quiz export…")]
    [InlineData(30, "Processing quiz media…")]
    [InlineData(60, "Rendering and packaging…")]
    [InlineData(90, "Finalizing Resolve package…")]
    [InlineData(100, "Quiz export complete")]
    public void StageFor_DescribesCurrentProgress(int percent, string expected)
    {
        Assert.Equal(expected, QuizResolveProgressEstimator.StageFor(percent));
    }
}
