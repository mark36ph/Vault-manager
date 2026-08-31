using System.Reflection;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDuplicatePathRepairWiringTests
{
    [Fact]
    public void DesktopDataService_ExposesDuplicatePathPreviewAndApply()
    {
        Assert.NotNull(typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.PreviewDuplicateQuizHistoryProjectFolders),
            BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(typeof(DesktopDataService).GetMethod(
            nameof(DesktopDataService.ApplyDuplicateQuizHistoryProjectFolderRepairs),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void ArchiveCompletedC_HasDuplicateRepairGuard()
    {
        Assert.NotNull(typeof(MainShellWindow).GetMethod(
            "ArchiveCompletedCWithDuplicateRepairAsync",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void DuplicateRepairSuggestion_StoresOneToOneDestination()
    {
        var suggestion = new QuizDuplicatePathRepairSuggestion(
            43,
            "General Knowledge Quiz #002 (Video)",
            @"C:\projects\General Knowledge Quiz - 002",
            @"Z:\FactVaultManager\Quizzes\General Knowledge Quiz - 002",
            QuizArchiveMatchConfidence.High,
            250,
            "episode and metadata match");

        Assert.Equal(43, suggestion.HistoryId);
        Assert.StartsWith("Z:\\", suggestion.ProposedFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QuizArchiveMatchConfidence.High, suggestion.Confidence);
    }
}
