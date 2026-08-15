namespace FactVaultManager.Desktop.Tests;

public sealed class DesktopDataServiceTests
{
    [Fact]
    public void Constructor_UsesRepositoryDataFolder_WhenRunningFromDevelopmentRepository()
    {
        var service = new DesktopDataService();

        var projectFile = Path.Combine(
            service.RuntimeRoot,
            "hybrid",
            "FactVaultManager.Desktop",
            "FactVaultManager.Desktop.csproj");

        Assert.True(File.Exists(projectFile));
        Assert.Equal(Path.Combine(service.RuntimeRoot, "data", "factvault.db"), service.DatabasePath);
        Assert.Equal(Path.Combine(service.RuntimeRoot, "data", "settings.json"), service.SettingsPath);
    }
}
