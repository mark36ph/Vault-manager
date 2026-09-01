using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class AutopilotSchedulePreferences
{
    // Kept as TargetDays for settings-file compatibility. The value now represents the
    // minimum number of future full quizzes Autopilot keeps scheduled.
    public int TargetDays { get; set; } = 14;
    public bool AutoFillEnabled { get; set; }
    public DateTime? LastAutomaticFillUtc { get; set; }
}

public static class AutopilotScheduleTargetPlanner
{
    public static readonly int[] AllowedTargets = [7, 14, 21, 30];

    public static int NormalizeTargetDays(int value) =>
        AllowedTargets.Contains(value) ? value : 14;

    public static int ScheduledQuizCount(
        IEnumerable<QuizHistorySummary> history,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.Count(item =>
            item.PublishedOnYouTube &&
            string.Equals(item.VideoType, "Video", StringComparison.Ordinal) &&
            ScheduledReleaseReadinessPlanner.TryFutureSchedule(item.YouTubeScheduledFor, now, out _));
    }

    public static int MissingScheduledQuizzes(
        IEnumerable<QuizHistorySummary> history,
        int targetCount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        targetCount = NormalizeTargetDays(targetCount);
        return Math.Max(0, targetCount - ScheduledQuizCount(history, now));
    }

    // Compatibility for older callers/settings-era tests. Schedule refill is now based on
    // future scheduled quiz inventory rather than whether individual calendar dates happen
    // to be occupied.
    public static int MissingScheduleDays(
        IEnumerable<QuizHistorySummary> history,
        int targetDays,
        DateTimeOffset now) =>
        MissingScheduledQuizzes(history, targetDays, now);

    public static int BatchSizeForMissingDays(int missingDays)
    {
        if (missingDays <= 0) return 0;
        // The existing proven batch renderer and trusted publishing preflight intentionally
        // start at two items. A target therefore means "keep at least N scheduled".
        return Math.Clamp(missingDays == 1 ? 2 : missingDays, 2, 20);
    }

    public static bool ShouldAutoFill(
        AutopilotSchedulePreferences preferences,
        int missingCount,
        bool productionBusy,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.AutoFillEnabled || productionBusy || missingCount <= 0)
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

    public static void Cancel()
    {
        lock (Gate)
        {
            _count = 0;
            _expiresUtc = default;
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

    public static void Cancel()
    {
        lock (Gate)
        {
            _armed = false;
            _expiresUtc = default;
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
