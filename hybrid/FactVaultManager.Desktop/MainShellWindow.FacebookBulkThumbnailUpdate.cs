using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record FacebookBulkThumbnailTarget(
    int HistoryId,
    string Title,
    string VideoId,
    string ThumbnailPath);

public static class FacebookBulkThumbnailUpdatePlanner
{
    public static bool IsCandidate(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return SocialVideoUploadRules.CanUploadToFacebook(history) &&
               history.PublishedOnFacebook &&
               !string.IsNullOrWhiteSpace(history.FacebookUrl);
    }

    public static FacebookBulkThumbnailTarget Resolve(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!SocialVideoUploadRules.CanUploadToFacebook(history))
            throw new InvalidOperationException("Facebook bulk Reel-cover updates are available for Shorts only.");
        if (!history.PublishedOnFacebook || string.IsNullOrWhiteSpace(history.FacebookUrl))
            throw new InvalidOperationException("This Short has not been published to Facebook.");

        var videoId = FacebookReelAnalyticsService.TryGetReelId(history.FacebookUrl);
        if (string.IsNullOrWhiteSpace(videoId))
            throw new InvalidOperationException("The saved Facebook Reel link does not contain a usable numeric Reel video ID.");

        var folder = history.ProjectFolder.Trim();
        if (folder.Length == 0)
            throw new DirectoryNotFoundException("This Quiz History entry does not have a saved project folder.");

        var thumbnailPath = Path.Combine(Path.GetFullPath(folder), "Thumbnail.png");
        if (!File.Exists(thumbnailPath))
            throw new FileNotFoundException("Thumbnail.png was not found for this Short.", thumbnailPath);

        return new FacebookBulkThumbnailTarget(
            history.Id,
            history.UploadTitleDisplay,
            videoId,
            thumbnailPath);
    }
}

public sealed record PublishedThumbnailRefreshPlan(
    IReadOnlyList<QuizHistorySummary> YouTubeHistories,
    IReadOnlyList<QuizHistorySummary> FacebookHistories)
{
    public static PublishedThumbnailRefreshPlan Build(IEnumerable<QuizHistorySummary> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        var items = histories.ToList();
        return new PublishedThumbnailRefreshPlan(
            items.Where(YouTubeBulkThumbnailUpdatePlanner.IsCandidate).ToList(),
            items.Where(FacebookBulkThumbnailUpdatePlanner.IsCandidate).ToList());
    }
}

public partial class MainShellWindow
{
    private void AppendFacebookBulkThumbnailActions(Button toolsButton)
    {
        AddUploadManagerMenuSeparator(toolsButton);
        AddUploadManagerMenuItem(toolsButton, "Update All Facebook Reel Covers", async (_, _) =>
            await UpdateAllFacebookReelCoversAsync(toolsButton));
        AddUploadManagerMenuItem(toolsButton, "Refresh All Published Thumbnails", async (_, _) =>
            await RefreshAllPublishedThumbnailsAsync(toolsButton));
    }

    private async Task UpdateAllFacebookReelCoversAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        const string title = "Update All Facebook Reel Covers";
        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000)
                .Where(FacebookBulkThumbnailUpdatePlanner.IsCandidate)
                .ToList();
            if (histories.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "There are no published Facebook Shorts with saved Reel links to update.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var settings = _data.LoadSettings();
            var pageAccessToken = FacebookPageToken();
            var page = await _facebookAnalytics.GetPageIdentityAsync(pageAccessToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "Facebook Page", settings.ApprovedFacebookPageId, page.PageId);

            var confirmation =
                $"Update the Reel cover for every published Facebook Short on this Page?\n\n" +
                $"Facebook Page: {page.PageName} ({page.PageId})\n" +
                $"Published Facebook Shorts found: {histories.Count:N0}\n\n" +
                "Each Reel will use its existing local Thumbnail.png. Missing thumbnails, invalid saved links, or Reels that cannot be updated will be skipped and reported.\n\n" +
                "Reels and local upload records will not be changed.";
            if (MessageBox.Show(
                    this,
                    confirmation,
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (settings.ApprovedFacebookPageId.Length == 0)
            {
                settings.ApprovedFacebookPageId = page.PageId;
                settings.ApprovedFacebookPageName = page.PageName;
                _data.SaveSettings(settings);
            }

            var succeeded = 0;
            var failed = new List<string>();
            for (var index = 0; index < histories.Count; index++)
            {
                var history = histories[index];
                sourceButton.Content = $"Facebook {index + 1}/{histories.Count}";
                try
                {
                    var target = FacebookBulkThumbnailUpdatePlanner.Resolve(history);
                    await _facebookReelUpload.VerifyUploadedReelAsync(pageAccessToken, target.VideoId);
                    await _facebookReelUpload.SetThumbnailAsync(pageAccessToken, target.VideoId, target.ThumbnailPath);
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            var summary = BuildBulkThumbnailSummary(
                $"Updated: {succeeded:N0}",
                failed,
                "Only Facebook Reel covers were changed.");
            MessageBox.Show(
                this,
                summary,
                title,
                MessageBoxButton.OK,
                failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private async Task RefreshAllPublishedThumbnailsAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        const string title = "Refresh All Published Thumbnails";
        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000).ToList();
            var plan = PublishedThumbnailRefreshPlan.Build(histories);
            if (plan.YouTubeHistories.Count == 0 && plan.FacebookHistories.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "There are no published YouTube long-form quizzes or Facebook Shorts with saved links to refresh.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var settings = _data.LoadSettings();
            var youtubeToken = "";
            YouTubeManagedChannel? youtubeChannel = null;
            var facebookToken = "";
            FacebookPageIdentity? facebookPage = null;

            if (plan.YouTubeHistories.Count > 0)
            {
                youtubeToken = await GetYouTubeManagementAccessTokenAsync();
                youtubeChannel = await _youtubeManagement.GetMyChannelAsync(youtubeToken);
                SocialPublishingAccountGuard.EnsureMatches(
                    "YouTube channel", settings.ApprovedYouTubeChannelId, youtubeChannel.Id);
            }

            if (plan.FacebookHistories.Count > 0)
            {
                facebookToken = FacebookPageToken();
                facebookPage = await _facebookAnalytics.GetPageIdentityAsync(facebookToken);
                SocialPublishingAccountGuard.EnsureMatches(
                    "Facebook Page", settings.ApprovedFacebookPageId, facebookPage.PageId);
            }

            var lines = new List<string>
            {
                "Refresh thumbnails across all published quiz destinations?",
                "",
            };
            if (youtubeChannel is not null)
            {
                lines.Add($"YouTube: {youtubeChannel.Title} ({youtubeChannel.Id})");
                lines.Add($"Published long-form quizzes: {plan.YouTubeHistories.Count:N0}");
                lines.Add("These local thumbnails will be regenerated before YouTube is updated.");
                lines.Add("");
            }
            if (facebookPage is not null)
            {
                lines.Add($"Facebook Page: {facebookPage.PageName} ({facebookPage.PageId})");
                lines.Add($"Published Facebook Shorts: {plan.FacebookHistories.Count:N0}");
                lines.Add("Facebook Reels will use each Short's existing local Thumbnail.png.");
                lines.Add("");
            }
            lines.Add("Videos, Reels and local upload/history records will not be changed.");
            lines.Add("Failures are isolated, so the remaining items will continue.");

            if (MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, lines),
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var remember = false;
            if (youtubeChannel is not null && settings.ApprovedYouTubeChannelId.Length == 0)
            {
                settings.ApprovedYouTubeChannelId = youtubeChannel.Id;
                settings.ApprovedYouTubeChannelName = youtubeChannel.Title;
                remember = true;
            }
            if (facebookPage is not null && settings.ApprovedFacebookPageId.Length == 0)
            {
                settings.ApprovedFacebookPageId = facebookPage.PageId;
                settings.ApprovedFacebookPageName = facebookPage.PageName;
                remember = true;
            }
            if (remember)
                _data.SaveSettings(settings);

            var failures = new List<string>();
            var regenerated = 0;
            var youtubeUpdated = 0;
            var facebookUpdated = 0;

            if (plan.YouTubeHistories.Count > 0)
            {
                var lookup = CreateQuizQuestionLookup();
                var youtubeService = new YouTubeThumbnailService();
                for (var index = 0; index < plan.YouTubeHistories.Count; index++)
                {
                    var history = plan.YouTubeHistories[index];
                    sourceButton.Content = $"Refresh YouTube {index + 1}/{plan.YouTubeHistories.Count}";
                    try
                    {
                        var videoId = YouTubeVideoReference.ParseVideoId(history.YouTubeUrl);
                        var result = RegenerateHistoricalThumbnail(history, lookup);
                        regenerated++;
                        await youtubeService.SetAsync(youtubeToken, videoId, result.ThumbnailPath);
                        youtubeUpdated++;
                    }
                    catch (Exception error)
                    {
                        failures.Add($"YouTube — {history.UploadTitleDisplay}: {error.Message}");
                    }

                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            if (plan.FacebookHistories.Count > 0)
            {
                for (var index = 0; index < plan.FacebookHistories.Count; index++)
                {
                    var history = plan.FacebookHistories[index];
                    sourceButton.Content = $"Refresh Facebook {index + 1}/{plan.FacebookHistories.Count}";
                    try
                    {
                        var target = FacebookBulkThumbnailUpdatePlanner.Resolve(history);
                        await _facebookReelUpload.VerifyUploadedReelAsync(facebookToken, target.VideoId);
                        await _facebookReelUpload.SetThumbnailAsync(facebookToken, target.VideoId, target.ThumbnailPath);
                        facebookUpdated++;
                    }
                    catch (Exception error)
                    {
                        failures.Add($"Facebook — {history.UploadTitleDisplay}: {error.Message}");
                    }

                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            RefreshUploadManager();
            var summary = new StringBuilder();
            summary.AppendLine($"Long-form thumbnails regenerated: {regenerated:N0}");
            summary.AppendLine($"YouTube thumbnails updated: {youtubeUpdated:N0}");
            summary.AppendLine($"Facebook Reel covers updated: {facebookUpdated:N0}");
            summary.AppendLine($"Skipped/failed: {failures.Count:N0}");
            if (failures.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Items needing attention:");
                foreach (var failure in failures.Take(12))
                    summary.AppendLine("• " + failure);
                if (failures.Count > 12)
                    summary.AppendLine($"• …and {failures.Count - 12:N0} more");
            }
            summary.AppendLine();
            summary.Append("Published videos/Reels and upload/history records were not changed.");

            MessageBox.Show(
                this,
                summary.ToString(),
                title,
                MessageBoxButton.OK,
                failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private static string BuildBulkThumbnailSummary(
        string successLine,
        IReadOnlyList<string> failures,
        string finalLine)
    {
        var summary = new StringBuilder();
        summary.AppendLine(successLine);
        summary.AppendLine($"Skipped/failed: {failures.Count:N0}");
        if (failures.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Items needing attention:");
            foreach (var failure in failures.Take(10))
                summary.AppendLine("• " + failure);
            if (failures.Count > 10)
                summary.AppendLine($"• …and {failures.Count - 10:N0} more");
        }
        summary.AppendLine();
        summary.Append(finalLine);
        return summary.ToString();
    }
}
