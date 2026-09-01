namespace FactVaultManager.Desktop.Tests;

public sealed class ApiConnectionsWebsiteTests
{
    [Fact]
    public void Build142_UnifiedApiPageIncludesWebsiteTrackerConnection()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsWebsite.cs");

        Assert.Contains("Website & Link Tracker", source, StringComparison.Ordinal);
        Assert.Contains("TRACKER_API_KEY", source, StringComparison.Ordinal);
        Assert.Contains("FactburstTrackerSettingsStore.Load(_data.SettingsPath)", source, StringComparison.Ordinal);
        Assert.Contains("FactburstTrackerSettingsStore.Save(", source, StringComparison.Ordinal);
        Assert.Contains("FactburstTrackerSettingsStore.DefaultBaseUrl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build142_WebsiteTestChecksHealthAndAuthenticatedStats()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.ApiConnectionsWebsite.cs");

        Assert.Contains("TestWebsiteConnectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("await client.HealthAsync(baseUrl)", source, StringComparison.Ordinal);
        Assert.Contains("await client.FetchStatsAsync(baseUrl, apiKey)", source, StringComparison.Ordinal);
        Assert.Contains("\"website\",", source, StringComparison.Ordinal);
        Assert.Contains("Save API & website settings", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build142_StartupStillInitializesWebsiteApiConnection()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");

        Assert.Contains("window.InitializeApiConnectionsWebsite();", source, StringComparison.Ordinal);
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
