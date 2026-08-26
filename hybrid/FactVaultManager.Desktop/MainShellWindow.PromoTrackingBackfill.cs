using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly YouTubePromoMetadataService _youtubePromoMetadata = new();
    private readonly FacebookPromoMetadataService _facebookPromoMetadata = new();
    private bool _promoTrackingBackfillUiInitialized;
    private int _promoTrackingBackfillUiAttempts;

    public void InitializePromoTrackingBackfillForApp()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(InitializePromoTrackingBackfillUi));
    }

    private void InitializePromoTrackingBackfillUi()
    {
        if (_promoTrackingBackfillUiInitialized) return;
        if (Content is DependencyObject root)
        {
            var refresh = FindVisualChildren<Button>(root)
                .FirstOrDefault(button => string.Equals(
                    Convert.ToString(button.Content),
                    "Refresh tracker",
                    StringComparison.Ordinal));
            if (refresh?.Parent is Grid header)
            {
                header.Children.Remove(refresh);
                refresh.Margin = new Thickness(8, 0, 0, 0);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                var backfill = new Button
                {
                    Content = "Backfill existing promos",
                    MinWidth = 170,
                    MinHeight = 36,
                    ToolTip = "Create tracking campaigns for already-published promos and replace their old full-quiz links without re-uploading media.",
                };
                StyleQuizHistoryButton(backfill, Color.FromRgb(255, 202, 45));
                backfill.Click += async (_, _) => await BackfillExistingPromosAsync(backfill);
                actions.Children.Add(backfill);
                actions.Children.Add(refresh);
                Grid.SetColumn(actions, 1);
                header.Children.Add(actions);
                _promoTrackingBackfillUiInitialized = true;
                return;
            }
        }

        if (++_promoTrackingBackfillUiAttempts >= 40) return;
        var retry = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            InitializePromoTrackingBackfillUi();
        };
        retry.Start();
    }

    private async Task BackfillExistingPromosAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        const string title = "Backfill Existing Promos";
        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!tracker.IsConfigured)
                throw new InvalidOperationException("Configure Settings → Link Tracker and click Save and test before backfilling existing promos.");

            _data.RecoverQuizHistoryProjectFolders();
            var targets = FactburstPromoBackfillPlanner.Build(_data.GetQuizHistory(2_000));
            if (targets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No existing long-form quizzes have saved promo publication records to backfill.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var youtubeCount = targets.Count(target => target.YouTube is not null);
            var facebookCount = targets.Count(target => target.Facebook is not null);
            var instagramCount = targets.Count(target => target.Instagram is not null);
            var appSettings = _data.LoadSettings();

            var youtubeToken = "";
            YouTubeManagedChannel? youtubeChannel = null;
            if (youtubeCount > 0)
            {
                youtubeToken = await GetYouTubeManagementAccessTokenAsync();
                youtubeChannel = await _youtubeManagement.GetMyChannelAsync(youtubeToken);
                SocialPublishingAccountGuard.EnsureMatches(
                    "YouTube channel",
                    appSettings.ApprovedYouTubeChannelId,
                    youtubeChannel.Id);
            }

            var facebookToken = "";
            FacebookPageIdentity? facebookPage = null;
            if (facebookCount > 0)
            {
                facebookToken = FacebookPageToken();
                facebookPage = await _facebookAnalytics.GetPageIdentityAsync(facebookToken);
                SocialPublishingAccountGuard.EnsureMatches(
                    "Facebook Page",
                    appSettings.ApprovedFacebookPageId,
                    facebookPage.PageId);
            }

            var confirmation = new StringBuilder();
            confirmation.AppendLine("Backfill tracking links into already-published promotional posts?");
            confirmation.AppendLine();
            confirmation.AppendLine($"Tracker: {tracker.BaseUrl}");
            confirmation.AppendLine($"Campaigns to create/update: {targets.Count:N0}");
            if (youtubeChannel is not null)
                confirmation.AppendLine($"YouTube promo Shorts: {youtubeCount:N0} • {youtubeChannel.Title} ({youtubeChannel.Id})");
            if (facebookPage is not null)
                confirmation.AppendLine($"Facebook promo Reels: {facebookCount:N0} • {facebookPage.PageName} ({facebookPage.PageId})");
            if (instagramCount > 0)
                confirmation.AppendLine($"Instagram link-in-bio URLs to prepare: {instagramCount:N0}");
            confirmation.AppendLine();
            confirmation.AppendLine("The app will preserve each remote post's current wording and replace only its old full-quiz YouTube link with the source-specific tracking link.");
            confirmation.AppendLine("Instagram captions will not be edited; its tracked URLs are prepared for link-in-bio use.");
            confirmation.AppendLine();
            confirmation.Append("No videos or Reels will be re-uploaded, and local upload/history records will not be changed.");

            if (MessageBox.Show(
                    this,
                    confirmation.ToString(),
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var rememberDestination = false;
            if (youtubeChannel is not null && appSettings.ApprovedYouTubeChannelId.Length == 0)
            {
                appSettings.ApprovedYouTubeChannelId = youtubeChannel.Id;
                appSettings.ApprovedYouTubeChannelName = youtubeChannel.Title;
                rememberDestination = true;
            }
            if (facebookPage is not null && appSettings.ApprovedFacebookPageId.Length == 0)
            {
                appSettings.ApprovedFacebookPageId = facebookPage.PageId;
                appSettings.ApprovedFacebookPageName = facebookPage.PageName;
                rememberDestination = true;
            }
            if (rememberDestination)
                _data.SaveSettings(appSettings);

            var campaignsUpdated = 0;
            var youtubeUpdated = 0;
            var youtubeAlreadyTracked = 0;
            var facebookUpdated = 0;
            var facebookAlreadyTracked = 0;
            var instagramLinks = new List<string>();
            var failures = new List<string>();

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                var history = target.History;
                sourceButton.Content = $"Backfill {index + 1}/{targets.Count}";

                FactburstTrackerCampaignLinks links;
                try
                {
                    links = await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                        tracker.BaseUrl,
                        tracker.ApiKey,
                        FactburstLinkTrackerClient.CampaignSlug(history),
                        history.Id,
                        history.UploadTitleDisplay,
                        history.YouTubeUrl);
                    campaignsUpdated++;
                }
                catch (Exception error)
                {
                    failures.Add($"Tracker — {history.UploadTitleDisplay}: {error.Message}");
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    continue;
                }

                if (target.YouTube is not null && youtubeChannel is not null)
                {
                    try
                    {
                        var current = await _youtubePromoMetadata.ReadAsync(
                            youtubeToken,
                            target.YouTube.VideoId,
                            youtubeChannel.Id);
                        var updated = FactburstPromoBackfillDescription.Apply(
                            current.Description,
                            links.YouTubePromoUrl);
                        if (string.Equals(updated, current.Description.Trim(), StringComparison.Ordinal))
                        {
                            youtubeAlreadyTracked++;
                        }
                        else
                        {
                            await _youtubePromoMetadata.UpdateDescriptionAsync(
                                youtubeToken,
                                current,
                                updated);
                            youtubeUpdated++;
                        }
                    }
                    catch (Exception error)
                    {
                        failures.Add($"YouTube — {history.UploadTitleDisplay}: {error.Message}");
                    }
                }

                if (target.Facebook is not null && facebookPage is not null)
                {
                    try
                    {
                        var current = await _facebookPromoMetadata.ReadAsync(
                            facebookToken,
                            target.Facebook.VideoId);
                        var updated = FactburstPromoBackfillDescription.Apply(
                            current.Description,
                            links.FacebookUrl);
                        if (string.Equals(updated, current.Description.Trim(), StringComparison.Ordinal))
                        {
                            facebookAlreadyTracked++;
                        }
                        else
                        {
                            await _facebookPromoMetadata.UpdateDescriptionAsync(
                                facebookToken,
                                target.Facebook.VideoId,
                                updated);
                            facebookUpdated++;
                        }
                    }
                    catch (Exception error)
                    {
                        failures.Add($"Facebook — {history.UploadTitleDisplay}: {error.Message}");
                    }
                }

                if (target.Instagram is not null)
                    instagramLinks.Add($"{history.UploadTitleDisplay} — {links.InstagramUrl}");

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            await RefreshFunnelPerformanceAsync(false);

            var summary = new StringBuilder();
            summary.AppendLine($"Campaigns registered/updated: {campaignsUpdated:N0}");
            summary.AppendLine($"YouTube promo descriptions updated: {youtubeUpdated:N0}");
            summary.AppendLine($"YouTube already tracked: {youtubeAlreadyTracked:N0}");
            summary.AppendLine($"Facebook Reel descriptions updated: {facebookUpdated:N0}");
            summary.AppendLine($"Facebook already tracked: {facebookAlreadyTracked:N0}");
            summary.AppendLine($"Instagram link-in-bio URLs ready: {instagramLinks.Count:N0}");
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
            summary.AppendLine("No media was re-uploaded and local upload/history records were not changed.");

            if (instagramLinks.Count > 0)
            {
                summary.AppendLine();
                summary.Append("Copy the Instagram link-in-bio list to the clipboard now?");
                var copy = MessageBox.Show(
                    this,
                    summary.ToString(),
                    title,
                    MessageBoxButton.YesNo,
                    failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (copy == MessageBoxResult.Yes)
                    Clipboard.SetText(string.Join(Environment.NewLine, instagramLinks));
            }
            else
            {
                MessageBox.Show(
                    this,
                    summary.ToString(),
                    title,
                    MessageBoxButton.OK,
                    failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
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
