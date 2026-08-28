using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class Build39GrowthUiWiringSourceTests
{
    [Fact]
    public void BuildNumber_Is39()
    {
        Assert.Equal(39, MainShellWindow.CurrentBuildNumber);
    }

    [Fact]
    public void ReliableInitializer_IsPresentOnMainWindow()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "InitializeYouTubeGrowthAnalyticsUiReliably",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }
}
