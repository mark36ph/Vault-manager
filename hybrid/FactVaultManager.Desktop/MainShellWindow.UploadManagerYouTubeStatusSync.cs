using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly YouTubePublicationStatusService _youtubePublicationStatus = new();
    private bool _uploadManagerYouTubeStatusSyncInitialized;
    private Button? _uploadManagerLiveRefreshButton;

    private void InitializeUploadManagerYouTubeStatusSync()
    {
        if (_uploadManagerYouTubeStatusSyncInitialized)
            return;

        _uploadManagerYouTubeStatusSyncInitialized = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(AttachUploadManagerLiveRefresh));
    }

    private void AttachUploadManagerLiveRefresh()
    {
        var refresh = FindUploadManagerRefreshButton(this);
        if (refresh is null)
        {
            var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            var attempts = 0;
            retry.Tick += (_, _) =>
            {
                attempts++;
                var candidate = FindUploadManagerRefreshButton(this);
                if (candidate is null && attempts < 10) return;
                retry.Stop();
                if (candidate is not null) HookUploadManagerRefresh(candidate);
            };
            retry.Start();
            return;
        }

        HookUploadManagerRefresh(refresh);
    }

    private void HookUploadManagerRefresh(Button refresh)
    {
        if (ReferenceEquals(_uploadManagerLiveRefreshButton, refresh))
            return;

        if (_uploadManagerLiveRefreshButton is not null)
            _uploadManagerLiveRefreshButton.Click -= UploadManagerLiveRefresh_Click;

        _uploadManagerLiveRefreshButton = refresh;
        refresh.ToolTip = "Refresh local upload records and sync live YouTube visibility/schedule status";
        refresh.Click += UploadManagerLiveRefresh_Click;
    }

    private async void UploadManagerLiveRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button refresh || !refresh.IsEnabled)
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

                foreach (var item in linked)
                {
                    if (!states.TryGetValue(item.VideoId!, out var state))
                        continue;

                    _data.UpdateQuizHistoryYouTubeUploadState(
                        item.History.Id,
                        state.PrivacyStatus,
                        state.PublishAt);
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
            refresh.Content = originalContent;
            refresh.IsEnabled = true;
        }
    }

    private static Button? FindUploadManagerRefreshButton(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), "Refresh", StringComparison.Ordinal) &&
                HasUploadManagerHeadingNearby(button))
            {
                return button;
            }

            var nested = FindUploadManagerRefreshButton(child);
            if (nested is not null)
                return nested;
        }

        return null;
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
