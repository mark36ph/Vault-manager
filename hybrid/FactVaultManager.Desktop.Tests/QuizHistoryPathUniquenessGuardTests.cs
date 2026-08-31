using System.Reflection;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizHistoryPathUniquenessGuardTests
{
    [Fact]
    public void DesktopDataService_ExposesPathUniquenessGuard()
    {
        Assert.NotNull(typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.EnsureQuizHistoryProjectFolderUniquenessGuard),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void MainWindow_RegistersPathUniquenessGuardAtLoad()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "MainShellWindowQuizHistoryPathUniqueness_Loaded",
            BindingFlags.Static | BindingFlags.NonPublic));
    }
}
