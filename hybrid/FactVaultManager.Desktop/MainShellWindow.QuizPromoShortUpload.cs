using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ShowQuizPromoShortUploadDialog(QuizHistorySummary history)
    {
        if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Choose the long-form quiz that owns the promotional Short.",
                "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder);
        if (video is null)
        {
            MessageBox.Show(this, "Create the promotional Short before uploading it.",
                "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (history.YouTubeUrl.Trim().Length == 0)
        {
            MessageBox.Show(this,
                "Upload the full quiz to YouTube first. Its link is required for the promo description and related-video funnel.",
                "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previousYouTube = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
        var previousFacebook = QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder);
        var previousInstagram = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder);
        var forceReuploadAll = false;
        if (previousYouTube is not null && previousFacebook is not null && previousInstagram is not null)
        {
            if (MessageBox.Show(this,
                    "This promotional Short is already recorded as uploaded to YouTube, Facebook, and Instagram.\n\nUpload another copy to all three platforms?",
                    "Promo Short Already Uploaded", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            forceReuploadAll = true;
        }

        var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        var campaignSlug = FactburstLinkTrackerClient.CampaignSlug(history);
        var trackingPreview = trackerSettings.IsConfigured
            ? FactburstLinkTrackerClient.BuildLinks(trackerSettings.BaseUrl, campaignSlug)
            : null;

        var dialog = new Window
        {
            Title = "Upload Promotional Short",
            Owner = this,
            Width = 780,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 940,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };
        var root = new Grid { Margin = new Thickness(24) };
        for (var row = 0; row < 6; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock
        {
            Text = "Upload Promo Short Everywhere",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Publishes the promo as a YouTube Short, Facebook Reel, and Instagram Reel. Promo records stay separate from the full quiz.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(heading);

        var file = new TextBox
        {
            Text = video,
            IsReadOnly = true,
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var filePanel = new StackPanel();
        filePanel.Children.Add(Label("PROMOTIONAL SHORT"));
        filePanel.Children.Add(file);
        Grid.SetRow(filePanel, 1);
        root.Children.Add(filePanel);

        var title = new TextBox
        {
            Text = QuizPromoShortUploadMetadata.Title(history.UploadTitleDisplay),
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var titlePanel = new StackPanel();
        titlePanel.Children.Add(Label("UPLOAD TITLE — YOUTUBE / FACEBOOK"));
        titlePanel.Children.Add(title);
        Grid.SetRow(titlePanel, 2);
        root.Children.Add(titlePanel);

        var description = new TextBox
        {
            Text = QuizPromoShortUploadMetadata.Description(history.UploadTitleDisplay, history.YouTubeUrl, history.Hashtags),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 145,
            MaxHeight = 220,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 5),
        };
        var descriptionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        descriptionPanel.Children.Add(Label("YOUTUBE / FACEBOOK DESCRIPTION — KEEP THE FULL-QUIZ LINK"));
        descriptionPanel.Children.Add(description);
        descriptionPanel.Children.Add(new TextBlock
        {
            Text = "Instagram replaces the YouTube URL with the link-in-bio call to action. Use the Instagram tracking URL below as that bio link when you want Instagram attribution.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
        });
        if (trackingPreview is not null)
        {
            descriptionPanel.Children.Add(new TextBlock
            {
                Text = $"Tracking campaign: {trackingPreview.Slug}",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(23, 92, 211)),
                Margin = new Thickness(0, 8, 0, 4),
            });
            descriptionPanel.Children.Add(new TextBox
            {
                Text = "Facebook: " + trackingPreview.FacebookUrl + Environment.NewLine +
                       "Instagram link in bio: " + trackingPreview.InstagramUrl + Environment.NewLine +
                       "YouTube promo: " + trackingPreview.YouTubePromoUrl,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 66,
                Padding = new Thickness(8),
            });
        }
        else
        {
            descriptionPanel.Children.Add(new TextBlock
            {
                Text = "Funnel tracking is currently off. Configure Settings → Link Tracker to use source-specific links.",
                Foreground = new SolidColorBrush(Color.FromRgb(185, 95, 20)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
        Grid.SetRow(descriptionPanel, 3);
        root.Children.Add(descriptionPanel);

        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        options.Children.Add(new TextBlock
        {
            Text = "YouTube visibility",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var privacy = new ComboBox { Width = 130, Height = 32 };
        privacy.Items.Add("private");
        privacy.Items.Add("unlisted");
        privacy.Items.Add("public");
        privacy.SelectedItem = "private";
        options.Children.Add(privacy);
        var notify = new CheckBox
        {
            Content = "Notify YouTube subscribers",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        };
        options.Children.Add(notify);
        Grid.SetRow(options, 4);
        root.Children.Add(options);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var initialPending = PromoPendingPlatforms(
            forceReuploadAll || previousYouTube is null,
            forceReuploadAll || previousFacebook is null,
            forceReuploadAll || previousInstagram is null);
        var statusText = new TextBlock
        {
            Text = "Ready. Will upload to: " + initialPending + ".",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
        };
        var progress = new ProgressBar
        {
            Height = 6,
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 5, 16, 0),
        };
        status.Children.Add(statusText);
        status.Children.Add(progress);
        footer.Children.Add(status);
        var cancel = new Button { Content = "Cancel", MinWidth = 82, MinHeight = 36, IsCancel = true };
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        var upload = new Button
        {
            Content = "Upload to All Platforms",
            MinWidth = 166,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        StyleQuizHistoryButton(upload, Color.FromRgb(70, 235, 115));
        Grid.SetColumn(upload, 2);
        footer.Children.Add(upload);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);

        upload.Click += async (_, _) =>
        {
            try
            {
                var recordedYouTube = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
                var recordedFacebook = QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder);
                var recordedInstagram = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder);
                var uploadYouTube = forceReuploadAll || recordedYouTube is null;
                var uploadFacebook = forceReuploadAll || recordedFacebook is null;
                var uploadInstagram = forceReuploadAll || recordedInstagram is null;
                if (!uploadYouTube && !uploadFacebook && !uploadInstagram)
                {
                    statusText.Text = "This promo is already uploaded to all three platforms.";
                    return;
                }

                var uploadTitle = title.Text.Trim();
                var uploadDescription = description.Text.Trim();
                var uploadPrivacy = Convert.ToString(privacy.SelectedItem) ?? "private";
                var validatedVideo = SocialVideoUploadRules.ValidateVideoFile(file.Text);
                SocialVideoUploadRules.ValidateUploadMetadata(
                    "Short",
                    uploadTitle,
                    uploadDescription,
                    requireFullYouTubeVideoLink: uploadYouTube || uploadFacebook);
                if (uploadYouTube)
                    SocialVideoUploadRules.ValidatePrivacy(uploadPrivacy);

                if (uploadFacebook || uploadInstagram)
                {
                    var duration = await new NativeFfmpegTimelineService().MediaDurationAsync(validatedVideo);
                    if (uploadFacebook) SocialVideoUploadRules.ValidateFacebookDuration(duration);
                    if (uploadInstagram) SocialVideoUploadRules.ValidateInstagramDuration(duration);
                }

                var destinations = (uploadYouTube ? SocialUploadDestination.YouTube : SocialUploadDestination.None) |
                                   (uploadFacebook ? SocialUploadDestination.Facebook : SocialUploadDestination.None) |
                                   (uploadInstagram ? SocialUploadDestination.Instagram : SocialUploadDestination.None);
                var preflight = await ConfirmSocialPublishingPreflightAsync(
                    dialog,
                    destinations,
                    validatedVideo,
                    uploadTitle,
                    uploadPrivacy,
                    scheduledFor: null);
                if (preflight is null) return;

                upload.IsEnabled = false;
                cancel.IsEnabled = false;
                privacy.IsEnabled = false;
                notify.IsEnabled = false;
                title.IsEnabled = false;
                description.IsEnabled = false;
                progress.Visibility = Visibility.Visible;

                var completed = new List<string>();
                var completedLinks = new List<string>();
                var failures = new List<string>();
                var warnings = new List<string>();
                var publicationState = _data.PublicationState;

                FactburstTrackerCampaignLinks? trackingLinks = null;
                var activeTrackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
                if (activeTrackerSettings.IsConfigured)
                {
                    statusText.Text = "Registering the Factburst funnel tracking campaign...";
                    trackingLinks = await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                        activeTrackerSettings.BaseUrl,
                        activeTrackerSettings.ApiKey,
                        FactburstLinkTrackerClient.CampaignSlug(history),
                        history.Id,
                        uploadTitle,
                        history.YouTubeUrl);
                }
                var youtubeDescription = trackingLinks is null
                    ? uploadDescription
                    : FactburstLinkTrackerClient.ReplaceFullQuizLink(uploadDescription, trackingLinks.YouTubePromoUrl);
                var facebookDescription = trackingLinks is null
                    ? uploadDescription
                    : FactburstLinkTrackerClient.ReplaceFullQuizLink(uploadDescription, trackingLinks.FacebookUrl);

                if (uploadYouTube)
                {
                    statusText.Text = "Uploading the promotional Short to YouTube... Keep this window open.";
                    publicationState.BeginAttempt(
                        history.Id, PublicationPlatform.YouTube, PublicationContentKind.Promo);
                    try
                    {
                        var result = await _youtubeVideoUpload.UploadAsync(
                            preflight.YouTubeAccessToken,
                            validatedVideo,
                            new YouTubeVideoUpload(
                                uploadTitle,
                                youtubeDescription,
                                uploadPrivacy,
                                notify.IsChecked == true));

                        QuizPromoShortPublicationStore.RecordYouTube(
                            history.ProjectFolder,
                            result,
                            uploadPrivacy,
                            DateTimeOffset.Now);
                        publicationState.RecordUploaded(
                            history.Id,
                            PublicationPlatform.YouTube,
                            PublicationContentKind.Promo,
                            result.VideoId,
                            result.Url,
                            uploadPrivacy,
                            DateTimeOffset.Now,
                            "promo-upload");
                        completed.Add("YouTube");
                        completedLinks.Add("YouTube: " + result.Url);

                        statusText.Text = "Verifying the YouTube promo upload...";
                        try
                        {
                            await _youtubeManagement.VerifyUploadedVideoAsync(
                                preflight.YouTubeAccessToken,
                                result.VideoId,
                                preflight.YouTubeChannel!.Id,
                                uploadTitle,
                                uploadPrivacy);
                        }
                        catch (Exception error)
                        {
                            publicationState.RecordFailure(
                                history.Id,
                                PublicationPlatform.YouTube,
                                PublicationContentKind.Promo,
                                SocialUploadJournalStep.Verification,
                                error.Message,
                                result.VideoId,
                                result.Url,
                                "promo-upload");
                            warnings.Add("YouTube verification: " + error.Message);
                        }
                    }
                    catch (Exception error)
                    {
                        publicationState.RecordFailure(
                            history.Id,
                            PublicationPlatform.YouTube,
                            PublicationContentKind.Promo,
                            SocialUploadJournalStep.Upload,
                            error.Message,
                            source: "promo-upload");
                        failures.Add("YouTube: " + error.Message);
                    }
                }

                if (uploadFacebook)
                {
                    statusText.Text = "Uploading the promotional Reel to Facebook... Keep this window open.";
                    publicationState.BeginAttempt(
                        history.Id, PublicationPlatform.Facebook, PublicationContentKind.Promo);
                    try
                    {
                        var result = await _facebookReelUpload.UploadAsync(
                            preflight.FacebookPageToken,
                            validatedVideo,
                            uploadTitle,
                            facebookDescription);
                        QuizPromoShortSocialPublicationStore.RecordFacebook(
                            history.ProjectFolder,
                            result,
                            DateTimeOffset.Now);
                        publicationState.RecordUploaded(
                            history.Id,
                            PublicationPlatform.Facebook,
                            PublicationContentKind.Promo,
                            result.VideoId,
                            result.Url,
                            uploadedAt: DateTimeOffset.Now,
                            source: "promo-upload");
                        completed.Add("Facebook");
                        if (result.Url.Trim().Length > 0)
                            completedLinks.Add("Facebook: " + result.Url);

                        statusText.Text = "Verifying the Facebook promo Reel...";
                        try
                        {
                            await _facebookReelUpload.VerifyUploadedReelAsync(
                                preflight.FacebookPageToken,
                                result.VideoId);
                        }
                        catch (Exception error)
                        {
                            publicationState.RecordFailure(
                                history.Id,
                                PublicationPlatform.Facebook,
                                PublicationContentKind.Promo,
                                SocialUploadJournalStep.Verification,
                                error.Message,
                                result.VideoId,
                                result.Url,
                                "promo-upload");
                            warnings.Add("Facebook verification: " + error.Message);
                        }
                    }
                    catch (Exception error)
                    {
                        publicationState.RecordFailure(
                            history.Id,
                            PublicationPlatform.Facebook,
                            PublicationContentKind.Promo,
                            SocialUploadJournalStep.Upload,
                            error.Message,
                            source: "promo-upload");
                        failures.Add("Facebook: " + error.Message);
                    }
                }

                if (uploadInstagram)
                {
                    statusText.Text = "Uploading the promotional Reel to Instagram... Keep this window open.";
                    publicationState.BeginAttempt(
                        history.Id, PublicationPlatform.Instagram, PublicationContentKind.Promo);
                    try
                    {
                        var instagramCaption = SocialVideoUploadRules.InstagramCaption(uploadDescription);
                        var result = await _instagramReelUpload.UploadReelAsync(
                            preflight.FacebookPageToken,
                            validatedVideo,
                            instagramCaption);
                        QuizPromoShortSocialPublicationStore.RecordInstagram(
                            history.ProjectFolder,
                            result,
                            DateTimeOffset.Now);
                        publicationState.RecordUploaded(
                            history.Id,
                            PublicationPlatform.Instagram,
                            PublicationContentKind.Promo,
                            result.MediaId,
                            result.Url,
                            uploadedAt: DateTimeOffset.Now,
                            source: "promo-upload");
                        completed.Add("Instagram");
                        if (result.Url.Trim().Length > 0)
                            completedLinks.Add("Instagram: " + result.Url);
                    }
                    catch (Exception error)
                    {
                        publicationState.RecordFailure(
                            history.Id,
                            PublicationPlatform.Instagram,
                            PublicationContentKind.Promo,
                            SocialUploadJournalStep.Upload,
                            error.Message,
                            source: "promo-upload");
                        failures.Add("Instagram: " + error.Message);
                    }
                }

                forceReuploadAll = false;
                RefreshUploadManager();

                var links = completedLinks.Count == 0
                    ? ""
                    : "\n\n" + string.Join("\n", completedLinks);
                var trackingText = trackingLinks is null
                    ? ""
                    : "\n\nFunnel tracking:\n" +
                      "Facebook: " + trackingLinks.FacebookUrl + "\n" +
                      "Instagram link in bio: " + trackingLinks.InstagramUrl + "\n" +
                      "YouTube promo: " + trackingLinks.YouTubePromoUrl +
                      "\n\nFor Instagram attribution, use the Instagram tracking URL as the profile/bio link viewers are told to tap.";
                var warningText = warnings.Count == 0
                    ? ""
                    : "\n\nWarnings:\n" + string.Join("\n", warnings);
                if (failures.Count == 0)
                {
                    statusText.Text = "Promotional Short uploaded successfully to YouTube, Facebook, and Instagram.";
                    MessageBox.Show(dialog,
                        "Promotional Short uploaded successfully to YouTube, Facebook, and Instagram." + links + trackingText +
                        "\n\nIn YouTube Studio, select the full quiz as this Short's related video." + warningText,
                        "Promo Short Uploaded",
                        MessageBoxButton.OK,
                        warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    dialog.DialogResult = true;
                    return;
                }

                var completedText = completed.Count == 0
                    ? "No platform upload completed."
                    : "Uploaded successfully to " + string.Join(" and ", completed) + ".";
                var failureText = "\n\nStill needs uploading:\n" + string.Join("\n", failures);
                statusText.Text = completedText + " Click Upload again to retry only the missing platforms.";
                MessageBox.Show(dialog,
                    completedText + links + trackingText + failureText + warningText +
                    "\n\nClick Upload to All Platforms again to retry only the platforms that are still missing.",
                    "Promo Upload Partially Complete",
                    MessageBoxButton.OK,
                    completed.Count == 0 ? MessageBoxImage.Error : MessageBoxImage.Warning);
            }
            catch (Exception error)
            {
                statusText.Text = error.Message;
                MessageBox.Show(dialog, error.Message, "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                upload.IsEnabled = true;
                cancel.IsEnabled = true;
                privacy.IsEnabled = true;
                notify.IsEnabled = true;
                title.IsEnabled = true;
                description.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static string PromoPendingPlatforms(bool youtube, bool facebook, bool instagram)
    {
        var platforms = new List<string>();
        if (youtube) platforms.Add("YouTube");
        if (facebook) platforms.Add("Facebook");
        if (instagram) platforms.Add("Instagram");
        return platforms.Count == 0 ? "none" : string.Join(", ", platforms);
    }
}
