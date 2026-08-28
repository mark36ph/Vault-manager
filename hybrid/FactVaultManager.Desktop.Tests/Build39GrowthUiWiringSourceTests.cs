using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class Build39GrowthUiWiringSourceTests
{
    [Fact]
    public void BuildNumber_Is39OrLater()
    {
        Assert.True(MainShellWindow.CurrentBuildNumber >= 39);
    }

    [Fact]
    public void ReliableInitializer_IsPresentOnMainWindow()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "InitializeYouTubeGrowthAnalyticsUiReliably",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }
}
