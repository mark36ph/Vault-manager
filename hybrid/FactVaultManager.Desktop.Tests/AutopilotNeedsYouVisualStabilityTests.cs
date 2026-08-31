using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotNeedsYouVisualStabilityTests
{
    [Theory]
    [InlineData(false, 0, "Healthy")]
    [InlineData(true, 0, "Working")]
    [InlineData(false, 3, "Needs you")]
    [InlineData(true, 3, "Needs you")]
    public void ResolveHealth_KeepsNeedsYouStableWhileManualTasksExist(bool running, int pendingTasks, string expected)
    {
        Assert.Equal(expected, AutopilotNeedsYouVisualStability.ResolveHealth(running, pendingTasks));
    }

    [Theory]
    [InlineData("Nothing", 0)]
    [InlineData("", 0)]
    [InlineData("1", 1)]
    [InlineData("12", 12)]
    public void ParsePendingCount_HandlesHomeCounterText(string value, int expected)
    {
        Assert.Equal(expected, AutopilotNeedsYouVisualStability.ParsePendingCount(value));
    }

    [Fact]
    public void Build116Source_WiresNeedsYouVisualStabilityGuard()
    {
        var buildInfo = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        Assert.Contains("InitializeAutopilotNeedsYouVisualStability", buildInfo, StringComparison.Ordinal);

        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.AutopilotNeedsYouVisualStability.cs");
        Assert.Contains("pendingTasks > 0 ? \"Needs you\"", source, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty", source, StringComparison.Ordinal);
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
