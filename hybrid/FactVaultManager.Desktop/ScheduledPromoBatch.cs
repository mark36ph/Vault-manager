using System.Globalization;

namespace FactVaultManager.Desktop;

public static class ScheduledPromoBatchPlanner
{
    public static IReadOnlyList<ScheduledReleaseReadinessRow> SelectMissingPromos(
        IEnumerable<ScheduledReleaseReadinessRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .Where(row => string.Equals(row.Promo, "Missing", StringComparison.Ordinal))
            .OrderBy(row => row.PublishAt)
            .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.HistoryId)
            .ToList();
    }

    public static string Summary(int created, int skipped, int failed)
    {
        created = Math.Max(0, created);
        skipped = Math.Max(0, skipped);
        failed = Math.Max(0, failed);
        return $"Created {created.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Skipped {skipped.ToString("N0", CultureInfo.InvariantCulture)} • " +
               $"Failed {failed.ToString("N0", CultureInfo.InvariantCulture)}";
    }
}
