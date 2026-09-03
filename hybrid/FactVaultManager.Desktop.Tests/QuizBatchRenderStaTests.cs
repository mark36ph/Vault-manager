namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBatchRenderStaTests
{
    [Fact]
    public void BatchRender_BuildsNativeQuizVideoOnDesktopDispatcher()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizBatchRender.cs");

        Assert.Contains(
            "var rendered = await Dispatcher.InvokeAsync(() => new NativeQuizVideoBuilder().BuildAndExport(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "var rendered = await Task.Run(() => new NativeQuizVideoBuilder().BuildAndExport(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Dispatcher.InvokeAsync(() => new QuizThemedCardRenderer().OverwriteCards(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Dispatcher.InvokeAsync(() => QuizVisualExportRewriter.ReExport(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "return QuizVisualExportRewriter.ReExport(augmented, exportQuestions, options);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Dispatcher.InvokeAsync(() => new QuizThumbnailRenderer().Write(",
            source,
            StringComparison.Ordinal);
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