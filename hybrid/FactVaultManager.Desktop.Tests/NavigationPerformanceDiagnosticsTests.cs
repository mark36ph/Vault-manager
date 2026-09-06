using Xunit;

namespace FactVaultManager.Desktop.Tests;

public sealed class NavigationPerformanceDiagnosticsTests
{
    [Fact]
    public void Build202_NavigationHotspotProfileWaitsForStableWpfLayout()
    {
        var profiler = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationHotspotProfiler.cs");

        Assert.Contains("NavigateAndWaitForStableLayoutAsync", profiler, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", profiler, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateAndWaitAsync(button)", profiler, StringComparison.Ordinal);
    }

    [Fact]
    public void Build203_QuizNotesPageIsRemovedFromShell()
    {
        var notesPage = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizNotesPage.cs");
        var navigation = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.NavigationSections.cs");

        Assert.Contains("private int _quizNotesTabIndex => _quizHistoryTabIndex;", notesPage, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildQuizNotesPage", notesPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AddQuizNotesNavigationButton", notesPage, StringComparison.Ordinal);
        Assert.DoesNotContain("navigation.Children.Add(quizNotes)", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("var quizNotes =", navigation, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
