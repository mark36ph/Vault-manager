namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDuplicatePathPartialRepairTests
{
    [Fact]
    public void Planner_RepairsSafeSibling_WhenOtherDuplicateRowHasNoAlternateMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-partial-{Guid.NewGuid():N}");
        try
        {
            var alternate = CreateProjectFolder(root, "Space - 001", "Space Quiz");
            var shared = @"C:\projects\Space - 001";
            var histories = new[]
            {
                History(70, "Space Quiz", "Space Quiz", 1, "16:9", shared),
                History(92, "Space Quiz", "Space Quiz", 2, "16:9", shared),
            };

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                histories,
                [QuizArchiveDeepMatcher.InspectProjectFolder(alternate)]);

            var suggestion = Assert.Single(plan.Suggestions);
            Assert.Equal(70, suggestion.HistoryId);
            Assert.Equal(alternate, suggestion.ProposedFolder);

            var conflict = Assert.Single(plan.Conflicts);
            Assert.Equal([92], conflict.HistoryIds);
            Assert.Contains("No High-confidence alternate", conflict.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Planner_DoesNotAssignOneAlternateFolderToTwoHistoryRows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-one-to-one-{Guid.NewGuid():N}");
        try
        {
            var alternate = CreateProjectFolder(root, "Space - 001", "Space Quiz");
            var shared = @"C:\projects\Space - 001";
            var histories = new[]
            {
                History(70, "Space Quiz", "Space Quiz", 1, "16:9", shared),
                History(170, "Space Quiz", "Space Quiz", 1, "16:9", shared),
            };

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                histories,
                [QuizArchiveDeepMatcher.InspectProjectFolder(alternate)]);

            Assert.Empty(plan.Suggestions);
            Assert.Equal(2, plan.Conflicts.Count);
            Assert.All(plan.Conflicts, conflict =>
                Assert.Contains("also a strong match", conflict.Reason, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Planner_CanRepairMultipleRowsFromSameSharedFolder_ToDistinctAlternates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-distinct-{Guid.NewGuid():N}");
        try
        {
            var space = CreateProjectFolder(root, "Space - 001", "Space Quiz");
            var technology = CreateProjectFolder(root, "Technology - 002", "Technology Quiz");
            var shared = @"C:\projects\corrupt-shared-folder";
            var histories = new[]
            {
                History(70, "Space Quiz", "Space Quiz", 1, "16:9", shared),
                History(94, "Technology Quiz", "Technology Quiz", 2, "16:9", shared),
            };

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                histories,
                [
                    QuizArchiveDeepMatcher.InspectProjectFolder(space),
                    QuizArchiveDeepMatcher.InspectProjectFolder(technology),
                ]);

            Assert.Equal(2, plan.Suggestions.Count);
            Assert.Empty(plan.Conflicts);
            Assert.Equal(2, plan.Suggestions.Select(item => item.ProposedFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(plan.Suggestions, item => item.HistoryId == 70 && item.ProposedFolder == space);
            Assert.Contains(plan.Suggestions, item => item.HistoryId == 94 && item.ProposedFolder == technology);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Planner_RejectsSpaceHistory_WhenFolderIsGeneralKnowledgeWithSameEpisode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-family-reject-{Guid.NewGuid():N}");
        try
        {
            // Deliberately put stale/copied Space metadata inside a General Knowledge project. The
            // folder family must win over matching episode 003 and misleading JSON evidence.
            var wrongFamily = CreateProjectFolder(root, "General Knowledge Quiz - 003", "Space Quiz");
            var history = History(92, "Space Quiz", "Space Quiz", 3, "16:9", @"C:\projects\Space - 001");

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                [history],
                [QuizArchiveDeepMatcher.InspectProjectFolder(wrongFamily)]);

            Assert.Empty(plan.Suggestions);
            var conflict = Assert.Single(plan.Conflicts);
            Assert.Equal([92], conflict.HistoryIds);
            Assert.Contains("No High-confidence alternate", conflict.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Planner_AllowsSpaceHistory_WhenFolderIsSpaceWithSameEpisode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-family-space-{Guid.NewGuid():N}");
        try
        {
            var space = CreateProjectFolder(root, "Space - 003", "Space Quiz");
            var history = History(92, "Space Quiz", "Space Quiz", 3, "16:9", @"C:\projects\Space - 001");

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                [history],
                [QuizArchiveDeepMatcher.InspectProjectFolder(space)]);

            var suggestion = Assert.Single(plan.Suggestions);
            Assert.Equal(92, suggestion.HistoryId);
            Assert.Equal(space, suggestion.ProposedFolder);
            Assert.Empty(plan.Conflicts);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Planner_TreatsIconsFolderAsLogosFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), $"duplicate-family-logos-{Guid.NewGuid():N}");
        try
        {
            var icons = CreateProjectFolder(root, "Icons - 001", "Logos Quiz");
            var history = History(83, "Logos Quiz", "Logos Quiz", 1, "16:9", @"C:\projects\Logos - 001");

            var plan = DesktopDataService.PlanDuplicatePathRepairTargets(
                [history],
                [QuizArchiveDeepMatcher.InspectProjectFolder(icons)]);

            var suggestion = Assert.Single(plan.Suggestions);
            Assert.Equal(83, suggestion.HistoryId);
            Assert.Equal(icons, suggestion.ProposedFolder);
            Assert.Empty(plan.Conflicts);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateProjectFolder(string root, string folderName, string title)
    {
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "quiz.json"), $"{{\"title\":\"{title}\",\"series\":\"{title}\"}}");
        File.WriteAllText(Path.Combine(folder, "timeline.json"), $"{{\"name\":\"{title}\"}}");
        return folder;
    }

    private static QuizHistorySummary History(
        int id,
        string title,
        string series,
        int episode,
        string format,
        string projectFolder) =>
        new(
            Id: id,
            Title: title,
            Created: "2026-08-31",
            QuestionCount: 10,
            Categories: title.Replace(" Quiz", "", StringComparison.Ordinal),
            Format: format,
            QuestionSeconds: 5,
            ShuffleAnswers: true,
            ProjectFolder: projectFolder,
            SeriesName: series,
            EpisodeNumber: episode,
            YouTubeTitle: $"Can You Beat It? | {series} #{episode:000}",
            YouTubeDescription: "",
            Hashtags: "",
            PinnedComment: "",
            PublishedOnYouTube: false,
            YouTubeUrl: "");
}
