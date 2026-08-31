namespace FactVaultManager.Desktop.Tests;

public sealed class QuizDuplicateGlobalAssignmentPlannerTests
{
    [Fact]
    public void Plan_UsesGlobalOneToOneAssignment_WhenLocalBestFolderConflicts()
    {
        var candidates = new Dictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>>
        {
            [1] = [Candidate(1, @"Z:\Space - 001", 300), Candidate(1, @"Z:\Space alternate", 280)],
            [2] = [Candidate(2, @"Z:\Space - 001", 290)],
        };

        var plan = QuizDuplicateGlobalAssignmentPlanner.Plan(candidates, stabilityMargin: 25);

        Assert.Equal(2, plan.MatchedCount);
        Assert.Equal(@"Z:\Space alternate", plan.BestAssignments[1].ArchiveFolder);
        Assert.Equal(@"Z:\Space - 001", plan.BestAssignments[2].ArchiveFolder);
        Assert.Equal([1, 2], plan.StableHistoryIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Plan_MaximizesMatchedRows_BeforeTotalScore()
    {
        var candidates = new Dictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>>
        {
            [10] = [Candidate(10, @"Z:\Technology - A", 500), Candidate(10, @"Z:\Technology - B", 100)],
            [20] = [Candidate(20, @"Z:\Technology - A", 490)],
        };

        var plan = QuizDuplicateGlobalAssignmentPlanner.Plan(candidates, stabilityMargin: 25);

        Assert.Equal(2, plan.MatchedCount);
        Assert.Equal(590, plan.TotalScore);
        Assert.Equal(@"Z:\Technology - B", plan.BestAssignments[10].ArchiveFolder);
        Assert.Equal(@"Z:\Technology - A", plan.BestAssignments[20].ArchiveFolder);
        Assert.Equal([10, 20], plan.StableHistoryIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Plan_LeavesPerfectlySwappableAssignmentsUnstable()
    {
        var candidates = new Dictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>>
        {
            [70] = [Candidate(70, @"Z:\Space Alpha", 300), Candidate(70, @"Z:\Space Beta", 300)],
            [92] = [Candidate(92, @"Z:\Space Alpha", 300), Candidate(92, @"Z:\Space Beta", 300)],
        };

        var plan = QuizDuplicateGlobalAssignmentPlanner.Plan(candidates, stabilityMargin: 25);

        Assert.Equal(2, plan.MatchedCount);
        Assert.Empty(plan.StableHistoryIds);
    }

    [Fact]
    public void Plan_DoesNotClaimSingleContestedFolder_WhenScoresAreTooClose()
    {
        var candidates = new Dictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>>
        {
            [71] = [Candidate(71, @"Z:\Technology Quiz", 300)],
            [94] = [Candidate(94, @"Z:\Technology Quiz", 290)],
        };

        var plan = QuizDuplicateGlobalAssignmentPlanner.Plan(candidates, stabilityMargin: 25);

        Assert.Equal(1, plan.MatchedCount);
        Assert.Empty(plan.StableHistoryIds);
    }

    [Fact]
    public void Plan_CanAcceptSingleContestedFolder_WhenGlobalEvidenceMarginIsDecisive()
    {
        var candidates = new Dictionary<int, IReadOnlyList<QuizArchiveDeepCandidate>>
        {
            [43] = [Candidate(43, @"Z:\General Knowledge Quiz - 002", 330)],
            [104] = [Candidate(104, @"Z:\General Knowledge Quiz - 002", 280)],
        };

        var plan = QuizDuplicateGlobalAssignmentPlanner.Plan(candidates, stabilityMargin: 25);

        Assert.Equal(1, plan.MatchedCount);
        Assert.Equal(43, Assert.Single(plan.StableHistoryIds));
        Assert.Equal(@"Z:\General Knowledge Quiz - 002", plan.BestAssignments[43].ArchiveFolder);
    }

    private static QuizArchiveDeepCandidate Candidate(int historyId, string folder, int score) =>
        new(
            HistoryId: historyId,
            Label: $"History #{historyId}",
            CurrentFolder: @"C:\shared",
            ArchiveFolder: folder,
            Confidence: QuizArchiveMatchConfidence.High,
            Score: score,
            Evidence: ["test evidence"]);
}
