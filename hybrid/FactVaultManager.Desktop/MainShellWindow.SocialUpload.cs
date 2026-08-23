using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private readonly YouTubeVideoUploadService _youtubeVideoUpload = new();
    private readonly FacebookReelUploadService _facebookReelUpload = new();

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
            Width = 760,
            Height = 610,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 253)),
        };
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock
        {
            Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title,
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            TextWrapping = TextWrapping.Wrap,
        });
        heading.Children.Add(new TextBlock
        {
            Text = history.VideoType == "Short"
                ? "Short • upload to YouTube, Facebook, or both"
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
        destinations.Child = destinationContent;
        Grid.SetRow(destinations, 2);
        root.Children.Add(destinations);

        var metadata = new TextBox
        {
            Text = SocialVideoUploadRules.UploadDescription(history),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            Padding = new Thickness(10),
            ToolTip = "Saved publishing description and hashtags",
        };
        Grid.SetRow(metadata, 3);
        root.Children.Add(metadata);

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
        uploadButton.IsEnabled = youtube.IsEnabled || facebook.IsEnabled;
        Grid.SetColumn(uploadButton, 2);
        footer.Children.Add(uploadButton);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        uploadButton.Click += async (_, _) =>
        {
            var uploadYouTube = youtube.IsChecked == true;
            var uploadFacebook = facebook.IsChecked == true;
            if (!uploadYouTube && !uploadFacebook)
            {
                MessageBox.Show(dialog, "Choose YouTube, Facebook, or both.", "Upload Video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var completed = new List<string>();
            try
            {
                var file = SocialVideoUploadRules.ValidateVideoFile(videoPath.Text);
                uploadButton.IsEnabled = false;
                browse.IsEnabled = false;
                cancel.IsEnabled = false;
                progress.Visibility = Visibility.Visible;
                var title = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title;
                var description = SocialVideoUploadRules.UploadDescription(history);

                if (uploadYouTube)
                {
                    statusText.Text = "Uploading to YouTube... Keep this window open.";
                    var accessToken = await GetYouTubeManagementAccessTokenAsync();
                    var result = await _youtubeVideoUpload.UploadAsync(
                        accessToken,
                        file,
                        new YouTubeVideoUpload(
                            title,
                            description,
                            Convert.ToString(privacy.SelectedItem) ?? "private",
                            notify.IsChecked == true));
                    _data.UpdateQuizHistoryYouTubeAnalytics(history.Id, true, result.Url, 0, 0, DateTime.Today);
                    completed.Add("YouTube");
                    youtube.IsChecked = false;
                    youtube.IsEnabled = false;
                    youtube.Content = "YouTube (uploaded)";
                }

                if (uploadFacebook)
                {
                    if (!SocialVideoUploadRules.CanUploadToFacebook(history))
                        throw new InvalidOperationException("Only Shorts can be uploaded to Facebook.");
                    statusText.Text = "Uploading the Short to Facebook... Keep this window open.";
                    var result = await _facebookReelUpload.UploadAsync(
                        FacebookPageToken(),
                        file,
                        title,
                        description);
                    _data.UpdateQuizHistoryFacebookAnalytics(history.Id, true, result.Url, 0, 0, 0, 0, DateTime.Today);
                    completed.Add("Facebook");
                    facebook.IsChecked = false;
                    facebook.IsEnabled = false;
                    facebook.Content = "Facebook (uploaded)";
                }

                RefreshQuizHistory();
                statusText.Text = "Upload complete: " + string.Join(" and ", completed) + ".";
                MessageBox.Show(dialog,
                    $"Uploaded successfully to {string.Join(" and ", completed)}.",
                    "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
                uploadButton.IsEnabled = youtube.IsEnabled || facebook.IsEnabled;
                browse.IsEnabled = true;
                cancel.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
