namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBatchRenderStaTests
{
    [Fact]
    public void BatchRender_BuildsNativeQuizVideoOnDedicatedStaRunner()
    {
        var source = ReadRepositoryFile("hybrid/FactVaultManager.Desktop/MainShellWindow.QuizBatchRender.cs");

        Assert.Contains(
            "var rendered = await QuizRenderStaRunner.RunAsync(() => new NativeQuizVideoBuilder().BuildAndExport(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "var rendered = await Task.Run(() => new NativeQuizVideoBuilder().BuildAndExport(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await QuizRenderStaRunner.RunAsync(() => new QuizThemedCardRenderer().OverwriteCards(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var result = await QuizRenderStaRunner.RunAsync(() => QuizVisualExportRewriter.ReExport(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "return QuizVisualExportRewriter.ReExport(augmented, exportQuestions, options);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var thumbnailPath = await QuizRenderStaRunner.RunAsync(() => new QuizThumbnailRenderer().Write(",
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