namespace FactVaultManager.Desktop.Tests;

public sealed class LibraryContentLifecycleUiTests
{
    [Fact]
    public void Library_AddsLifecycleColumnsAndWorkflowFilters()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizContentLifecycle.cs");

        Assert.Contains("title.Text = \"Library\";", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Stage\"", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Next action\"", source, StringComparison.Ordinal);
        Assert.Contains("QuizContentLifecycle.Filters", source, StringComparison.Ordinal);
        Assert.Contains("need attention", source, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GetDefaultView", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInfo_InitializesLifecycleAfterQuizHistoryCleanup()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.BuildInfo.cs");
        var cleanup = source.IndexOf("InitializeQuizHistoryUiCleanup();", StringComparison.Ordinal);
        var lifecycle = source.IndexOf("InitializeQuizContentLifecycleUi();", StringComparison.Ordinal);

        Assert.True(cleanup >= 0);
        Assert.True(lifecycle > cleanup);
        Assert.Contains("CurrentBuildNumber = 122", source, StringComparison.Ordinal);
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
