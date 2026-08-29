using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class AutopilotRecoverySubsystem
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public string LastError { get; set; } = "";
}

public sealed class AutopilotRecoveryState
{
    public DateTime UpdatedAtUtc { get; set; }
    public List<AutopilotRecoverySubsystem> Subsystems { get; set; } = [];
}

public static class AutopilotRecoveryPolicy
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    public static TimeSpan DelayForFailure(int consecutiveFailures)
    {
        var index = Math.Clamp(consecutiveFailures - 1, 0, RetryDelays.Length - 1);
        return RetryDelays[index];
    }

    public static bool ShouldAttempt(AutopilotRecoverySubsystem subsystem, DateTime utcNow) =>
        subsystem.NextRetryUtc is null || subsystem.NextRetryUtc <= utcNow;

    public static bool IsConfigurationError(string? message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0) return false;
        return text.Contains("Connect YouTube", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("access token", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("approved account", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("does not match the approved", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Configure Settings", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase);
    }

    public static string OverallStatus(AutopilotRecoveryState state)
    {
        var configured = state.Subsystems.Where(item => !string.Equals(item.State, "Not configured", StringComparison.OrdinalIgnoreCase)).ToList();
        if (configured.Any(item => string.Equals(item.State, "Needs setup", StringComparison.OrdinalIgnoreCase)))
            return "Needs setup";
        if (configured.Any(item => string.Equals(item.State, "Recovering", StringComparison.OrdinalIgnoreCase)))
            return "Recovering";
        if (configured.Count > 0 && configured.All(item => string.Equals(item.State, "Healthy", StringComparison.OrdinalIgnoreCase)))
            return "Healthy";
        return configured.Count == 0 ? "Needs setup" : "Checking";
    }

    public static AutopilotRecoverySubsystem GetOrCreate(AutopilotRecoveryState state, string name)
    {
        var item = state.Subsystems.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is not null) return item;
        item = new AutopilotRecoverySubsystem { Name = name };
        state.Subsystems.Add(item);
        return item;
    }

    public static void RecordSuccess(AutopilotRecoverySubsystem subsystem, DateTime utcNow)
    {
        subsystem.State = "Healthy";
        subsystem.ConsecutiveFailures = 0;
        subsystem.LastSuccessUtc = utcNow;
        subsystem.NextRetryUtc = null;
        subsystem.LastError = "";
    }

    public static void RecordFailure(AutopilotRecoverySubsystem subsystem, DateTime utcNow, string error)
    {
        subsystem.ConsecutiveFailures = Math.Max(0, subsystem.ConsecutiveFailures) + 1;
        subsystem.LastFailureUtc = utcNow;
        subsystem.LastError = (error ?? "").Trim();
        if (IsConfigurationError(subsystem.LastError))
        {
            subsystem.State = "Needs setup";
            subsystem.NextRetryUtc = utcNow.AddHours(6);
        }
        else
        {
            subsystem.State = "Recovering";
            subsystem.NextRetryUtc = utcNow.Add(DelayForFailure(subsystem.ConsecutiveFailures));
        }
    }

    public static void RecordNotConfigured(AutopilotRecoverySubsystem subsystem)
    {
        subsystem.State = "Not configured";
        subsystem.ConsecutiveFailures = 0;
        subsystem.NextRetryUtc = null;
        subsystem.LastError = "";
    }
}

public static class AutopilotRecoveryStateStore
{
    public static string PathFor(string settingsPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(settingsPath))
            ?? throw new InvalidOperationException("The settings folder could not be resolved.");
        return Path.Combine(folder, "autopilot-recovery.json");
    }

    public static AutopilotRecoveryState Load(string settingsPath)
    {
        var path = PathFor(settingsPath);
        if (!File.Exists(path)) return new AutopilotRecoveryState();
        try
        {
            return JsonSerializer.Deserialize<AutopilotRecoveryState>(File.ReadAllText(path))
                   ?? new AutopilotRecoveryState();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine("Could not load Autopilot recovery state: " + error.Message);
            return new AutopilotRecoveryState();
        }
    }

    public static void Save(string settingsPath, AutopilotRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = PathFor(settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        state.UpdatedAtUtc = DateTime.UtcNow;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
