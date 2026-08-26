using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public sealed record FacebookThumbnailUpdateTarget(string VideoId, string Url);

public static class FacebookThumbnailUpdatePlanner
{
    public static FacebookThumbnailUpdateTarget Resolve(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (!SocialVideoUploadRules.CanUploadToFacebook(history))
            throw new InvalidOperationException("Facebook Reel cover updates are available for Shorts only.");
        if (!history.PublishedOnFacebook || string.IsNullOrWhiteSpace(history.FacebookUrl))
            throw new InvalidOperationException("Upload this Short to Facebook before updating its Reel cover.");

        var videoId = FacebookReelAnalyticsService.TryGetReelId(history.FacebookUrl);
        if (string.IsNullOrWhiteSpace(videoId))
            throw new InvalidOperationException("The saved Facebook Reel link does not contain a usable numeric Reel video ID.");
        return new FacebookThumbnailUpdateTarget(videoId, history.FacebookUrl.Trim());
    }
}

public partial class MainShellWindow
{
    private sealed record FacebookThumbnailPreflightSession(
        string PageAccessToken,
        FacebookPageIdentity Page);

    private void AppendFacebookThumbnailActions(WrapPanel actions)
    {
        var toolsButton = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Quiz Tools ▾",
                StringComparison.Ordinal));
        if (toolsButton is null)
            return;

        AppendYouTubeBulkThumbnailAction(toolsButton);
        AddUploadManagerMenuSeparator(toolsButton);
        AddUploadManagerMenuItem(toolsButton, "Update Facebook Reel Cover", async (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                await UpdateSelectedQuizFacebookThumbnailAsync(history, regenerateFirst: false);
            else
                MessageBox.Show(this, "Select a published Facebook Short first.", "Update Facebook Reel Cover",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuItem(toolsButton, "Regenerate + Update Facebook", async (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                await UpdateSelectedQuizFacebookThumbnailAsync(history, regenerateFirst: true);
            else
                MessageBox.Show(this, "Select a published Facebook Short first.", "Regenerate + Update Facebook",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AppendFacebookBulkThumbnailActions(toolsButton);
    }

    private async Task UpdateSelectedQuizFacebookThumbnailAsync(
        QuizHistorySummary history,
        bool regenerateFirst)
    {
        var title = regenerateFirst ? "Regenerate + Update Facebook" : "Update Facebook Reel Cover";
        QuizHistoricalThumbnailResult? regenerated = null;
        try
        {
            history = ResolveThumbnailHistoryEntry(history);
            var target = FacebookThumbnailUpdatePlanner.Resolve(history);
            var thumbnailPath = HistoricalThumbnailPath(history);
            if (!regenerateFirst && !File.Exists(thumbnailPath))
                throw new FileNotFoundException("Thumbnail.png was not found. Regenerate the thumbnail first.", thumbnailPath);

            var preflight = await ConfirmFacebookThumbnailUpdatePreflightAsync(
                this,
                history,
                target.VideoId,
                thumbnailPath,
                regenerateFirst);
            if (preflight is null)
                return;

            if (regenerateFirst)
            {
                regenerated = RegenerateHistoricalThumbnail(history, CreateQuizQuestionLookup());
                thumbnailPath = regenerated.ThumbnailPath;
                RefreshUploadManager();
            }

            await _facebookReelUpload.SetThumbnailAsync(
                preflight.PageAccessToken,
                target.VideoId,
                thumbnailPath);

            var details = regenerated is null
                ? $"Uploaded:\n{thumbnailPath}"
                : $"Featured question: {regenerated.FeaturedQuestionNumber} of {regenerated.QuestionCount}\n" +
                  $"Hook: {regenerated.Hook}\n\nUploaded:\n{thumbnailPath}";
            MessageBox.Show(
                this,
                (regenerated is null
                    ? "Facebook Reel cover updated successfully.\n\n"
                    : "Thumbnail regenerated and Facebook Reel cover updated successfully.\n\n") +
                details + "\n\nThe Reel and local upload records were not changed.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            var prefix = regenerated is null
                ? ""
                : "The local Thumbnail.png was regenerated successfully, but the Facebook Reel cover was not updated.\n\n";
            MessageBox.Show(this, prefix + error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<FacebookThumbnailPreflightSession?> ConfirmFacebookThumbnailUpdatePreflightAsync(
        Window owner,
        QuizHistorySummary history,
        string videoId,
        string thumbnailPath,
        bool regenerateFirst)
    {
        var settings = _data.LoadSettings();
        var pageAccessToken = FacebookPageToken();
        var page = await _facebookAnalytics.GetPageIdentityAsync(pageAccessToken);
        SocialPublishingAccountGuard.EnsureMatches(
            "Facebook Page", settings.ApprovedFacebookPageId, page.PageId);

        await _facebookReelUpload.VerifyUploadedReelAsync(pageAccessToken, videoId);

        var lines = new List<string>
        {
            regenerateFirst
                ? "Regenerate the local thumbnail and replace the cover on this existing Facebook Reel?"
                : "Replace the cover on this existing Facebook Reel?",
            "",
            $"Quiz: {history.UploadTitleDisplay}",
            $"Facebook Page: {page.PageName} ({page.PageId})",
            $"Reel video ID: {videoId}",
            regenerateFirst
                ? $"Cover: regenerate {Path.GetFileName(thumbnailPath)}, then upload it"
                : $"Cover: {thumbnailPath}",
            "",
            "Only the Reel cover will change on Facebook. The Reel and local upload records will not be changed.",
        };

        if (MessageBox.Show(
                owner,
                string.Join(Environment.NewLine, lines),
                "Facebook Reel Cover Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return null;

        if (settings.ApprovedFacebookPageId.Length == 0)
        {
            settings.ApprovedFacebookPageId = page.PageId;
            settings.ApprovedFacebookPageName = page.PageName;
            _data.SaveSettings(settings);
        }

        return new FacebookThumbnailPreflightSession(pageAccessToken, page);
    }
}
