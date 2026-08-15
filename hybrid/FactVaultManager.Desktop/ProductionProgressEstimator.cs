namespace FactVaultManager.Desktop;

public static class ProductionProgressEstimator
{
    private static readonly (string Stage, double Weight)[] Stages =
    [
        ("research", 8),
        ("facts", 7),
        ("script", 8),
        ("image_prompts", 60),
        ("voice", 7),
        ("timeline", 5),
        ("resolve", 5),
    ];

    public static double Calculate(string stage, double stageProgress, double previousPercent)
    {
        var previous = Math.Clamp(previousPercent, 0, 100);
        var index = Array.FindIndex(Stages, item =>
            string.Equals(item.Stage, stage, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return previous;

        var completed = 0.0;
        for (var i = 0; i < index; i++)
            completed += Stages[i].Weight;

        var estimate = completed + Stages[index].Weight * Math.Clamp(stageProgress, 0, 1);
        return Math.Max(previous, Math.Clamp(estimate, 0, 100));
    }
}
