using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public sealed record YouTubeAnalyticsRow(
    int HistoryId,
    string Type,
    string Quiz,
    string Uploaded,
    long Views,
    long Likes,
    long Comments,
    double EngagementRate,
    string Url)
{
    public string EngagementDisplay => EngagementRate.ToString("0.00'%'", CultureInfo.InvariantCulture);
}

public static class YouTubeAnalyticsMetrics
{
    public static double EngagementRate(long views, long likes, long comments) =>
        views <= 0 ? 0 : (Math.Max(0, likes) + Math.Max(0, comments)) * 100.0 / views;
}

public partial class MainShellWindow
{
    private bool _youtubeAnalyticsPageInitialized;
    private bool _youtubeAnalyticsPageRefreshing;
    private int _youtubeAnalyticsTabIndex = -1;
    private DataGrid? _youtubeAnalyticsGrid;
    private TextBlock? _youtubeSubscribersText;
    private TextBlock? _youtubeChannelViewsText;
    private TextBlock? _youtubeChannelVideosText;
    private TextBlock? _youtubeEngagementText;
    private TextBlock? _youtubeAnalyticsPageStatus;
    private TextBlock? _youtubeChannelNameText;

    private void InitializeYouTubeAnalyticsPage()
    {
        if (_youtubeAnalyticsPageInitialized || MainTabs is null)
            return;

        _youtubeAnalyticsPageInitialized = true;
        var tab = new TabItem { Content = BuildYouTubeAnalyticsPage() };
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                await RefreshYouTubeAnalyticsPageAsync(false);
        };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
            tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _youtubeAnalyticsTabIndex = MainTabs.Items.Count - 1;
        AddYouTubeAnalyticsNavigationButton(_youtubeAnalyticsTabIndex);
    }

    private FrameworkElement BuildYouTubeAnalyticsPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "YouTube Analytics",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        _youtubeChannelNameText = new TextBlock
        {
            Text = "Channel and tracked quiz performance",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        };
        heading.Children.Add(_youtubeChannelNameText);
        header.Children.Add(heading);

        var refresh = new Button
        {
            Content = "Refresh from YouTube",
            MinWidth = 156,
            MinHeight = 36,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshYouTubeAnalyticsPageAsync(true);
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var subscribers = BuildQuizHistoryStatCard("Subscribers", Color.FromRgb(248, 90, 105));
        _youtubeSubscribersText = subscribers.Value;
        subscribers.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(subscribers.Card);

        var views = BuildQuizHistoryStatCard("Channel views", Color.FromRgb(0, 204, 255));
        _youtubeChannelViewsText = views.Value;
        views.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(views.Card, 1);
        stats.Children.Add(views.Card);

        var videos = BuildQuizHistoryStatCard("Channel videos", Color.FromRgb(204, 70, 255));
        _youtubeChannelVideosText = videos.Value;
        videos.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(videos.Card, 2);
        stats.Children.Add(videos.Card);

        var engagement = BuildQuizHistoryStatCard("Tracked engagement", Color.FromRgb(70, 235, 115));
        _youtubeEngagementText = engagement.Value;
        engagement.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(engagement.Card, 3);
        stats.Children.Add(engagement.Card);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _youtubeAnalyticsGrid = BuildYouTubeAnalyticsGrid();
        var tableCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255),
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.4,
            },
            Child = _youtubeAnalyticsGrid,
        };
        Grid.SetRow(tableCard, 2);
        root.Children.Add(tableCard);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _youtubeAnalyticsPageStatus = new TextBlock
        {
            Text = "Open this page to update analytics.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(_youtubeAnalyticsPageStatus);
        var open = new Button { Content = "Open selected video", MinWidth = 142, MinHeight = 36 };
        StyleQuizHistoryButton(open, Color.FromRgb(255, 202, 45));
        open.Click += (_, _) => OpenSelectedYouTubeAnalyticsVideo();
        Grid.SetColumn(open, 1);
        footer.Children.Add(open);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private DataGrid BuildYouTubeAnalyticsGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            MinRowHeight = 42,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(15, 31, 86)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(12, 25, 72)),
        };

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Triggers.Add(selected);
        grid.CellStyle = cellStyle;

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9)));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255))));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        grid.ColumnHeaderStyle = headerStyle;

        grid.Columns.Add(TextColumn("Type", nameof(YouTubeAnalyticsRow.Type), 78));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(YouTubeAnalyticsRow.Quiz)),
            SortMemberPath = nameof(YouTubeAnalyticsRow.Quiz),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(TextColumn("Uploaded", nameof(YouTubeAnalyticsRow.Uploaded), 104));
        grid.Columns.Add(NumberColumn("Views", nameof(YouTubeAnalyticsRow.Views), 92));
        grid.Columns.Add(NumberColumn("Likes", nameof(YouTubeAnalyticsRow.Likes), 82));
        grid.Columns.Add(NumberColumn("Comments", nameof(YouTubeAnalyticsRow.Comments), 94));
        grid.Columns.Add(TextColumn("Engagement", nameof(YouTubeAnalyticsRow.EngagementDisplay), 110));
        grid.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.ChangedButton == MouseButton.Left)
                OpenSelectedYouTubeAnalyticsVideo();
        };
        return grid;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        SortMemberPath = property,
        Width = new DataGridLength(width),
    };

    private static DataGridTextColumn NumberColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property) { StringFormat = "N0" },
        SortMemberPath = property,
        Width = new DataGridLength(width),
    };

    private void AddYouTubeAnalyticsNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;
        var notesButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizNotesTabIndex.ToString(), StringComparison.Ordinal));
        if (notesButton?.Parent is not StackPanel navigation)
            return;

        var analyticsButton = new Button { Content = "▶   YouTube Analytics", Tag = tabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle)
            analyticsButton.Style = navStyle;
        analyticsButton.Click += Navigate_Click;
        var notesIndex = navigation.Children.IndexOf(notesButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, notesIndex + 1), analyticsButton);
    }

    private async Task RefreshYouTubeAnalyticsPageAsync(bool showErrors)
    {
        if (_youtubeAnalyticsPageRefreshing || _youtubeAnalyticsGrid is null)
            return;

        var apiKey = _data.LoadSettings().YouTubeApiKey.Trim();
        if (apiKey.Length == 0)
        {
            SetYouTubeAnalyticsStatus("Add the API key in Settings → YouTube.");
            return;
        }

        var linked = _data.GetQuizHistory()
            .Where(item => item.PublishedOnYouTube && !string.IsNullOrWhiteSpace(item.YouTubeUrl))
            .Select(item => new { History = item, VideoId = YouTubeVideoAnalyticsService.TryGetVideoId(item.YouTubeUrl) })
            .Where(item => item.VideoId is not null)
            .ToList();
        if (linked.Count == 0)
        {
            SetYouTubeAnalyticsStatus("No published quizzes have a saved YouTube video link.");
            return;
        }

        try
        {
            _youtubeAnalyticsPageRefreshing = true;
            SetYouTubeAnalyticsStatus("Updating channel and video analytics...");
            var videos = await _youtubeVideoAnalytics.FetchAsync(apiKey, linked.Select(item => item.VideoId!));
            var rows = new List<YouTubeAnalyticsRow>();
            foreach (var item in linked)
            {
                if (!videos.TryGetValue(item.VideoId!, out var video))
                    continue;

                _data.UpdateQuizHistoryYouTubeMetrics(item.History.Id, video.Views, video.Likes, video.PublishedAt);
                var quizName = item.History.YouTubeTitle.Trim();
                if (quizName.Length == 0) quizName = video.Title.Length > 0 ? video.Title : item.History.Title;
                rows.Add(new YouTubeAnalyticsRow(
                    item.History.Id,
                    item.History.VideoType,
                    quizName,
                    video.PublishedAt?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "",
                    video.Views,
                    video.Likes,
                    video.Comments,
                    YouTubeAnalyticsMetrics.EngagementRate(video.Views, video.Likes, video.Comments),
                    item.History.YouTubeUrl));
            }

            _youtubeAnalyticsGrid.ItemsSource = rows.OrderByDescending(row => row.Views).ToList();
            var channelId = videos.Values.Select(video => video.ChannelId).FirstOrDefault(id => id.Length > 0) ?? "";
            var channel = await _youtubeVideoAnalytics.FetchChannelAsync(apiKey, channelId);
            _youtubeSubscribersText!.Text = channel?.Subscribers?.ToString("N0") ?? "Hidden";
            _youtubeChannelViewsText!.Text = channel?.Views.ToString("N0") ?? "—";
            _youtubeChannelVideosText!.Text = channel?.Videos.ToString("N0") ?? "—";
            _youtubeChannelNameText!.Text = channel is null || channel.Title.Length == 0
                ? "Channel and tracked quiz performance"
                : $"{channel.Title} • channel and tracked quiz performance";

            var totalViews = rows.Sum(row => row.Views);
            var totalLikes = rows.Sum(row => row.Likes);
            var totalComments = rows.Sum(row => row.Comments);
            _youtubeEngagementText!.Text =
                YouTubeAnalyticsMetrics.EngagementRate(totalViews, totalLikes, totalComments)
                    .ToString("0.00'%'", CultureInfo.InvariantCulture);
            RefreshQuizHistory();
            SetYouTubeAnalyticsStatus(
                $"Updated {rows.Count:N0} tracked videos • {totalViews:N0} views • {totalLikes:N0} likes • {totalComments:N0} comments");
        }
        catch (Exception error)
        {
            Debug.WriteLine($"YouTube analytics page: {error.Message}");
            SetYouTubeAnalyticsStatus("YouTube analytics update failed.");
            if (showErrors)
                MessageBox.Show(this, error.Message, "YouTube Analytics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _youtubeAnalyticsPageRefreshing = false;
        }
    }

    private void SetYouTubeAnalyticsStatus(string text)
    {
        if (_youtubeAnalyticsPageStatus is not null)
            _youtubeAnalyticsPageStatus.Text = text;
    }

    private void OpenSelectedYouTubeAnalyticsVideo()
    {
        if (_youtubeAnalyticsGrid?.SelectedItem is not YouTubeAnalyticsRow row || row.Url.Length == 0)
            return;
        Process.Start(new ProcessStartInfo(row.Url) { UseShellExecute = true });
    }
}
