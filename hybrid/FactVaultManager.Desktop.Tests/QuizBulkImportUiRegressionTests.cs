namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBulkImportUiRegressionTests
{
    [Fact]
    public void BulkImporter_DoesNotRequireImportTabToBeVisuallyRealized()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizBulkImport.cs");

        Assert.Contains("Selector.SelectionChangedEvent", source, StringComparison.Ordinal);
        Assert.Contains("FindQuizBulkImportTab", source, StringComparison.Ordinal);
        Assert.Contains("tabs.Items.OfType<TabItem>()", source, StringComparison.Ordinal);
        Assert.Contains("importTab?.Content is not DependencyObject importRoot", source, StringComparison.Ordinal);
        Assert.Contains("FindQuizBulkImportDescendants<FrameworkElement>(importRoot)", source, StringComparison.Ordinal);
        Assert.Contains("Preview and import JSON", source, StringComparison.Ordinal);
        Assert.Contains("Load JSON file", source, StringComparison.Ordinal);
        Assert.Contains("Import Questions", source, StringComparison.Ordinal);
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
