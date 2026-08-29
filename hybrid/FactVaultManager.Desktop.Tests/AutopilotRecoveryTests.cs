using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotRecoveryTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(5, 120)]
    [InlineData(9, 360)]
    public void DelayForFailure_BacksOffWithoutExceedingSixHours(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), AutopilotRecoveryPolicy.DelayForFailure(failures));
    }

    [Fact]
    public void RecordFailure_TransientFailureEntersRecoveringState()
    {
        var now = new DateTime(2026, 8, 29, 6, 30, 0, DateTimeKind.Utc);
        var subsystem = new AutopilotRecoverySubsystem { Name = "Tracker" };

        AutopilotRecoveryPolicy.RecordFailure(subsystem, now, "The connection timed out.");

        Assert.Equal("Recovering", subsystem.State);
        Assert.Equal(1, subsystem.ConsecutiveFailures);
        Assert.Equal(now.AddMinutes(5), subsystem.NextRetryUtc);
        Assert.False(AutopilotRecoveryPolicy.ShouldAttempt(subsystem, now.AddMinutes(4)));
        Assert.True(AutopilotRecoveryPolicy.ShouldAttempt(subsystem, now.AddMinutes(5)));
    }

    [Fact]
    public void RecordFailure_ConfigurationProblemNeedsSetup()
    {
        var now = new DateTime(2026, 8, 29, 6, 30, 0, DateTimeKind.Utc);
        var subsystem = new AutopilotRecoverySubsystem { Name = "YouTube" };

        AutopilotRecoveryPolicy.RecordFailure(subsystem, now, "Connect YouTube in Settings first.");

        Assert.Equal("Needs setup", subsystem.State);
        Assert.Equal(now.AddHours(6), subsystem.NextRetryUtc);
    }

    [Fact]
    public void RecordSuccess_ClearsRecoveryState()
    {
        var subsystem = new AutopilotRecoverySubsystem
        {
            Name = "Website",
            State = "Recovering",
            ConsecutiveFailures = 4,
            LastError = "offline",
            NextRetryUtc = DateTime.UtcNow.AddHours(1),
        };

        AutopilotRecoveryPolicy.RecordSuccess(subsystem, DateTime.UtcNow);

        Assert.Equal("Healthy", subsystem.State);
        Assert.Equal(0, subsystem.ConsecutiveFailures);
        Assert.Equal("", subsystem.LastError);
        Assert.Null(subsystem.NextRetryUtc);
    }

    [Fact]
    public void OverallStatus_PrioritizesSetupThenRecoveryThenHealthy()
    {
        var state = new AutopilotRecoveryState
        {
            Subsystems =
            [
                new AutopilotRecoverySubsystem { Name = "YouTube", State = "Healthy" },
                new AutopilotRecoverySubsystem { Name = "Tracker", State = "Recovering" },
            ],
        };
        Assert.Equal("Recovering", AutopilotRecoveryPolicy.OverallStatus(state));

        state.Subsystems.Add(new AutopilotRecoverySubsystem { Name = "Facebook", State = "Needs setup" });
        Assert.Equal("Needs setup", AutopilotRecoveryPolicy.OverallStatus(state));

        state.Subsystems.RemoveAll(item => item.State != "Healthy");
        Assert.Equal("Healthy", AutopilotRecoveryPolicy.OverallStatus(state));
    }

    [Fact]
    public void Build46Source_UsesQuietFiveMinuteRecoverySupervisor()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotRecovery.cs");
        Assert.Contains("Interval = TimeSpan.FromMinutes(5)", source, StringComparison.Ordinal);
        Assert.Contains("RunAutopilotRecoveryPassAsync", source, StringComparison.Ordinal);
        Assert.Contains("FactburstLinkTrackerClient", source, StringComparison.Ordinal);
        Assert.Contains("FactburstWebsitePublishingClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox.Show", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
