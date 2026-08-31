using System.Reflection;
using FactVaultManager.Desktop;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizGroupedBulkArchiveTests
{
    [Fact]
    public void GroupedArchiveItem_TracksAllLinkedHistoryRows()
    {
        var item = new QuizGroupedCArchiveItem(
            @"C:\projects\Space - 001",
            "Space - 001",
            new[] { 70, 92, 103 },
            @"Z:\FactVaultManager\Quizzes\Space - 001",
            QuizGroupedCArchiveAction.CopyToArchive);

        Assert.Equal(70, item.HistoryId);
        Assert.Equal(3, item.HistoryRowCount);
        Assert.Equal(new[] { 70, 92, 103 }, item.HistoryIds);
    }

    [Fact]
    public void Preview_SeparatesPhysicalFolderAndHistoryRowCounts()
    {
        var type = typeof(QuizGroupedCArchivePreview);

        Assert.Equal(typeof(int), type.GetProperty(nameof(QuizGroupedCArchivePreview.ExistingPhysicalFolders))!.PropertyType);
        Assert.Equal(typeof(int), type.GetProperty(nameof(QuizGroupedCArchivePreview.ExistingHistoryRows))!.PropertyType);
        Assert.Equal(typeof(int), type.GetProperty(nameof(QuizGroupedCArchivePreview.ReadyPhysicalFolders))!.PropertyType);
        Assert.Equal(typeof(int), type.GetProperty(nameof(QuizGroupedCArchivePreview.ReadyHistoryRows))!.PropertyType);
    }

    [Fact]
    public void DataService_ExposesConfirmedGroupedArchiveWithProgress()
    {
        var preview = typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.PreviewGroupedCompletedCQuizProjects),
            BindingFlags.Instance | BindingFlags.Public);
        var apply = typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.ArchiveGroupedCompletedCQuizProjects),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types:
            [
                typeof(QuizGroupedCArchivePreview),
                typeof(IProgress<QuizGroupedCArchiveProgress>),
            ],
            modifiers: null);

        Assert.NotNull(preview);
        Assert.Equal(typeof(QuizGroupedCArchivePreview), preview.ReturnType);
        Assert.NotNull(apply);
        Assert.Equal(typeof(QuizGroupedCArchiveApplyResult), apply.ReturnType);
    }

    [Fact]
    public void Progress_ReportsOnePhysicalFolderAndItsHistoryRowCount()
    {
        var progress = new QuizGroupedCArchiveProgress(
            2,
            3,
            71,
            "Technology - 001",
            4,
            "Updating 4 Quiz History row(s) atomically to Z:",
            @"C:\projects\Technology - 001",
            @"Z:\FactVaultManager\Quizzes\Technology - 001");

        Assert.Equal(2, progress.Current);
        Assert.Equal(3, progress.Total);
        Assert.Equal(4, progress.HistoryRowCount);
        Assert.Contains("atomically", progress.Stage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_ExposesGroupedArchiveUiInitializer()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            nameof(MainShellWindow.InitializeQuizHistoryGroupedBulkArchiveUi),
            BindingFlags.Instance | BindingFlags.Public));
    }
}
