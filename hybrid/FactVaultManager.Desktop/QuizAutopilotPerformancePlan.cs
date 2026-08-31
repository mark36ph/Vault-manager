namespace FactVaultManager.Desktop;

public static class QuizAutopilotPerformancePlan
{
    public static string CategoryForSlot(
        IReadOnlyList<string>? performancePlan,
        int zeroBasedSlot,
        string? fallbackCategory)
    {
        if (performancePlan is not null &&
            zeroBasedSlot >= 0 &&
            zeroBasedSlot < performancePlan.Count)
        {
            var recommended = (performancePlan[zeroBasedSlot] ?? "").Trim();
            if (recommended.Length > 0)
                return recommended;
        }

        return (fallbackCategory ?? "").Trim();
    }
}
