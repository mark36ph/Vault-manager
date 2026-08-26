using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record YouTubeBulkThumbnailTarget(
    int HistoryId,
    string Title,
    string VideoId,
    string ThumbnailPath);

public static class YouTubeBulkThumbnailUpdatePlanner
{
    public static bool IsCandidate(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return QuizHistoricalThumbnailRegenerator.IsBatchEligible(history) &&
               history.PublishedOnYouTube &&
               !string.IsNullOrWhiteSpace(history.YouTubeUrl);
    }

    public static YouTubeBulkThumbnailTarget Resolve(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!QuizHistoricalThumbnailRegenerator.IsBatchEligible(history))
            throw new InvalidOperationException("YouTube bulk thumbnail updates are available for long-form quizzes only.");
        if (!history.PublishedOnYouTube || string.IsNullOrWhiteSpace(history.YouTubeUrl))
            throw new InvalidOperationException("This long-form quiz has not been published to YouTube.");

        var videoId = YouTubeVideoReference.ParseVideoId(history.YouTubeUrl);
        var folder = history.ProjectFolder.Trim();
        if (folder.Length == 0)
            throw new DirectoryNotFoundException("This Quiz History entry does not have a saved project folder.");

        var thumbnailPath = Path.Combine(Path.GetFullPath(folder), "Thumbnail.png");
        if (!File.Exists(thumbnailPath))
            throw new FileNotFoundException("Thumbnail.png was not found. Regenerate the thumbnail first.", thumbnailPath);

        return new YouTubeBulkThumbnailTarget(
            history.Id,
            history.UploadTitleDisplay,
            videoId,
            thumbnailPath);
    }
}

public partial class MainShellWindow
{
    private void AppendYouTubeBulkThumbnailAction(Button toolsButton)
    {
        AddUploadManagerMenuItem(toolsButton, "Update All YouTube Thumbnails", async (_, _) =>
            await UpdateAllYouTubeThumbnailsAsync(toolsButton));
    }

    private async Task UpdateAllYouTubeThumbnailsAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        const string title = "Update All YouTube Thumbnails";
        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000)
                .Where(YouTubeBulkThumbnailUpdatePlanner.IsCandidate)
                .ToList();
            if (histories.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "There are no published long-form YouTube quizzes with saved video links to update.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var settings = _data.LoadSettings();
            var accessToken = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel", settings.ApprovedYouTubeChannelId, channel.Id);

            var confirmation =
                $"Update the YouTube thumbnail for every published long-form quiz on this channel?\n\n" +
                $"YouTube: {channel.Title} ({channel.Id})\n" +
                $"Published long-form quizzes found: {histories.Count:N0}\n\n" +
                "Each video will use its existing local Thumbnail.png. Missing thumbnails, invalid saved links, or videos that cannot be updated will be skipped and reported.\n\n" +
                "Videos and local upload records will not be changed.";
            if (MessageBox.Show(
                    this,
                    confirmation,
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (settings.ApprovedYouTubeChannelId.Length == 0)
            {
                settings.ApprovedYouTubeChannelId = channel.Id;
                settings.ApprovedYouTubeChannelName = channel.Title;
                _data.SaveSettings(settings);
            }

            var service = new YouTubeThumbnailService();
            var succeeded = 0;
            var failed = new List<string>();
            for (var index = 0; index < histories.Count; index++)
            {
                var history = histories[index];
                sourceButton.Content = $"YouTube {index + 1}/{histories.Count}";
                try
                {
                    var target = YouTubeBulkThumbnailUpdatePlanner.Resolve(history);
                    await service.SetAsync(accessToken, target.VideoId, target.ThumbnailPath);
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Updated: {succeeded:N0}");
            summary.AppendLine($"Skipped/failed: {failed.Count:N0}");
            if (failed.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Items needing attention:");
                foreach (var failure in failed.Take(10))
                    summary.AppendLine("• " + failure);
                if (failed.Count > 10)
                    summary.AppendLine($"• …and {failed.Count - 10:N0} more");
            }
            summary.AppendLine();
            summary.Append("Only YouTube custom thumbnails were changed.");

            MessageBox.Show(
                this,
                summary.ToString(),
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
}
