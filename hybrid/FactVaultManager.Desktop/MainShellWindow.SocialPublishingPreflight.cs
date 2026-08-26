using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private sealed record SocialPublishingPreflightSession(
        string YouTubeAccessToken,
        YouTubeManagedChannel? YouTubeChannel,
        string FacebookPageToken,
        FacebookPageIdentity? FacebookPage);

    private sealed record YouTubeThumbnailPreflightSession(
        string AccessToken,
        YouTubeManagedChannel Channel);

    private async Task<SocialPublishingPreflightSession?> ConfirmSocialPublishingPreflightAsync(
        Window owner,
        SocialUploadDestination destinations,
        string videoPath,
        string title,
        string youtubePrivacy,
        DateTimeOffset? scheduledFor,
        int itemCount = 1)
    {
        var settings = _data.LoadSettings();
        var youtubeToken = "";
        YouTubeManagedChannel? youtubeChannel = null;
        var facebookToken = "";
        FacebookPageIdentity? facebookPage = null;

        if (destinations.HasFlag(SocialUploadDestination.YouTube))
        {
            youtubeToken = await GetYouTubeManagementAccessTokenAsync();
            youtubeChannel = await _youtubeManagement.GetMyChannelAsync(youtubeToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel", settings.ApprovedYouTubeChannelId, youtubeChannel.Id);
        }

        if (destinations.HasFlag(SocialUploadDestination.Facebook) ||
            destinations.HasFlag(SocialUploadDestination.Instagram))
        {
            facebookToken = FacebookPageToken();
            facebookPage = await _facebookAnalytics.GetPageIdentityAsync(facebookToken);
            SocialPublishingAccountGuard.EnsureMatches(
                "Facebook Page", settings.ApprovedFacebookPageId, facebookPage.PageId);
        }

        var lines = new List<string>
        {
            "Check the destination accounts before uploading.",
            "",
            itemCount == 1 ? $"Video: {Path.GetFileName(videoPath)}" : $"Queue: {itemCount:N0} videos",
            itemCount == 1 ? $"Title: {title}" : $"First video: {title}",
        };
        if (youtubeChannel is not null)
        {
            lines.Add("");
            lines.Add($"YouTube: {youtubeChannel.Title} ({youtubeChannel.Id})");
            lines.Add(scheduledFor is null
                ? $"Visibility: {youtubePrivacy}"
                : $"Scheduled: {scheduledFor.Value:dd-MM-yyyy HH:mm} local time");
        }
        if (facebookPage is not null)
        {
            lines.Add("");
            lines.Add($"Facebook Page: {facebookPage.PageName} ({facebookPage.PageId})");
            if (destinations.HasFlag(SocialUploadDestination.Facebook)) lines.Add("Facebook Reel: selected");
            if (destinations.HasFlag(SocialUploadDestination.Instagram))
                lines.Add("Instagram Reel: selected through this linked Page");
        }
        lines.Add("");
        lines.Add("Continue with this upload?");

        if (MessageBox.Show(owner, string.Join(Environment.NewLine, lines), "Publishing Preflight",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return null;

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
        if (remember) _data.SaveSettings(settings);

        return new SocialPublishingPreflightSession(youtubeToken, youtubeChannel, facebookToken, facebookPage);
    }

    private async Task<YouTubeThumbnailPreflightSession?> ConfirmYouTubeThumbnailUpdatePreflightAsync(
        Window owner,
        QuizHistorySummary history,
        string videoId,
        string thumbnailPath,
        bool regenerateFirst)
    {
        var settings = _data.LoadSettings();
        var accessToken = await GetYouTubeManagementAccessTokenAsync();
        var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
        SocialPublishingAccountGuard.EnsureMatches(
            "YouTube channel", settings.ApprovedYouTubeChannelId, channel.Id);

        var lines = new List<string>
        {
            regenerateFirst
                ? "Regenerate the local thumbnail and replace the thumbnail on this existing YouTube video?"
                : "Replace the thumbnail on this existing YouTube video?",
            "",
            $"Quiz: {history.UploadTitleDisplay}",
            $"YouTube: {channel.Title} ({channel.Id})",
            $"Video ID: {videoId}",
            regenerateFirst
                ? $"Thumbnail: regenerate {Path.GetFileName(thumbnailPath)}, then upload it"
                : $"Thumbnail: {thumbnailPath}",
            "",
            "Only the custom thumbnail will change on YouTube. The video and local upload records will not be changed.",
        };

        if (MessageBox.Show(
                owner,
                string.Join(Environment.NewLine, lines),
                "YouTube Thumbnail Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return null;

        if (settings.ApprovedYouTubeChannelId.Length == 0)
        {
            settings.ApprovedYouTubeChannelId = channel.Id;
            settings.ApprovedYouTubeChannelName = channel.Title;
            _data.SaveSettings(settings);
        }

        return new YouTubeThumbnailPreflightSession(accessToken, channel);
    }

    private void ResetApprovedYouTubeAccount()
    {
        var settings = _data.LoadSettings();
        settings.ApprovedYouTubeChannelId = "";
        settings.ApprovedYouTubeChannelName = "";
        _data.SaveSettings(settings);
        SettingsStatusText.Text = "Approved YouTube destination reset. The next upload will ask you to approve the connected channel.";
    }

    private void ResetApprovedFacebookAccount()
    {
        var settings = _data.LoadSettings();
        settings.ApprovedFacebookPageId = "";
        settings.ApprovedFacebookPageName = "";
        _data.SaveSettings(settings);
        SettingsStatusText.Text = "Approved Facebook destination reset. The next upload will ask you to approve the connected Page.";
    }
}
