using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotFirstUiTests
{
    [Theory]
    [InlineData(false, 0, "Healthy")]
    [InlineData(true, 0, "Working")]
    [InlineData(false, 2, "Needs you")]
    public void Health_UsesSimpleAutopilotStates(bool running, int manualTasks, string expected)
    {
        Assert.Equal(expected, AutopilotHomePlanner.Health(running, manualTasks));
    }

    [Fact]
    public void ScheduleCoverageDays_CountsThroughLatestFutureRelease()
    {
        var now = new DateTimeOffset(2026, 8, 29, 6, 0, 0, TimeSpan.FromHours(1));
        var releases = new[]
        {
            now.AddHours(3),
            now.AddDays(3).AddHours(3),
            now.AddDays(6).AddHours(3),
        };

        Assert.Equal(7, AutopilotHomePlanner.ScheduleCoverageDays(releases, now));
    }

    [Fact]
    public void ScheduleCoverageDays_ReturnsZeroWhenNothingFutureIsQueued()
    {
        var now = new DateTimeOffset(2026, 8, 29, 6, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(0, AutopilotHomePlanner.ScheduleCoverageDays(new[] { now.AddDays(-1) }, now));
    }

    [Theory]
    [InlineData("Ready", false)]
    [InlineData("N/A", false)]
    [InlineData("Scheduled", false)]
    [InlineData("Automatic", false)]
    [InlineData("Missing", true)]
    [InlineData("Needs attention", true)]
    [InlineData("Manual", true)]
    public void NeedsManualValue_OnlyFlagsOutstandingManualStates(string value, bool expected)
    {
        Assert.Equal(expected, AutopilotHomePlanner.NeedsManualValue(value));
    }

    [Fact]
    public void Build45Source_ContainsCompactDailyNavigationAndAdvancedFallback()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotFirstUi.cs");
        Assert.Contains("Factburst Autopilot", source, StringComparison.Ordinal);
        Assert.Contains("Generate + Fill Schedule", source, StringComparison.Ordinal);
        Assert.Contains("✦   Autopilot", source, StringComparison.Ordinal);
        Assert.Contains("+   Create", source, StringComparison.Ordinal);
        Assert.Contains("↗   Performance", source, StringComparison.Ordinal);
        Assert.Contains("▤   Library", source, StringComparison.Ordinal);
        Assert.Contains("⋯   Advanced", source, StringComparison.Ordinal);
        Assert.Contains("The detailed tools stay out of the way unless you need them", source, StringComparison.Ordinal);
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
