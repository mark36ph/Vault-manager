using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizAutopilotPerformancePlanTests
{
    [Fact]
    public void CategoryForSlot_PerformanceRecommendationBeatsBuilderDefault()
    {
        var plan = new[] { "Science", "Space", "History" };

        var category = QuizAutopilotPerformancePlan.CategoryForSlot(
            plan,
            zeroBasedSlot: 0,
            fallbackCategory: "General Knowledge");

        Assert.Equal("Science", category);
    }

    [Fact]
    public void CategoryForSlot_AdvancesThroughPerformancePlan()
    {
        var plan = new[] { "Science", "Space", "History", "Film" };

        var categories = Enumerable.Range(0, plan.Length)
            .Select(index => QuizAutopilotPerformancePlan.CategoryForSlot(plan, index, "General Knowledge"))
            .ToArray();

        Assert.Equal(plan, categories);
        Assert.True(categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
    }

    [Fact]
    public void CategoryForSlot_UsesCurrentBuilderCategoryOnlyWhenPlanHasNoSlot()
    {
        var category = QuizAutopilotPerformancePlan.CategoryForSlot(
            Array.Empty<string>(),
            zeroBasedSlot: 0,
            fallbackCategory: "Technology");

        Assert.Equal("Technology", category);
    }
}
