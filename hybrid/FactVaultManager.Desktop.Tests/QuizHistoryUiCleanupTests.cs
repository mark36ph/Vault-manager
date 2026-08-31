using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizHistoryUiCleanupTests
{
    [Fact]
    public void MainWindow_ExposesQuizHistoryUiCleanupInitializer()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            nameof(MainShellWindow.InitializeQuizHistoryUiCleanup),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void MainWindow_HasQuizHistoryUiCleanupPass()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "ApplyQuizHistoryUiCleanup",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
