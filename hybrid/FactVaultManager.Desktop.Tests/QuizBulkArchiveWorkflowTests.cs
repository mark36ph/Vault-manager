using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizBulkArchiveWorkflowTests
{
    [Fact]
    public void ArchiveAction_IncludesJournaledLinkRecovery()
    {
        Assert.Contains(
            QuizCompletedCArchiveAction.RestoreJournaledArchiveLink,
            Enum.GetValues<QuizCompletedCArchiveAction>());
    }

    [Fact]
    public void Preview_ReportsJournaledLinkRecoverySeparately()
    {
        var property = typeof(QuizCompletedCArchivePreview).GetProperty(nameof(QuizCompletedCArchivePreview.RestoreArchivedLinks));
        Assert.NotNull(property);
        Assert.Equal(typeof(int), property.PropertyType);
    }

    [Fact]
    public void DataService_ExposesJournalReconciliation()
    {
        var method = typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.ReconcileJournaledQuizArchivePaths),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(QuizArchiveJournalReconciliationResult), method.ReturnType);
    }

    [Fact]
    public void BulkArchive_CanUseConfirmedPreviewWithLiveProgress()
    {
        var method = typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.ArchiveCompletedCQuizProjects),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types:
            [
                typeof(QuizCompletedCArchivePreview),
                typeof(IProgress<QuizCompletedCArchiveProgress>),
            ],
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(QuizCompletedCArchiveApplyResult), method.ReturnType);
    }

    [Fact]
    public void Progress_ContainsCurrentQuizAndPaths()
    {
        var progress = new QuizCompletedCArchiveProgress(
            4,
            12,
            43,
            "General Knowledge Quiz #002 (Video)",
            "Copying C: project to Z:",
            @"C:\projects\General Knowledge Quiz - 002",
            @"Z:\FactVaultManager\Quizzes\General Knowledge Quiz - 002");

        Assert.Equal(4, progress.Current);
        Assert.Equal(12, progress.Total);
        Assert.Equal(43, progress.HistoryId);
        Assert.Contains("Copying", progress.Stage);
        Assert.StartsWith("C:", progress.SourceFolder, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Z:", progress.DestinationFolder, StringComparison.OrdinalIgnoreCase);
    }
}
