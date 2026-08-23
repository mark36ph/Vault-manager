using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly YouTubeVideoUploadService _youtubeVideoUpload = new();
    private readonly FacebookReelUploadService _facebookReelUpload = new();
    private readonly InstagramManagementService _instagramReelUpload = new();

    private void ShowSelectedQuizUpload()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(this, "Select a quiz first.", "Upload Video", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowQuizUploadDialog(history);
    }

    private void ShowQuizUploadDialog(QuizHistorySummary history)
    {
        var dialog = new Window
        {
            Title = "Upload Quiz Video",
            Owner = this,
            Width = 940,
            Height = 860,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock
        {
            Text = "Upload " + (history.VideoType == "Short" ? "Short" : "Full Video"),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            TextWrapping = TextWrapping.Wrap,
        });
        heading.Children.Add(new TextBlock
        {
            Text = history.VideoType == "Short"
                ? "Short • upload to YouTube, Facebook, Instagram, or any combination"
                : "Full video • upload to YouTube only",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(heading);

        var filePanel = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        filePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fileLabel = new TextBlock
        {
            Text = "Completed video file",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 0, 0, 5),
        };
        filePanel.Children.Add(fileLabel);
        var videoPath = new TextBox
        {
            Text = SocialVideoUploadRules.FindLikelyRenderedVideo(history.ProjectFolder) ?? "",
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        Grid.SetRow(videoPath, 1);
        filePanel.Children.Add(videoPath);
        var browse = new Button { Content = "Browse...", MinWidth = 92, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(browse, Color.FromRgb(0, 204, 255));
        browse.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Choose the completed quiz video",
                Filter = "Video files (*.mp4;*.mov;*.m4v)|*.mp4;*.mov;*.m4v|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (Directory.Exists(history.ProjectFolder)) picker.InitialDirectory = history.ProjectFolder;
            if (picker.ShowDialog(dialog) == true) videoPath.Text = picker.FileName;
        };
        Grid.SetRow(browse, 1);
        Grid.SetColumn(browse, 1);
        filePanel.Children.Add(browse);
        Grid.SetRow(filePanel, 1);
        root.Children.Add(filePanel);

        var thumbnailPanel = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        thumbnailPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        thumbnailPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        thumbnailPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        thumbnailPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        thumbnailPanel.Children.Add(new TextBlock
        {
            Text = "Thumbnail / Reel cover (optional, JPG or PNG, maximum 2 MB)",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 0, 0, 5),
        });
        var thumbnailPath = new TextBox
        {
            Text = SocialVideoUploadRules.FindLikelyThumbnail(history.ProjectFolder) ?? "",
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        Grid.SetRow(thumbnailPath, 1);
        thumbnailPanel.Children.Add(thumbnailPath);
        var browseThumbnail = new Button { Content = "Browse...", MinWidth = 92, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(browseThumbnail, Color.FromRgb(255, 190, 0));
        browseThumbnail.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Choose the thumbnail image",
                Filter = "Thumbnail images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (Directory.Exists(history.ProjectFolder)) picker.InitialDirectory = history.ProjectFolder;
            if (picker.ShowDialog(dialog) == true) thumbnailPath.Text = picker.FileName;
        };
        Grid.SetRow(browseThumbnail, 1);
        Grid.SetColumn(browseThumbnail, 1);
        thumbnailPanel.Children.Add(browseThumbnail);
        Grid.SetRow(thumbnailPanel, 2);
        root.Children.Add(thumbnailPanel);

        var destinations = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 221, 235)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var destinationContent = new Grid();
        destinationContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        destinationContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        destinationContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        destinationContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        destinationContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var youtubePanel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        var youtube = new CheckBox
        {
            Content = history.PublishedOnYouTube ? "YouTube (already published)" : "Upload to YouTube",
            IsChecked = !history.PublishedOnYouTube,
            IsEnabled = !history.PublishedOnYouTube,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        };
        youtubePanel.Children.Add(youtube);
        youtubePanel.Children.Add(new TextBlock
        {
            Text = history.VideoType == "Short" ? "This will be recognised as a Short by YouTube." : "This will upload as a full YouTube video.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 4, 0, 8),
        });
        var privacy = new ComboBox { Width = 130, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(22, 0, 0, 8) };
        privacy.Items.Add("private");
        privacy.Items.Add("unlisted");
        privacy.Items.Add("public");
        privacy.SelectedItem = "private";
        youtubePanel.Children.Add(privacy);
        var notify = new CheckBox { Content = "Notify subscribers", IsChecked = true, Margin = new Thickness(22, 0, 0, 0) };
        youtubePanel.Children.Add(notify);
        destinationContent.Children.Add(youtubePanel);

        var facebookPanel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var facebookAllowed = SocialVideoUploadRules.CanUploadToFacebook(history);
        var facebook = new CheckBox
        {
            Content = !facebookAllowed
                ? "Facebook (Shorts only)"
                : history.PublishedOnFacebook ? "Facebook (already published)" : "Upload to Facebook",
            IsChecked = facebookAllowed && !history.PublishedOnFacebook,
            IsEnabled = facebookAllowed && !history.PublishedOnFacebook,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        };
        facebookPanel.Children.Add(facebook);
        facebookPanel.Children.Add(new TextBlock
        {
            Text = facebookAllowed
                ? "Publishes this vertical video as a Reel on Factburst Quiz. Requires pages_manage_posts."
                : "Full videos are never sent to Facebook.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 4, 0, 0),
        });
        Grid.SetColumn(facebookPanel, 1);
        destinationContent.Children.Add(facebookPanel);

        var instagramPanel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var instagramAllowed = string.Equals(history.VideoType, "Short", StringComparison.Ordinal);
        var instagram = new CheckBox
        {
            Content = !instagramAllowed
                ? "Instagram (Shorts only)"
                : history.PublishedOnInstagram ? "Instagram (already published)" : "Upload to Instagram",
            IsChecked = instagramAllowed && !history.PublishedOnInstagram,
            IsEnabled = instagramAllowed && !history.PublishedOnInstagram,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
        };
        instagramPanel.Children.Add(instagram);
        instagramPanel.Children.Add(new TextBlock
        {
            Text = instagramAllowed
                ? "Publishes this vertical video as an Instagram Reel through the linked Facebook Page."
                : "Full videos are never sent to Instagram.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 4, 0, 0),
        });
        Grid.SetColumn(instagramPanel, 2);
        destinationContent.Children.Add(instagramPanel);

        var schedulePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var schedule = new CheckBox
        {
            Content = "Schedule publication",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        var scheduleDate = new DatePicker
        {
            SelectedDate = DateTime.Today.AddDays(1),
            Width = 145,
            Height = 32,
            IsEnabled = false,
        };
        var scheduleAt = new TextBlock
        {
            Text = "at",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = QuizMutedBrush(),
        };
        var scheduleTime = new TextBox
        {
            Text = "18:00",
            Width = 64,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = false,
            ToolTip = "Local time in 24-hour HH:mm format",
        };
        var scheduleZone = new TextBlock
        {
            Text = "local time",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = QuizMutedBrush(),
        };
        schedulePanel.Children.Add(schedule);
        schedulePanel.Children.Add(scheduleDate);
        schedulePanel.Children.Add(scheduleAt);
        schedulePanel.Children.Add(scheduleTime);
        schedulePanel.Children.Add(scheduleZone);
        Grid.SetRow(schedulePanel, 1);
        Grid.SetColumnSpan(schedulePanel, 3);
        destinationContent.Children.Add(schedulePanel);
        schedule.Checked += (_, _) =>
        {
            scheduleDate.IsEnabled = true;
            scheduleTime.IsEnabled = true;
            privacy.SelectedItem = "private";
            privacy.IsEnabled = false;
        };
        schedule.Unchecked += (_, _) =>
        {
            scheduleDate.IsEnabled = false;
            scheduleTime.IsEnabled = false;
            privacy.IsEnabled = youtube.IsEnabled;
        };
        destinations.Child = destinationContent;
        Grid.SetRow(destinations, 3);
        root.Children.Add(destinations);

        var metadataPanel = new Grid();
        metadataPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metadataPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metadataPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metadataPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        metadataPanel.Children.Add(new TextBlock
        {
            Text = "Upload title",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 0, 0, 5),
        });
        var titleBox = new TextBox
        {
            Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title,
            MinHeight = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        Grid.SetRow(titleBox, 1);
        metadataPanel.Children.Add(titleBox);
        var descriptionLabel = new TextBlock
        {
            Text = history.VideoType == "Short"
                ? "Description (must keep the full-video YouTube link)"
                : "Description",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 10, 0, 5),
        };
        Grid.SetRow(descriptionLabel, 2);
        metadataPanel.Children.Add(descriptionLabel);
        var descriptionBox = new TextBox
        {
            Text = SocialVideoUploadRules.UploadDescription(history),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            Padding = new Thickness(10),
            ToolTip = "Saved publishing description, full-video link, and hashtags",
        };
        Grid.SetRow(descriptionBox, 3);
        metadataPanel.Children.Add(descriptionBox);
        Grid.SetRow(metadataPanel, 4);
        root.Children.Add(metadataPanel);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var statusText = new TextBlock { Text = "Choose the final rendered video, then upload.", Foreground = QuizMutedBrush(), TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { Height = 6, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 16, 0) };
        status.Children.Add(statusText);
        status.Children.Add(progress);
        footer.Children.Add(status);
        var cancel = new Button { Content = "Cancel", MinWidth = 82, MinHeight = 36, IsCancel = true };
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        var uploadButton = new Button { Content = "Upload", MinWidth = 104, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        StyleQuizHistoryButton(uploadButton, Color.FromRgb(70, 235, 115));
        uploadButton.IsEnabled = youtube.IsEnabled || facebook.IsEnabled || instagram.IsEnabled;
        Grid.SetColumn(uploadButton, 2);
        footer.Children.Add(uploadButton);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);

        uploadButton.Click += async (_, _) =>
        {
            var uploadYouTube = youtube.IsChecked == true;
            var uploadFacebook = facebook.IsChecked == true;
            var uploadInstagram = instagram.IsChecked == true;
            if (!uploadYouTube && !uploadFacebook && !uploadInstagram)
            {
                MessageBox.Show(dialog, "Choose YouTube, Facebook, Instagram, or a combination.", "Upload Video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var completed = new List<string>();
            var thumbnailWarnings = new List<string>();
            try
            {
                var file = SocialVideoUploadRules.ValidateVideoFile(videoPath.Text);
                var thumbnail = SocialVideoUploadRules.ValidateThumbnailFile(thumbnailPath.Text);
                var title = titleBox.Text.Trim();
                var description = descriptionBox.Text.Trim();
                SocialVideoUploadRules.ValidateUploadMetadata(history.VideoType, title, description);
                if (uploadInstagram && schedule.IsChecked == true)
                    throw new InvalidOperationException("Scheduled Instagram publishing is not available yet. Turn off scheduling or deselect Instagram.");
                var scheduledFor = SocialVideoUploadRules.ResolveScheduledPublishAt(
                    schedule.IsChecked == true,
                    scheduleDate.SelectedDate,
                    scheduleTime.Text,
                    DateTimeOffset.Now,
                    uploadFacebook);
                uploadButton.IsEnabled = false;
                browse.IsEnabled = false;
                browseThumbnail.IsEnabled = false;
                schedule.IsEnabled = false;
                scheduleDate.IsEnabled = false;
                scheduleTime.IsEnabled = false;
                cancel.IsEnabled = false;
                progress.Visibility = Visibility.Visible;

                if (uploadYouTube)
                {
                    statusText.Text = scheduledFor is null
                        ? "Uploading to YouTube... Keep this window open."
                        : "Uploading to YouTube for scheduled publication... Keep this window open.";
                    var accessToken = await GetYouTubeManagementAccessTokenAsync();
                    var result = await _youtubeVideoUpload.UploadAsync(
                        accessToken,
                        file,
                        new YouTubeVideoUpload(
                            title,
                            description,
                            Convert.ToString(privacy.SelectedItem) ?? "private",
                            notify.IsChecked == true,
                            scheduledFor));
                    _data.UpdateQuizHistoryYouTubeAnalytics(
                        history.Id, true, result.Url, 0, 0, scheduledFor?.LocalDateTime.Date ?? DateTime.Today);
                    completed.Add("YouTube");
                    if (thumbnail is not null)
                    {
                        statusText.Text = "Setting the YouTube thumbnail...";
                        try { await _youtubeVideoUpload.SetThumbnailAsync(accessToken, result.VideoId, thumbnail); }
                        catch (Exception error) { thumbnailWarnings.Add("YouTube thumbnail: " + error.Message); }
                    }
                    youtube.IsChecked = false;
                    youtube.IsEnabled = false;
                    youtube.Content = "YouTube (uploaded)";
                }

                if (uploadFacebook)
                {
                    if (!SocialVideoUploadRules.CanUploadToFacebook(history))
                        throw new InvalidOperationException("Only Shorts can be uploaded to Facebook.");
                    var duration = await new NativeFfmpegTimelineService().MediaDurationAsync(file);
                    SocialVideoUploadRules.ValidateFacebookDuration(duration);
                    statusText.Text = scheduledFor is null
                        ? "Uploading the Short to Facebook... Keep this window open."
                        : "Uploading the Short to Facebook for scheduled publication... Keep this window open.";
                    var pageToken = FacebookPageToken();
                    var result = await _facebookReelUpload.UploadAsync(
                        pageToken,
                        file,
                        title,
                        description,
                        scheduledFor);
                    _data.UpdateQuizHistoryFacebookAnalytics(
                        history.Id, true, result.Url, 0, 0, 0, 0, scheduledFor?.LocalDateTime.Date ?? DateTime.Today);
                    completed.Add("Facebook");
                    if (thumbnail is not null)
                    {
                        statusText.Text = "Setting the Facebook Reel cover...";
                        try { await _facebookReelUpload.SetThumbnailAsync(pageToken, result.VideoId, thumbnail); }
                        catch (Exception error) { thumbnailWarnings.Add("Facebook Reel cover: " + error.Message); }
                    }
                    facebook.IsChecked = false;
                    facebook.IsEnabled = false;
                    facebook.Content = "Facebook (uploaded)";
                }

                if (uploadInstagram)
                {
                    if (!string.Equals(history.VideoType, "Short", StringComparison.Ordinal))
                        throw new InvalidOperationException("Only Shorts can be uploaded to Instagram.");
                    var duration = await new NativeFfmpegTimelineService().MediaDurationAsync(file);
                    SocialVideoUploadRules.ValidateInstagramDuration(duration);
                    statusText.Text = "Uploading the Short to Instagram... Keep this window open.";
                    var pageToken = FacebookPageToken();
                    var instagramCaption = SocialVideoUploadRules.InstagramCaption(description);
                    var result = await _instagramReelUpload.UploadReelAsync(
                        pageToken,
                        file,
                        instagramCaption);
                    _data.UpdateQuizHistoryInstagramPublication(
                        history.Id, true, result.Url, DateTime.Today);
                    completed.Add("Instagram");
                    instagram.IsChecked = false;
                    instagram.IsEnabled = false;
                    instagram.Content = "Instagram (uploaded)";
                }

                RefreshQuizHistory();
                var warningText = thumbnailWarnings.Count == 0
                    ? ""
                    : "\n\nThe video upload succeeded, but:\n" + string.Join("\n", thumbnailWarnings);
                var completion = scheduledFor is null
                    ? "Uploaded successfully"
                    : $"Uploaded and scheduled for {scheduledFor.Value:dd-MM-yyyy HH:mm}";
                statusText.Text = completion + ": " + string.Join(" and ", completed) +
                                  (thumbnailWarnings.Count == 0 ? "." : ". Thumbnail warning shown.");
                MessageBox.Show(dialog,
                    $"{completion} on {string.Join(" and ", completed)}.{warningText}",
                    scheduledFor is null ? "Upload Complete" : "Upload Scheduled", MessageBoxButton.OK,
                    thumbnailWarnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                dialog.DialogResult = true;
            }
            catch (Exception error)
            {
                RefreshQuizHistory();
                var prefix = completed.Count == 0
                    ? ""
                    : $"Uploaded successfully to {string.Join(" and ", completed)}, but the remaining upload failed.\n\n";
                statusText.Text = prefix + error.Message;
                MessageBox.Show(dialog, prefix + error.Message, "Upload Video", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                uploadButton.IsEnabled = youtube.IsEnabled || facebook.IsEnabled || instagram.IsEnabled;
                browse.IsEnabled = true;
                browseThumbnail.IsEnabled = true;
                schedule.IsEnabled = true;
                scheduleDate.IsEnabled = schedule.IsChecked == true;
                scheduleTime.IsEnabled = schedule.IsChecked == true;
                cancel.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
