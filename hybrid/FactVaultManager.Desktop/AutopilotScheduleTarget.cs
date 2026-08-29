using System.Globalization;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class AutopilotSchedulePreferences
{
    public int TargetDays { get; set; } = 14;
    public bool AutoFillEnabled { get; set; }
    public DateTime? LastAutomaticFillUtc { get; set; }
}

public static class AutopilotScheduleTargetPlanner
{
    public static readonly int[] AllowedTargets = [7, 14, 21, 30];

    public static int NormalizeTargetDays(int value) =>
        AllowedTargets.Contains(value) ? value : 14;

    public static int MissingScheduleDays(
        IEnumerable<QuizHistorySummary> history,
        int targetDays,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        targetDays = NormalizeTargetDays(targetDays);
        var start = now.LocalDateTime.Date.AddDays(1);
        var desired = Enumerable.Range(0, targetDays)
            .Select(offset => start.AddDays(offset))
            .ToHashSet();

        foreach (var item in history)
        {
            if (!string.Equals(item.VideoType, "Video", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(item.YouTubeScheduledFor))
                continue;
            if (!DateTimeOffset.TryParse(
                    item.YouTubeScheduledFor,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var scheduled))
                continue;
            desired.Remove(scheduled.LocalDateTime.Date);
        }
        return desired.Count;
    }

    public static int BatchSizeForMissingDays(int missingDays)
    {
        if (missingDays <= 0) return 0;
        // The existing proven batch renderer intentionally starts at two items.
        return Math.Clamp(missingDays == 1 ? 2 : missingDays, 2, 20);
    }

    public static bool ShouldAutoFill(
        AutopilotSchedulePreferences preferences,
        int missingDays,
        bool productionBusy,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.AutoFillEnabled || productionBusy || missingDays < 2)
            return false;
        if (preferences.LastAutomaticFillUtc is { } last && utcNow - last < TimeSpan.FromMinutes(20))
            return false;
        return true;
    }
}

public static class AutopilotSchedulePreferencesStore
{
    public static string PathFor(string settingsPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(settingsPath))
            ?? throw new InvalidOperationException("The settings folder could not be resolved.");
        return Path.Combine(folder, "autopilot-preferences.json");
    }

    public static AutopilotSchedulePreferences Load(string settingsPath)
    {
        var path = PathFor(settingsPath);
        if (!File.Exists(path)) return new AutopilotSchedulePreferences();
        try
        {
            var preferences = JsonSerializer.Deserialize<AutopilotSchedulePreferences>(File.ReadAllText(path))
                              ?? new AutopilotSchedulePreferences();
            preferences.TargetDays = AutopilotScheduleTargetPlanner.NormalizeTargetDays(preferences.TargetDays);
            return preferences;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine("Could not load Autopilot schedule preferences: " + error.Message);
            return new AutopilotSchedulePreferences();
        }
    }

    public static void Save(string settingsPath, AutopilotSchedulePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.TargetDays = AutopilotScheduleTargetPlanner.NormalizeTargetDays(preferences.TargetDays);
        var path = PathFor(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}

public static class AutopilotBatchCountRequest
{
    private static readonly object Gate = new();
    private static int _count;
    private static DateTime _expiresUtc;

    public static void Arm(int count)
    {
        if (count is < 2 or > 20) throw new ArgumentOutOfRangeException(nameof(count));
        lock (Gate)
        {
            _count = count;
            _expiresUtc = DateTime.UtcNow.AddMinutes(2);
        }
    }

    public static bool TryConsume(out int count)
    {
        lock (Gate)
        {
            count = 0;
            if (_count == 0 || DateTime.UtcNow > _expiresUtc)
            {
                _count = 0;
                return false;
            }
            count = _count;
            _count = 0;
            return true;
        }
    }
}

public static class AutopilotTrustedPublishingPreflight
{
    private static readonly object Gate = new();
    private static DateTime _expiresUtc;
    private static bool _armed;

    public static void Arm()
    {
        lock (Gate)
        {
            _armed = true;
            _expiresUtc = DateTime.UtcNow.AddHours(8);
        }
    }

    public static bool TryConsume()
    {
        lock (Gate)
        {
            var active = _armed && DateTime.UtcNow <= _expiresUtc;
            _armed = false;
            return active;
        }
    }
}
