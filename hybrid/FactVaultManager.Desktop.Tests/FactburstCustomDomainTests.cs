using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class FactburstCustomDomainTests
{
    [Fact]
    public void PreferredBaseUrl_UsesCustomDomainWhenEmpty()
    {
        Assert.Equal(
            "https://go.factburstquiz.com",
            FactburstTrackerSettingsStore.PreferredBaseUrl(""));
    }

    [Theory]
    [InlineData("https://go.factburstquiz.workers.dev")]
    [InlineData("https://go.factburstquiz.workers.dev/")]
    [InlineData(" HTTPS://GO.FACTBURSTQUIZ.WORKERS.DEV/ ")]
    public void PreferredBaseUrl_MigratesLegacyWorkersDomain(string legacy)
    {
        Assert.Equal(
            "https://go.factburstquiz.com",
            FactburstTrackerSettingsStore.PreferredBaseUrl(legacy));
    }

    [Fact]
    public void PreferredBaseUrl_PreservesExplicitCustomTracker()
    {
        Assert.Equal(
            "https://tracker.example.com",
            FactburstTrackerSettingsStore.PreferredBaseUrl(" https://tracker.example.com/ "));
    }
}
