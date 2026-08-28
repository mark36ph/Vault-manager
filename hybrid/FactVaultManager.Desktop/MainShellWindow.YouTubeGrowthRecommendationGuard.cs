using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _youtubeGrowthRecommendationGuardInitialized;
    private bool _youtubeGrowthRecommendationGuardApplyQueued;

    internal void InitializeYouTubeGrowthRecommendationGuard()
    {
        if (_youtubeGrowthRecommendationGuardInitialized)
            return;

        _youtubeGrowthRecommendationGuardInitialized = true;

        if (_youtubeManagerContent is not null)
            _youtubeManagerContent.LayoutUpdated += YouTubeGrowthRecommendationGuard_LayoutUpdated;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureYouTubeGrowthRecommendationCard));
    }

    private void YouTubeGrowthRecommendationGuard_LayoutUpdated(object? sender, EventArgs e)
    {
        if (!string.Equals(_youtubeManagerSection, "analytics", StringComparison.OrdinalIgnoreCase) ||
            _youtubeGrowthRecommendationGuardApplyQueued ||
            GrowthRecommendationCardIsCurrent())
        {
            return;
        }

        _youtubeGrowthRecommendationGuardApplyQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _youtubeGrowthRecommendationGuardApplyQueued = false;
                EnsureYouTubeGrowthRecommendationCard();
            }));
    }

    private bool GrowthRecommendationCardIsCurrent()
    {
        if (_youtubeNextQuizText is null || _youtubeNextQuizReasonText is null)
            return false;

        return _youtubeNextQuizText.Text.EndsWith("Quiz — Full video", StringComparison.Ordinal) &&
               !_youtubeNextQuizReasonText.Text.Contains("published Short", StringComparison.OrdinalIgnoreCase) &&
               !_youtubeNextQuizReasonText.Text.Contains("published Shorts", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureYouTubeGrowthRecommendationCard()
    {
        if (_youtubeNextQuizText is null || _youtubeNextQuizReasonText is null ||
            !string.Equals(_youtubeManagerSection, "analytics", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
                .GroupBy(snapshot => snapshot.HistoryId)
                .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                .ToList();
            var plan = BuildYouTubeGrowthCategoryPlan(1);
            var summary = YouTubeGrowthUiSummaryBuilder.Build(plan, snapshots);

            _youtubeNextQuizText.Text = $"{summary.RecommendedCategory} Quiz — Full video";
            _youtubeNextQuizReasonText.Text = summary.RecommendationReason;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"YouTube growth recommendation guard: {error.Message}");
            _youtubeNextQuizText.Text = "Growth recommendation unavailable";
            _youtubeNextQuizReasonText.Text = "Refresh YouTube Analytics after checking the question bank.";
        }
    }
}
