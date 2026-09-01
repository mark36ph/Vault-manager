namespace FactVaultManager.Desktop.Tests;

public sealed class YouTubeApiResponsivenessTests
{
    [Fact]
    public void Build139_UploadManagerFilesystemSnapshotRunsOffUiThread()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.UploadManagerPage.cs");

        Assert.Contains("_uploadManagerRefreshRunning", source, StringComparison.Ordinal);
        Assert.Contains("var snapshot = await Task.Run(BuildUploadManagerSnapshot);", source, StringComparison.Ordinal);
        Assert.Contains("private UploadManagerSnapshot BuildUploadManagerSnapshot()", source, StringComparison.Ordinal);
        Assert.Contains("QuizPromoShortUploadState.Display(item.ProjectFolder)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Build139_UploadManagerEntryPointDoesNotProbeProjectFoldersSynchronously()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.UploadManagerPage.cs");
        var start = source.IndexOf("private void RefreshUploadManager()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RefreshUploadManagerAsync()", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var entryPoint = source[start..end];
        Assert.Contains("_ = RefreshUploadManagerAsync();", entryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("_data.GetQuizHistory()", entryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("QuizPromoShortUploadState.Display", entryPoint, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
