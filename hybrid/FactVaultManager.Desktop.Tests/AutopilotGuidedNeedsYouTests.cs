using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class AutopilotGuidedNeedsYouTests
{
    [Fact]
    public void MainWindow_ExposesGuidedNeedsYouInitializer()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            nameof(MainShellWindow.InitializeAutopilotGuidedNeedsYou),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void MainWindow_HasSingleNextTaskEntryPoint()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "StartNextGuidedAutopilotTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void MainWindow_HasGuidedNeedsYouWindowPass()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "ApplyGuidedNeedsYouWindow",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
