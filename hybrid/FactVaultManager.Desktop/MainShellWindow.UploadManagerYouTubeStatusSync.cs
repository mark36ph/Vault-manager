using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly YouTubePublicationStatusService _youtubePublicationStatus = new();
    private bool _uploadManagerYouTubeStatusSyncInitialized;

    private void InitializeUploadManagerYouTubeStatusSync()
    {
        if (_uploadManagerYouTubeStatusSyncInitialized)
            return;

        _uploadManagerYouTubeStatusSyncInitialized = true;
        AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(UploadManagerLiveRefresh_Click),
            handledEventsToo: true);
    }

    private async void UploadManagerLiveRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is not Button refresh ||
            !string.Equals(refresh.Content?.ToString(), "Refresh", StringComparison.Ordinal) ||
            !HasUploadManagerHeadingNearby(refresh) ||
            !refresh.IsEnabled)
            return;

        var originalContent = refresh.Content;
        refresh.IsEnabled = false;
        refresh.Content = "Syncing...";
        try
        {
            var linked = _data.GetQuizHistory()
                .Where(history => history.PublishedOnYouTube && !string.IsNullOrWhiteSpace(history.YouTubeUrl))
                .Select(history => new
                {
                    History = history,
                    VideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl),
                })
                .Where(item => item.VideoId is not null)
                .ToList();

            if (linked.Count > 0)
            {
                var accessToken = await GetYouTubeManagementAccessTokenAsync();
                var settings = _data.LoadSettings();
                var channel = await _youtubeManagement.GetMyChannelAsync(accessToken);
                SocialPublishingAccountGuard.EnsureMatches(
                    "YouTube channel",
                    settings.ApprovedYouTubeChannelId,
                    channel.Id);

                var states = await _youtubePublicationStatus.FetchAsync(
                    accessToken,
                    linked.Select(item => item.VideoId!));

                var now = DateTimeOffset.Now;
                foreach (var item in linked)
                {
                    if (!states.TryGetValue(item.VideoId!, out var state))
                        continue;

                    var scheduledFor = YouTubeScheduleIntegrity.ResolveScheduledFor(
                        item.History.YouTubeScheduledFor,
                        state.PrivacyStatus,
                        state.PublishAt,
                        now);

                    _data.UpdateQuizHistoryYouTubeUploadState(
                        item.History.Id,
                        state.PrivacyStatus,
                        scheduledFor);
                }
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine("Upload Manager YouTube status sync failed: " + error);
            MessageBox.Show(
                this,
                "The local Upload Manager was refreshed, but YouTube status sync failed.\n\n" + error.Message,
                "YouTube Status Sync",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            RefreshQuizHistory();
            RefreshUploadManager();
            refresh.Content = originalContent;
            refresh.IsEnabled = true;
        }
    }

    private static bool HasUploadManagerHeadingNearby(DependencyObject button)
    {
        DependencyObject? ancestor = button;
        for (var level = 0; level < 5 && ancestor is not null; level++)
        {
            if (ContainsUploadManagerHeading(ancestor))
                return true;
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        return false;
    }

    private static bool ContainsUploadManagerHeading(DependencyObject root)
    {
        if (root is TextBlock text && string.Equals(text.Text, "Upload Manager", StringComparison.Ordinal))
            return true;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            if (ContainsUploadManagerHeading(VisualTreeHelper.GetChild(root, index)))
                return true;
        }
        return false;
    }
}
