using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryDialogRestoreUiRegistered = RegisterQuizHistoryDialogRestoreUi();

    private static bool RegisterQuizHistoryDialogRestoreUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizHistoryDialogRestoreUi_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizHistoryDialogRestoreUi_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShellWindow window || e.OriginalSource is not MainShellWindow)
            return;

        var questionsButton = FindVisualChildren<Button>(window)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Questions", StringComparison.Ordinal));
        if (questionsButton is not null && questionsButton.Tag?.ToString() != "quiz-history-restored")
        {
            questionsButton.Tag = "quiz-history-restored";
            questionsButton.Click += (_, _) => window.ShowSelectedQuizHistoryQuestionsRestored();
        }
    }

    private void ShowSelectedQuizHistoryQuestionsRestored()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
            return;

        var questions = _data.GetQuizHistoryQuestions(history.Id);
        var dialog = new Window
        {
            Title = $"Questions — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 1080,
            Height = 700,
            MinWidth = 780,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        var series = string.IsNullOrWhiteSpace(history.SeriesName)
            ? "Unnumbered legacy export"
            : $"{history.SeriesName} {history.EpisodeLabel}";
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock
        {
            Text = series,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"{history.CreatedDisplay}  •  {history.QuestionCount} questions  •  {history.Format}  •  {history.QuestionSeconds} seconds per question",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"Categories: {history.Categories}",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 13, 16, 13),
            Child = summary,
        });

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            AlternationCount = 2,
            AlternatingRowBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
            MinRowHeight = 42,
            Margin = new Thickness(0, 14, 0, 12),
            ItemsSource = questions,
        };
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        grid.CellStyle = cellStyle;
        var questionTextStyle = new Style(typeof(TextBlock));
        questionTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        questionTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(QuizHistoryQuestion.Position)), Width = new DataGridLength(52) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Bank No.", Binding = new Binding(nameof(QuizHistoryQuestion.QuestionId)), Width = new DataGridLength(82) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding(nameof(QuizHistoryQuestion.Category)), Width = new DataGridLength(150) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new Binding(nameof(QuizHistoryQuestion.Difficulty)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Question", Binding = new Binding(nameof(QuizHistoryQuestion.Question)), ElementStyle = questionTextStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var close = new Button { Content = "Close", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);
        dialog.ShowDialog();
    }

    private void ShowQuizPublishingMetadata(QuizHistorySummary history, bool manageComments = false)
    {
        var dialog = new Window
        {
            Title = manageComments
                ? $"First Comments — {history.SeriesName} {history.EpisodeLabel}"
                : $"Publishing Metadata — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 760,
            Height = 680,
            MinWidth = 620,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = stack;
        dialog.Content = scroll;
        stack.Children.Add(new TextBlock { Text = $"{history.SeriesName} {history.EpisodeLabel}", FontSize = 22, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = manageComments
                ? "Post the saved first comment when each platform is ready, then open it to pin manually."
                : "Publishing metadata saved with this successful quiz export.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 12),
        });
        AddQuizHistoryPublishingField(stack, "YouTube title", history.YouTubeTitle, 58);
        AddQuizHistoryPublishingField(stack, "Description", history.YouTubeDescription, 180);
        AddQuizHistoryPublishingField(stack, "Hashtags", history.Hashtags, 58);
        AddQuizHistoryPublishingField(stack, "First comment", history.PinnedComment, 95);

        var youtubeFirstCommentId = history.YouTubeFirstCommentId;
        var facebookFirstCommentId = history.FacebookFirstCommentId;
        var firstCommentStatusHeading = new TextBlock
        {
            Text = "First-comment status",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
            Visibility = manageComments ? Visibility.Visible : Visibility.Collapsed,
        };
        stack.Children.Add(firstCommentStatusHeading);
        var firstCommentStatus = new TextBlock
        {
            Foreground = QuizMutedBrush(),
            Visibility = manageComments ? Visibility.Visible : Visibility.Collapsed,
        };
        void UpdateFirstCommentStatus() => firstCommentStatus.Text =
            $"YouTube: {(youtubeFirstCommentId.Length > 0 ? "Posted — ready to pin" : "Not posted")}   •   " +
            $"Facebook: {(facebookFirstCommentId.Length > 0 ? "Posted — ready to pin" : "Not posted")}";
        UpdateFirstCommentStatus();
        stack.Children.Add(firstCommentStatus);

        var pinActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = manageComments ? Visibility.Visible : Visibility.Collapsed,
        };
        var openYouTubeToPin = new Button
        {
            Content = "Open YouTube to pin",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            IsEnabled = youtubeFirstCommentId.Length > 0 && history.YouTubeUrl.Length > 0,
        };
        openYouTubeToPin.Click += (_, _) =>
        {
            var videoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl);
            if (videoId is not null)
                Process.Start(new ProcessStartInfo(YouTubeManagementService.BuildCommentUrl(videoId, youtubeFirstCommentId)) { UseShellExecute = true });
        };
        pinActions.Children.Add(openYouTubeToPin);
        var openFacebookToPin = new Button
        {
            Content = "Open Facebook to pin",
            MinHeight = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = facebookFirstCommentId.Length > 0 && history.FacebookUrl.Length > 0,
        };
        openFacebookToPin.Click += (_, _) => Process.Start(new ProcessStartInfo(history.FacebookUrl) { UseShellExecute = true });
        pinActions.Children.Add(openFacebookToPin);
        stack.Children.Add(pinActions);

        var postMissingComments = new Button
        {
            Content = "Post missing first comments",
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, 10, 0, 0),
            IsEnabled = history.PinnedComment.Trim().Length > 0 &&
                (history.PublishedOnYouTube && !history.YouTubeIsScheduled && youtubeFirstCommentId.Length == 0 ||
                 history.PublishedOnFacebook && !history.FacebookIsScheduled && facebookFirstCommentId.Length == 0),
            ToolTip = "Posts the saved first comment only where this quiz is already published and no comment ID is recorded.",
            Visibility = manageComments ? Visibility.Visible : Visibility.Collapsed,
        };
        StyleQuizHistoryButton(postMissingComments, System.Windows.Media.Color.FromRgb(70, 235, 115));
        postMissingComments.Click += async (_, _) =>
        {
            postMissingComments.IsEnabled = false;
            var posted = new List<string>();
            var errors = new List<string>();
            if (history.PublishedOnYouTube && !history.YouTubeIsScheduled && youtubeFirstCommentId.Length == 0)
            {
                var videoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl);
                if (videoId is null) errors.Add("YouTube: the saved video URL does not contain a usable video ID.");
                else
                {
                    try
                    {
                        firstCommentStatus.Text = "Posting the first YouTube comment...";
                        var token = await GetYouTubeManagementAccessTokenAsync();
                        youtubeFirstCommentId = await _youtubeManagement.PostTopLevelCommentAsync(token, videoId, history.PinnedComment);
                        _data.UpdateQuizHistoryYouTubeFirstComment(history.Id, youtubeFirstCommentId);
                        posted.Add("YouTube");
                    }
                    catch (Exception error) { errors.Add("YouTube: " + error.Message); }
                }
            }
            if (history.PublishedOnFacebook && !history.FacebookIsScheduled && facebookFirstCommentId.Length == 0)
            {
                var videoId = FacebookReelAnalyticsService.TryGetReelId(history.FacebookUrl);
                if (videoId is null) errors.Add("Facebook: the saved Reel URL does not contain a usable video ID.");
                else
                {
                    try
                    {
                        firstCommentStatus.Text = "Posting the first Facebook comment...";
                        facebookFirstCommentId = await _facebookComments.PostTopLevelCommentAsync(FacebookPageToken(), videoId, history.PinnedComment);
                        _data.UpdateQuizHistoryFacebookFirstComment(history.Id, facebookFirstCommentId);
                        posted.Add("Facebook");
                    }
                    catch (Exception error) { errors.Add("Facebook: " + error.Message); }
                }
            }
            UpdateFirstCommentStatus();
            openYouTubeToPin.IsEnabled = youtubeFirstCommentId.Length > 0 && history.YouTubeUrl.Length > 0;
            openFacebookToPin.IsEnabled = facebookFirstCommentId.Length > 0 && history.FacebookUrl.Length > 0;
            postMissingComments.IsEnabled = history.PinnedComment.Trim().Length > 0 &&
                (history.PublishedOnYouTube && !history.YouTubeIsScheduled && youtubeFirstCommentId.Length == 0 ||
                 history.PublishedOnFacebook && !history.FacebookIsScheduled && facebookFirstCommentId.Length == 0);
            RefreshQuizHistory();
            var resultText = posted.Count > 0 ? "Posted successfully to " + string.Join(" and ", posted) + "." : "No first comments were posted.";
            if (errors.Count > 0) resultText += "\n\n" + string.Join("\n", errors);
            MessageBox.Show(dialog, resultText, "Post First Comments", MessageBoxButton.OK, errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        };
        stack.Children.Add(postMissingComments);

        var copy = new Button { Content = "Copy all metadata", MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 12, 0, 0) };
        copy.Click += (_, _) => Clipboard.SetText($"TITLE\n{history.YouTubeTitle}\n\nDESCRIPTION\n{history.YouTubeDescription}\n\nHASHTAGS\n{history.Hashtags}\n\nFIRST COMMENT\n{history.PinnedComment}");
        stack.Children.Add(copy);
        dialog.ShowDialog();
    }

    private static void AddQuizHistoryPublishingField(Panel parent, string label, string value, double height)
    {
        parent.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        parent.Children.Add(new TextBox
        {
            Text = value,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = height,
        });
    }

    private void ShowQuizYouTubePublication(QuizHistorySummary history)
    {
        var dialog = new Window
        {
            Title = $"YouTube Analytics — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 650,
            Height = 455,
            MinWidth = 570,
            MinHeight = 420,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(18) };
        for (var i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;
        root.Children.Add(new TextBlock { Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var published = new CheckBox { Content = "Published on YouTube", IsChecked = history.PublishedOnYouTube, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(published, 1); root.Children.Add(published);
        var label = new TextBlock { Text = "YouTube video link", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(label, 2); root.Children.Add(label);
        var url = new TextBox { Text = history.YouTubeUrl, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(url, 3); root.Children.Add(url);
        var analytics = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        analytics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        analytics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        analytics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        analytics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        analytics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var viewsPanel = new StackPanel(); viewsPanel.Children.Add(new TextBlock { Text = "Views", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var views = new TextBox { Text = history.YouTubeViews.ToString(System.Globalization.CultureInfo.InvariantCulture), MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center }; viewsPanel.Children.Add(views); analytics.Children.Add(viewsPanel);
        var likesPanel = new StackPanel(); likesPanel.Children.Add(new TextBlock { Text = "Likes", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var likes = new TextBox { Text = history.YouTubeLikes.ToString(System.Globalization.CultureInfo.InvariantCulture), MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center }; likesPanel.Children.Add(likes); Grid.SetColumn(likesPanel, 2); analytics.Children.Add(likesPanel);
        var uploadPanel = new StackPanel(); uploadPanel.Children.Add(new TextBlock { Text = "Upload date", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var uploadDate = new DatePicker { SelectedDate = QuizYouTubeAnalytics.ParseUploadDate(history.YouTubeUploadDate), SelectedDateFormat = DatePickerFormat.Short, MinHeight = 34 }; uploadPanel.Children.Add(uploadDate); Grid.SetColumn(uploadPanel, 4); analytics.Children.Add(uploadPanel);
        Grid.SetRow(analytics, 4); root.Children.Add(analytics);
        var hint = new TextBlock { Text = "These figures update from YouTube when you use Refresh. You can still correct them manually.", Foreground = QuizMutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(hint, 5); root.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var open = new Button { Content = "Open video", MinWidth = 95 }; open.Click += (_, _) => { try { var videoUrl = QuizYouTubePublication.NormalizeUrl(url.Text); if (videoUrl.Length == 0) throw new InvalidOperationException("Enter the YouTube video link first."); Process.Start(new ProcessStartInfo(videoUrl) { UseShellExecute = true }); } catch (Exception error) { MessageBox.Show(dialog, error.Message, "Open YouTube Video", MessageBoxButton.OK, MessageBoxImage.Error); } }; actions.Children.Add(open);
        actions.Children.Add(new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        var save = new Button { Content = "Save", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsDefault = true }; save.Click += (_, _) => { try { var viewCount = QuizYouTubeAnalytics.ParseMetric(views.Text, "Views"); var likeCount = QuizYouTubeAnalytics.ParseMetric(likes.Text, "Likes"); if (!_data.UpdateQuizHistoryYouTubeAnalytics(history.Id, published.IsChecked == true, url.Text, viewCount, likeCount, uploadDate.SelectedDate)) throw new InvalidOperationException("The selected quiz-history entry no longer exists."); dialog.DialogResult = true; RefreshQuizHistory(); } catch (Exception error) { MessageBox.Show(dialog, error.Message, "Save YouTube Analytics", MessageBoxButton.OK, MessageBoxImage.Error); } }; actions.Children.Add(save);
        Grid.SetRow(actions, 6); root.Children.Add(actions);
        dialog.ShowDialog();
    }
}
