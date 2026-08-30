namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotMasterSwitchTests
{
    [Fact]
    public void Build79_UsesOneVisibleMasterAutopilotSwitch()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var schedule = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotScheduleTarget.cs");
        var guard = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotHomeLaunchGuard.cs");

        Assert.Contains("CurrentBuildNumber = 79", buildInfo, StringComparison.Ordinal);
        Assert.Contains("Content = preferences.AutoFillEnabled ? \"AUTOPILOT ON\" : \"AUTOPILOT OFF\"", schedule, StringComparison.Ordinal);
        Assert.Contains("ApplyAutopilotMasterState(true)", schedule, StringComparison.Ordinal);
        Assert.Contains("ApplyAutopilotMasterState(false)", schedule, StringComparison.Ordinal);
        Assert.Contains("await EvaluateAutomaticScheduleFillAsync()", schedule, StringComparison.Ordinal);
        Assert.Contains("await RunFullAutopilotAsync()", schedule, StringComparison.Ordinal);
        Assert.Contains("Fill schedule now", guard, StringComparison.Ordinal);
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
