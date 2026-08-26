using System.Diagnostics;
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
                "Upload the full quiz to YouTube first. Its link is required for the Short description and related-video funnel.",
                "Upload Promo Short", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previousUpload = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
        if (previousUpload is not null &&
            MessageBox.Show(this,
                $"This promotional Short is already recorded as uploaded:\n\n{previousUpload.Url}\n\nUpload another copy?",
                "Promo Short Already Uploaded", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(previousUpload.Url) { UseShellExecute = true });
            return;
        }

        var dialog = new Window
        {
            Title = "Upload Promotional Short",
            Owner = this,
            Width = 760,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 860,
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
            Text = "Upload Promo Short to YouTube",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "This upload is tracked separately and will not replace the full quiz's YouTube record.",
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
        titlePanel.Children.Add(Label("YOUTUBE TITLE"));
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
            Margin = new Thickness(0, 0, 0, 14),
        };
        var descriptionPanel = new StackPanel();
        descriptionPanel.Children.Add(Label("DESCRIPTION — KEEP THE FULL-QUIZ LINK"));
        descriptionPanel.Children.Add(description);
        Grid.SetRow(descriptionPanel, 3);
        root.Children.Add(descriptionPanel);

        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        options.Children.Add(new TextBlock
        {
            Text = "Visibility",
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
            Content = "Notify subscribers",
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
        var statusText = new TextBlock
        {
            Text = "Ready. YouTube will recognise the vertical video as a Short.",
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
            Content = "Upload to YouTube",
            MinWidth = 138,
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
                var uploadTitle = title.Text.Trim();
                var uploadDescription = description.Text.Trim();
                var uploadPrivacy = Convert.ToString(privacy.SelectedItem) ?? "private";
                var validatedVideo = SocialVideoUploadRules.ValidateVideoFile(file.Text);
                SocialVideoUploadRules.ValidateUploadMetadata(
                    "Short", uploadTitle, uploadDescription, requireFullYouTubeVideoLink: true);
                SocialVideoUploadRules.ValidatePrivacy(uploadPrivacy);

                var preflight = await ConfirmSocialPublishingPreflightAsync(
                    dialog,
                    SocialUploadDestination.YouTube,
                    validatedVideo,
                    uploadTitle,
                    uploadPrivacy,
                    scheduledFor: null);
                if (preflight is null) return;

                upload.IsEnabled = false;
                cancel.IsEnabled = false;
                privacy.IsEnabled = false;
                title.IsEnabled = false;
                description.IsEnabled = false;
                progress.Visibility = Visibility.Visible;
                statusText.Text = "Uploading the promotional Short to YouTube... Keep this window open.";

                var result = await _youtubeVideoUpload.UploadAsync(
                    preflight.YouTubeAccessToken,
                    validatedVideo,
                    new YouTubeVideoUpload(
                        uploadTitle,
                        uploadDescription,
                        uploadPrivacy,
                        notify.IsChecked == true));

                var warning = "";
                statusText.Text = "Verifying the YouTube upload...";
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
                    warning = "\n\nUpload verification warning: " + error.Message;
                }

                QuizPromoShortPublicationStore.RecordYouTube(
                    history.ProjectFolder,
                    result,
                    uploadPrivacy,
                    DateTimeOffset.Now);
                RefreshUploadManager();
                statusText.Text = "Promotional Short uploaded successfully.";
                MessageBox.Show(dialog,
                    $"Promotional Short uploaded successfully.\n\n{result.Url}\n\n" +
                    "Open this Short in YouTube Studio and select the full quiz as its related video." + warning,
                    "Promo Short Uploaded",
                    MessageBoxButton.OK,
                    warning.Length == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                dialog.DialogResult = true;
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
                title.IsEnabled = true;
                description.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed;
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
