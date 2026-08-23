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

public sealed record YouTubeQuizChoice(string VideoId, string Title)
{
    public string Display => Title;
}

public static class YouTubeAnalyticsMetrics
{
    public static double EngagementRate(long views, long likes, long comments) =>
        views <= 0 ? 0 : (Math.Max(0, likes) + Math.Max(0, comments)) * 100.0 / views;

    public static long PreserveHighest(long stored, long fetched) =>
        Math.Max(0, Math.Max(stored, fetched));
}

public sealed record YouTubeNextQuizRecommendation(string Category, string VideoType, string Reason)
{
    public string Display => $"{Category} Quiz — {VideoType}";
}

public static class YouTubeNextQuizPlanner
{
    public static YouTubeNextQuizRecommendation Recommend(
        IReadOnlyList<QuizHistorySummary> history,
        IEnumerable<string> availableCategories)
    {
        var categories = availableCategories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categories.Count == 0)
            categories.Add("General Knowledge");

        var published = history.Where(item => item.PublishedOnYouTube).ToList();
        if (published.Count == 0)
        {
            var first = categories.FirstOrDefault(category =>
                string.Equals(category, "General Knowledge", StringComparison.OrdinalIgnoreCase))
                ?? categories[0];
            return new YouTubeNextQuizRecommendation(
                first,
                "Video",
                $"{first} does not yet have a published full quiz.");
        }

        var videoCount = published.Count(item => item.VideoType == "Video");
        var shortCount = published.Count(item => item.VideoType == "Short");
        var videoType = videoCount == shortCount
            ? (published[0].VideoType == "Video" ? "Short" : "Video")
            : videoCount < shortCount ? "Video" : "Short";

        var recentCategory = FindCategory(published[0], categories);
        var candidates = categories
            .Select((category, order) => new
            {
                Category = category,
                Order = order,
                TypeCount = published.Count(item =>
                    item.VideoType == videoType &&
                    string.Equals(FindCategory(item, categories), category, StringComparison.OrdinalIgnoreCase)),
                TotalCount = published.Count(item =>
                    string.Equals(FindCategory(item, categories), category, StringComparison.OrdinalIgnoreCase)),
                Views = published
                    .Where(item => string.Equals(FindCategory(item, categories), category, StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.YouTubeViews),
            })
            .ToList();

        var minimum = candidates.Min(item => item.TypeCount);
        var leastUsed = candidates.Where(item => item.TypeCount == minimum).ToList();
        if (leastUsed.Count > 1)
        {
            var alternatives = leastUsed
                .Where(item => !string.Equals(item.Category, recentCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (alternatives.Count > 0)
                leastUsed = alternatives;
        }

        var choice = leastUsed
            .OrderBy(item => item.TotalCount)
            .ThenByDescending(item => item.Views)
            .ThenBy(item => item.Order)
            .First();
        var typeLabel = videoType == "Short" ? "Short" : "full video";
        var reason = choice.TypeCount == 0
            ? $"{choice.Category} does not yet have a published {typeLabel}."
            : $"{choice.Category} has the fewest published {typeLabel}s ({choice.TypeCount:N0}).";
        return new YouTubeNextQuizRecommendation(choice.Category, videoType, reason);
    }

    private static string FindCategory(QuizHistorySummary item, IReadOnlyList<string> categories)
    {
        var series = item.SeriesName.Trim();
        if (series.EndsWith(" Quiz", StringComparison.OrdinalIgnoreCase))
            series = series[..^5].Trim();
        var seriesMatch = categories.FirstOrDefault(category =>
            string.Equals(category, series, StringComparison.OrdinalIgnoreCase));
        if (seriesMatch is not null)
            return seriesMatch;

        var stored = item.Categories
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return categories.FirstOrDefault(category =>
                   stored.Any(value => string.Equals(value, category, StringComparison.OrdinalIgnoreCase)))
               ?? "General Knowledge";
    }
}

public partial class MainShellWindow
{
    private bool _youtubeAnalyticsPageInitialized;
    private bool _youtubeAnalyticsPageRefreshing;
    private int _youtubeAnalyticsTabIndex = -1;
    private DataGrid? _youtubeAnalyticsGrid;
    private TextBlock? _youtubeTrackedVideosText;
    private TextBlock? _youtubeTrackedViewsText;
    private TextBlock? _youtubeTrackedLikesText;
    private TextBlock? _youtubeTrackedCommentsText;
    private TextBlock? _youtubeTopCategoryText;
    private TextBlock? _youtubeNextQuizText;
    private TextBlock? _youtubeNextQuizReasonText;
    private TextBlock? _youtubeAnalyticsPageStatus;
    private TextBlock? _youtubeChannelNameText;
    private readonly YouTubeManagementService _youtubeManagement = new();
    private ContentControl? _youtubeManagerContent;
    private readonly Dictionary<string, Button> _youtubeManagerButtons = new(StringComparer.OrdinalIgnoreCase);
    private string _youtubeManagerSection = "analytics";
    private DataGrid? _youtubeCommentsGrid;
    private ComboBox? _youtubeCommentStatus;
    private TextBox? _youtubeReplyText;
    private TextBlock? _youtubeNeedsReplyCountText;
    private readonly HashSet<string> _youtubeHandledCommentIds = new(StringComparer.Ordinal);
    private TextBlock? _youtubeCommentsStatus;
    private DataGrid? _youtubePlaylistsGrid;
    private DataGrid? _youtubePlaylistVideosGrid;
    private ComboBox? _youtubeQuizVideoChoice;
    private TextBlock? _youtubeQuizVideoEmptyText;
    private IReadOnlyList<YouTubeQuizChoice> _youtubeAllQuizVideoChoices = Array.Empty<YouTubeQuizChoice>();
    private readonly Dictionary<string, IReadOnlyList<YouTubePlaylistVideo>> _youtubePlaylistVideoCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _youtubePlaylistVideoIds = new(StringComparer.Ordinal);
    private YouTubeManagerCacheStore? _youtubeManagerCacheStore;
    private ComboBox? _youtubePlaylistPrivacyChoice;
    private TextBlock? _youtubePlaylistsStatus;

    private void InitializeYouTubeAnalyticsPage()
    {
        if (_youtubeAnalyticsPageInitialized || MainTabs is null)
            return;

        _youtubeAnalyticsPageInitialized = true;
        var tab = new TabItem { Content = BuildYouTubeAnalyticsPage() };
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                await RefreshCurrentYouTubeManagerSectionAsync(false);
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

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(new TextBlock
        {
            Text = "YouTube Manager",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55,
            },
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Review performance, manage comments, and organise channel playlists.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        root.Children.Add(heading);

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        AddYouTubeManagerButton(navigation, "analytics", "Analytics");
        AddYouTubeManagerButton(navigation, "comments", "Comments");
        AddYouTubeManagerButton(navigation, "playlists", "Playlists");
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);

        _youtubeManagerContent = new ContentControl();
        Grid.SetRow(_youtubeManagerContent, 2);
        root.Children.Add(_youtubeManagerContent);
        SelectYouTubeManagerSection("analytics");
        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.58),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private FrameworkElement BuildYouTubeAnalyticsSection()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Text = "Analytics",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        _youtubeChannelNameText = new TextBlock
        {
            Text = "Channel and tracked quiz performance",
            Foreground = QuizMutedBrush(),
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
        for (var index = 0; index < 5; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var videos = BuildQuizHistoryStatCard("Tracked videos", Color.FromRgb(248, 90, 105));
        _youtubeTrackedVideosText = videos.Value;
        videos.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(videos.Card);

        var views = BuildQuizHistoryStatCard("YouTube views", Color.FromRgb(0, 204, 255));
        _youtubeTrackedViewsText = views.Value;
        views.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(views.Card, 1);
        stats.Children.Add(views.Card);

        var likes = BuildQuizHistoryStatCard("YouTube likes", Color.FromRgb(248, 90, 105));
        _youtubeTrackedLikesText = likes.Value;
        likes.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(likes.Card, 2);
        stats.Children.Add(likes.Card);

        var comments = BuildQuizHistoryStatCard("Tracked comments", Color.FromRgb(204, 70, 255));
        _youtubeTrackedCommentsText = comments.Value;
        comments.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(comments.Card, 3);
        stats.Children.Add(comments.Card);

        var topCategory = BuildQuizHistoryStatCard("Top category by views", Color.FromRgb(70, 235, 115));
        _youtubeTopCategoryText = topCategory.Value;
        _youtubeTopCategoryText.FontSize = 20;
        topCategory.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(topCategory.Card, 4);
        stats.Children.Add(topCategory.Card);

        var savedStatistics = QuizHistoryStatistics.Calculate(_data.GetQuizHistory());
        _youtubeTrackedViewsText.Text = savedStatistics.Views.ToString("N0");
        _youtubeTrackedLikesText.Text = savedStatistics.Likes.ToString("N0");
        _youtubeTopCategoryText.Text = savedStatistics.TopCategory;

        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        var nextQuiz = BuildQuizHistoryStatCard("Next quiz to create", Color.FromRgb(255, 202, 45));
        _youtubeNextQuizText = nextQuiz.Value;
        _youtubeNextQuizText.FontSize = 22;
        if (nextQuiz.Card.Child is StackPanel nextQuizContent)
        {
            _youtubeNextQuizReasonText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(184, 201, 235)),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            nextQuizContent.Children.Add(_youtubeNextQuizReasonText);
        }
        nextQuiz.Card.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(nextQuiz.Card, 2);
        root.Children.Add(nextQuiz.Card);
        RefreshYouTubeRecommendation();

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
        Grid.SetRow(tableCard, 3);
        root.Children.Add(tableCard);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _youtubeAnalyticsPageStatus = new TextBlock
        {
            Text = "Open this page to update analytics.",
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(_youtubeAnalyticsPageStatus);
        var open = new Button { Content = "Open selected video", MinWidth = 142, MinHeight = 36 };
        StyleQuizHistoryButton(open, Color.FromRgb(255, 202, 45));
        open.Click += (_, _) => OpenSelectedYouTubeAnalyticsVideo();
        Grid.SetColumn(open, 1);
        footer.Children.Add(open);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
    }

    private void AddYouTubeManagerButton(Panel parent, string key, string text)
    {
        var button = new Button { Content = text, MinWidth = 112, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        if (FindResource("YouTubeManagerTabButtonStyle") is Style managerButtonStyle)
            button.Style = managerButtonStyle;
        button.Click += async (_, _) =>
        {
            SelectYouTubeManagerSection(key);
            await RefreshCurrentYouTubeManagerSectionAsync(false);
        };
        _youtubeManagerButtons[key] = button;
        parent.Children.Add(button);
    }

    private void SelectYouTubeManagerSection(string key)
    {
        if (_youtubeManagerContent is null) return;
        _youtubeManagerSection = key;
        _youtubeManagerContent.Content = key switch
        {
            "comments" => BuildYouTubeCommentsSection(),
            "playlists" => BuildYouTubePlaylistsSection(),
            _ => BuildYouTubeAnalyticsSection(),
        };
        foreach (var pair in _youtubeManagerButtons)
        {
            var selected = pair.Key == key;
            pair.Value.Tag = selected ? "Selected" : null;
        }
    }

    private Task RefreshCurrentYouTubeManagerSectionAsync(bool showErrors) => _youtubeManagerSection switch
    {
        "comments" => RefreshYouTubeCommentsAsync(showErrors),
        "playlists" => RefreshYouTubePlaylistsAsync(showErrors),
        _ => RefreshYouTubeAnalyticsPageAsync(showErrors),
    };

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

    private static DataGridTemplateColumn WrappedTextColumn(string header, string property, DataGridLength width)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(property));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 4));
        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = new DataTemplate { VisualTree = text },
            SortMemberPath = property,
            Width = width,
        };
    }

    private DataGridTemplateColumn YouTubeCommentVideoColumn()
    {
        var link = new FrameworkElementFactory(typeof(TextBlock));
        link.SetBinding(TextBlock.TextProperty, new Binding(nameof(YouTubeCommentItem.VideoTitle)));
        link.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        link.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        link.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255)));
        link.SetValue(TextBlock.CursorProperty, Cursors.Hand);
        link.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        link.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 4));
        link.SetValue(TextBlock.ToolTipProperty, "Open this video on YouTube");
        link.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(YouTubeCommentVideo_MouseLeftButtonUp));
        return new DataGridTemplateColumn
        {
            Header = "Video",
            CellTemplate = new DataTemplate { VisualTree = link },
            SortMemberPath = nameof(YouTubeCommentItem.VideoTitle),
            Width = new DataGridLength(250),
        };
    }

    private DataGridTemplateColumn YouTubeCommentAuthorColumn()
    {
        var link = new FrameworkElementFactory(typeof(TextBlock));
        link.SetBinding(TextBlock.TextProperty, new Binding(nameof(YouTubeCommentItem.Author)));
        link.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        link.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        link.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255)));
        link.SetValue(TextBlock.CursorProperty, Cursors.Hand);
        link.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        link.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 4));
        link.SetValue(TextBlock.ToolTipProperty, "Open this author's YouTube profile");
        link.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(YouTubeCommentAuthor_MouseLeftButtonUp));
        return new DataGridTemplateColumn
        {
            Header = "Author",
            CellTemplate = new DataTemplate { VisualTree = link },
            SortMemberPath = nameof(YouTubeCommentItem.Author),
            Width = new DataGridLength(132),
        };
    }

    private void YouTubeCommentVideo_MouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: YouTubeCommentItem comment } ||
            string.IsNullOrWhiteSpace(comment.VideoId))
            return;

        var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(comment.VideoId.Trim());
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        eventArgs.Handled = true;
    }

    private void YouTubeCommentAuthor_MouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: YouTubeCommentItem comment } ||
            !Uri.TryCreate(comment.AuthorProfileUrl, UriKind.Absolute, out var profileUri))
            return;

        var host = profileUri.Host.TrimEnd('.');
        if (profileUri.Scheme != Uri.UriSchemeHttps ||
            !(string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
              host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)))
            return;

        Process.Start(new ProcessStartInfo(profileUri.AbsoluteUri) { UseShellExecute = true });
        eventArgs.Handled = true;
    }

    private FrameworkElement BuildYouTubeCommentsSection()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Comments", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)) });
        title.Children.Add(new TextBlock { Text = "Reply to viewers and review comments held by YouTube.", Foreground = QuizMutedBrush() });
        toolbar.Children.Add(title);
        _youtubeNeedsReplyCountText = new TextBlock
        {
            Text = "0",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 202, 45)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var needsReplyBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 18, 78)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 202, 45)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Needs reply  ",
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    _youtubeNeedsReplyCountText,
                },
            },
        };
        Grid.SetColumn(needsReplyBadge, 1);
        toolbar.Children.Add(needsReplyBadge);

        _youtubeCommentStatus = new ComboBox { Width = 150, Height = 34, Margin = new Thickness(8, 0, 8, 0) };
        _youtubeCommentStatus.Items.Add("Needs reply");
        _youtubeCommentStatus.Items.Add("Published");
        _youtubeCommentStatus.Items.Add("Held for review");
        _youtubeCommentStatus.Items.Add("Likely spam");
        _youtubeCommentStatus.SelectedIndex = 1;
        _youtubeCommentStatus.SelectionChanged += async (_, _) => await RefreshYouTubeCommentsAsync(false);
        Grid.SetColumn(_youtubeCommentStatus, 2);
        toolbar.Children.Add(_youtubeCommentStatus);
        var refresh = new Button { Content = "Refresh comments", MinWidth = 132, MinHeight = 34 };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshYouTubeCommentsAsync(true);
        Grid.SetColumn(refresh, 3);
        toolbar.Children.Add(refresh);
        root.Children.Add(toolbar);

        _youtubeCommentsGrid = BuildManagerGrid();
        _youtubeCommentsGrid.RowHeight = double.NaN;
        _youtubeCommentsGrid.MinRowHeight = 44;
        _youtubeCommentsGrid.Columns.Add(YouTubeCommentAuthorColumn());
        _youtubeCommentsGrid.Columns.Add(YouTubeCommentVideoColumn());
        _youtubeCommentsGrid.Columns.Add(WrappedTextColumn(
            "Comment",
            nameof(YouTubeCommentItem.Text),
            new DataGridLength(1, DataGridLengthUnitType.Star)));
        _youtubeCommentsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Published",
            Binding = new Binding(nameof(YouTubeCommentItem.PublishedAt)) { StringFormat = "dd-MM-yyyy HH:mm" },
            Width = new DataGridLength(132),
        });
        _youtubeCommentsGrid.Columns.Add(NumberColumn("Likes", nameof(YouTubeCommentItem.LikeCount), 62));
        _youtubeCommentsGrid.Columns.Add(NumberColumn("Replies", nameof(YouTubeCommentItem.ReplyCount), 68));
        var commentsCard = ManagerCard(_youtubeCommentsGrid);
        Grid.SetRow(commentsCard, 1);
        root.Children.Add(commentsCard);

        var actionRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _youtubeReplyText = new TextBox { MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        actionRow.Children.Add(_youtubeReplyText);
        AddCommentAction(actionRow, 1, "Open to like", Color.FromRgb(204, 70, 255), () =>
        {
            OpenSelectedYouTubeComment();
            return Task.CompletedTask;
        });
        AddCommentAction(actionRow, 2, "Reply", Color.FromRgb(0, 204, 255), () => ReplyToSelectedCommentAsync());
        AddCommentAction(actionRow, 3, "Approve", Color.FromRgb(70, 235, 115), () => ModerateSelectedCommentAsync("published"));
        AddCommentAction(actionRow, 4, "Hold", Color.FromRgb(255, 202, 45), () => ModerateSelectedCommentAsync("heldForReview"));
        AddCommentAction(actionRow, 5, "Reject", Color.FromRgb(248, 90, 105), () => ModerateSelectedCommentAsync("rejected"));
        Grid.SetRow(actionRow, 2);
        root.Children.Add(actionRow);

        _youtubeCommentsStatus = new TextBlock { Text = "Connect YouTube in Settings to manage comments.", Foreground = QuizMutedBrush(), Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(_youtubeCommentsStatus, 3);
        root.Children.Add(_youtubeCommentsStatus);
        return root;
    }

    private FrameworkElement BuildYouTubePlaylistsSection()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Playlists", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        title.Children.Add(new TextBlock
        {
            Text = "Category playlists are created automatically as private. Select one to change its privacy.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
        });
        toolbar.Children.Add(title);
        var create = new Button { Content = "Create playlist", MinWidth = 124, MinHeight = 34, Margin = new Thickness(8, 0, 8, 0) };
        StyleQuizHistoryButton(create, Color.FromRgb(204, 70, 255));
        create.Click += async (_, _) => await CreateYouTubePlaylistAsync();
        Grid.SetColumn(create, 1);
        toolbar.Children.Add(create);
        var refresh = new Button { Content = "Refresh playlists", MinWidth = 132, MinHeight = 34 };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshYouTubePlaylistsAsync(true);
        Grid.SetColumn(refresh, 2);
        toolbar.Children.Add(refresh);
        root.Children.Add(toolbar);

        var tables = new Grid();
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        tables.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
        _youtubePlaylistsGrid = BuildManagerGrid();
        _youtubePlaylistsGrid.Columns.Add(new DataGridTextColumn { Header = "Playlist", Binding = new Binding(nameof(YouTubePlaylistItem.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _youtubePlaylistsGrid.Columns.Add(TextColumn("Privacy", nameof(YouTubePlaylistItem.Privacy), 84));
        _youtubePlaylistsGrid.Columns.Add(NumberColumn("Videos", nameof(YouTubePlaylistItem.VideoCount), 72));
        _youtubePlaylistsGrid.SelectionChanged += async (_, _) =>
        {
            SyncSelectedPlaylistPrivacyChoice();
            await RefreshSelectedPlaylistVideosAsync(false);
        };
        tables.Children.Add(ManagerCard(_youtubePlaylistsGrid));
        _youtubePlaylistVideosGrid = BuildManagerGrid();
        _youtubePlaylistVideosGrid.Columns.Add(new DataGridTextColumn { Header = "Videos in selected playlist", Binding = new Binding(nameof(YouTubePlaylistVideo.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _youtubePlaylistVideosGrid.Columns.Add(NumberColumn("Position", nameof(YouTubePlaylistVideo.Position), 80));
        var videosCard = ManagerCard(_youtubePlaylistVideosGrid);
        Grid.SetColumn(videosCard, 2);
        tables.Children.Add(videosCard);
        Grid.SetRow(tables, 1);
        root.Children.Add(tables);

        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 4; index++)
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _youtubeQuizVideoChoice = new ComboBox { MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), DisplayMemberPath = nameof(YouTubeQuizChoice.Display) };
        actions.Children.Add(_youtubeQuizVideoChoice);
        _youtubeQuizVideoEmptyText = new TextBlock
        {
            Text = "No full videos are available to add to a playlist.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        actions.Children.Add(_youtubeQuizVideoEmptyText);
        var add = new Button { Content = "Add quiz video", MinWidth = 124, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        StyleQuizHistoryButton(add, Color.FromRgb(70, 235, 115));
        add.Click += async (_, _) => await AddSelectedQuizToPlaylistAsync();
        Grid.SetColumn(add, 1);
        actions.Children.Add(add);
        var remove = new Button { Content = "Remove video", MinWidth = 116, MinHeight = 36 };
        StyleQuizHistoryButton(remove, Color.FromRgb(248, 90, 105));
        remove.Click += async (_, _) => await RemoveSelectedPlaylistVideoAsync();
        Grid.SetColumn(remove, 2);
        actions.Children.Add(remove);

        _youtubePlaylistPrivacyChoice = new ComboBox
        {
            MinWidth = 104,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _youtubePlaylistPrivacyChoice.Items.Add("Private");
        _youtubePlaylistPrivacyChoice.Items.Add("Public");
        _youtubePlaylistPrivacyChoice.SelectedIndex = 0;
        Grid.SetColumn(_youtubePlaylistPrivacyChoice, 3);
        actions.Children.Add(_youtubePlaylistPrivacyChoice);

        var updatePrivacy = new Button { Content = "Update privacy", MinWidth = 120, MinHeight = 36 };
        StyleQuizHistoryButton(updatePrivacy, Color.FromRgb(204, 70, 255));
        updatePrivacy.Click += async (_, _) => await UpdateSelectedPlaylistPrivacyAsync();
        Grid.SetColumn(updatePrivacy, 4);
        actions.Children.Add(updatePrivacy);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        _youtubePlaylistsStatus = new TextBlock { Text = "Connect YouTube in Settings to manage playlists.", Foreground = QuizMutedBrush(), Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(_youtubePlaylistsStatus, 3);
        root.Children.Add(_youtubePlaylistsStatus);
        return root;
    }

    private static DataGrid BuildManagerGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(15, 31, 86)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(12, 25, 72)),
            MinRowHeight = 40,
        };
        var header = new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        grid.ColumnHeaderStyle = header;

        var cell = new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
        cell.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cell.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cell.Triggers.Add(selected);
        grid.CellStyle = cell;
        return grid;
    }

    private static Border ManagerCard(UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(12),
        Child = content,
    };

    private void AddCommentAction(Grid row, int column, string label, Color colour, Func<Task> action)
    {
        var button = new Button { Content = label, MinWidth = 82, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        StyleQuizHistoryButton(button, colour);
        button.Click += async (_, _) => await action();
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private async Task<string> GetYouTubeManagementAccessTokenAsync()
    {
        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthClientId.Length == 0 || settings.YouTubeOAuthRefreshToken.Length == 0)
            throw new InvalidOperationException("Open Settings → YouTube, enter the OAuth desktop client details, then connect your Google account.");
        return await _youtubeOAuth.RefreshAccessTokenAsync(
            settings.YouTubeOAuthClientId,
            settings.YouTubeOAuthClientSecret,
            settings.YouTubeOAuthRefreshToken);
    }

    private async Task RefreshYouTubeCommentsAsync(bool showErrors)
    {
        if (_youtubeCommentsGrid is null) return;
        try
        {
            SetYouTubeCommentsStatus("Loading comments...");
            var token = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(token);
            var selection = _youtubeCommentStatus?.SelectedItem?.ToString() ?? "Needs reply";

            var published = await _youtubeManagement.ListCommentsAsync(token, channel.Id, "published");
            var needsReply = YouTubeCommentInbox.Filter(published, needsReply: true, handledCommentIds: _youtubeHandledCommentIds);
            if (_youtubeNeedsReplyCountText is not null)
                _youtubeNeedsReplyCountText.Text = needsReply.Count.ToString("N0");

            IReadOnlyList<YouTubeCommentItem> comments;
            if (selection == "Needs reply")
            {
                comments = needsReply;
            }
            else if (selection == "Published")
            {
                comments = YouTubeCommentInbox.Filter(published, needsReply: false);
            }
            else
            {
                var status = selection == "Held for review" ? "heldForReview" : "likelySpam";
                var moderated = await _youtubeManagement.ListCommentsAsync(token, channel.Id, status);
                comments = YouTubeCommentInbox.Filter(moderated, needsReply: false);
            }

            _youtubeCommentsGrid.ItemsSource = comments;
            SetYouTubeCommentsStatus(selection == "Needs reply"
                ? $"{channel.Title} • {comments.Count:N0} viewer comments need a reply"
                : $"{channel.Title} • {comments.Count:N0} {selection.ToLowerInvariant()} comments");
        }
        catch (Exception error)
        {
            SetYouTubeCommentsStatus(error.Message);
            if (showErrors) MessageBox.Show(this, error.Message, "YouTube Comments", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelectedYouTubeComment()
    {
        if (_youtubeCommentsGrid?.SelectedItem is not YouTubeCommentItem comment)
        {
            MessageBox.Show(this, "Select a comment first.", "YouTube Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var url = YouTubeManagementService.BuildCommentUrl(comment.VideoId, comment.Id);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task ReplyToSelectedCommentAsync()
    {
        if (_youtubeCommentsGrid?.SelectedItem is not YouTubeCommentItem comment)
        {
            MessageBox.Show(this, "Select a comment first.", "YouTube Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var reply = _youtubeReplyText?.Text.Trim() ?? "";
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.ReplyAsync(token, comment.Id, reply);
            _youtubeHandledCommentIds.Add(comment.Id);
            if (_youtubeReplyText is not null) _youtubeReplyText.Clear();
            await RefreshYouTubeCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Reply to Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ModerateSelectedCommentAsync(string status)
    {
        if (_youtubeCommentsGrid?.SelectedItem is not YouTubeCommentItem comment)
        {
            MessageBox.Show(this, "Select a comment first.", "YouTube Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var action = status switch { "published" => "approve", "heldForReview" => "hold for review", _ => "reject" };
        if (MessageBox.Show(this, $"Do you want to {action} this comment from {comment.Author}?", "Confirm Comment Action", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.SetModerationStatusAsync(token, comment.Id, status);
            await RefreshYouTubeCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Moderate Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshYouTubePlaylistsAsync(bool showErrors)
    {
        if (_youtubePlaylistsGrid is null) return;

        var accountKey = CurrentYouTubeCacheAccountKey();
        var cached = YouTubeManagerCacheStoreInstance.Load(accountKey);
        if (cached is not null)
        {
            ApplyYouTubePlaylistSnapshot(cached);
            if (!showErrors && YouTubeManagerCacheStore.IsFresh(cached, DateTime.UtcNow))
            {
                SetYouTubePlaylistsStatus(
                    $"Loaded {cached.Playlists.Count:N0} playlists from cache. Use Refresh playlists to check YouTube now.");
                return;
            }

            SetYouTubePlaylistsStatus("Showing cached playlists while checking YouTube...");
        }
        else
        {
            SetYouTubePlaylistsStatus("Loading playlists...");
        }

        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            var playlists = await _youtubeManagement.ListPlaylistsAsync(token);
            var missingCategories = YouTubeCategoryPlaylistPlanner.MissingCategories(
                QuizQuestionTopicCategorizer.Categories,
                playlists);
            foreach (var category in missingCategories)
            {
                await _youtubeManagement.CreatePlaylistAsync(
                    token,
                    YouTubeCategoryPlaylistPlanner.PlaylistTitle(category),
                    $"Quiz videos for the {category} category.",
                    "private");
            }
            if (missingCategories.Count > 0)
                playlists = await _youtubeManagement.ListPlaylistsAsync(token);

            SetYouTubePlaylistsStatus("Checking videos across all playlists...");
            await RefreshAllYouTubePlaylistVideoIdsAsync(token, playlists);
            BindYouTubePlaylistData(playlists);
            TrySaveYouTubePlaylistCache(accountKey, playlists);
            SetYouTubePlaylistsStatus(missingCategories.Count == 0
                ? $"Loaded {playlists.Count:N0} playlists. All category playlists are ready."
                : $"Created {missingCategories.Count:N0} private category playlists and loaded {playlists.Count:N0} playlists.");
        }
        catch (Exception error)
        {
            SetYouTubePlaylistsStatus(cached is null
                ? error.Message
                : "Could not refresh YouTube. Cached playlists are still shown.");
            if (showErrors) MessageBox.Show(this, error.Message, "YouTube Playlists", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshSelectedPlaylistVideosAsync(bool showErrors)
    {
        if (_youtubePlaylistVideosGrid is null || _youtubePlaylistsGrid?.SelectedItem is not YouTubePlaylistItem playlist)
            return;

        if (!showErrors && _youtubePlaylistVideoCache.TryGetValue(playlist.Id, out var cachedVideos))
        {
            BindSelectedYouTubePlaylistVideos(playlist, cachedVideos);
            return;
        }

        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            var videos = await _youtubeManagement.ListPlaylistVideosAsync(token, playlist.Id);
            _youtubePlaylistVideoCache[playlist.Id] = videos;
            RebuildYouTubePlaylistVideoIds();
            BindSelectedYouTubePlaylistVideos(playlist, videos);
            TrySaveYouTubePlaylistCache(
                CurrentYouTubeCacheAccountKey(),
                CurrentYouTubePlaylists());
        }
        catch (Exception error)
        {
            SetYouTubePlaylistsStatus(error.Message);
            if (showErrors) MessageBox.Show(this, error.Message, "YouTube Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CreateYouTubePlaylistAsync()
    {
        var result = ShowCreatePlaylistDialog();
        if (result is null) return;
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.CreatePlaylistAsync(token, result.Value.Title, result.Value.Description, result.Value.Privacy);
            await RefreshYouTubePlaylistsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private YouTubeManagerCacheStore YouTubeManagerCacheStoreInstance =>
        _youtubeManagerCacheStore ??= new YouTubeManagerCacheStore(_data.DatabasePath);

    private string CurrentYouTubeCacheAccountKey()
    {
        var settings = _data.LoadSettings();
        return YouTubeManagerCacheStore.CreateAccountKey(
            settings.YouTubeOAuthClientId,
            settings.YouTubeOAuthRefreshToken);
    }

    private IReadOnlyList<YouTubePlaylistItem> CurrentYouTubePlaylists() =>
        (_youtubePlaylistsGrid?.ItemsSource as IEnumerable<YouTubePlaylistItem>)?.ToList()
        ?? new List<YouTubePlaylistItem>();

    private void ApplyYouTubePlaylistSnapshot(YouTubeManagerCacheSnapshot snapshot)
    {
        _youtubePlaylistVideoCache.Clear();
        foreach (var pair in snapshot.PlaylistVideos)
            _youtubePlaylistVideoCache[pair.Key] = pair.Value;
        RebuildYouTubePlaylistVideoIds();
        BindYouTubePlaylistData(snapshot.Playlists);
    }

    private void BindYouTubePlaylistData(IReadOnlyList<YouTubePlaylistItem> playlists)
    {
        if (_youtubePlaylistsGrid is null)
            return;

        _youtubePlaylistsGrid.ItemsSource = playlists;
        _youtubeAllQuizVideoChoices = _data.GetQuizHistory()
            .Where(item => item.PublishedOnYouTube)
            .Where(item => string.Equals(item.VideoType, "Video", StringComparison.OrdinalIgnoreCase))
            .Select(item => new YouTubeQuizChoice(
                YouTubeVideoAnalyticsService.TryGetVideoId(item.YouTubeUrl) ?? "",
                item.YouTubeTitle.Length > 0 ? item.YouTubeTitle : item.Title))
            .Where(item => item.VideoId.Length > 0)
            .GroupBy(item => item.VideoId)
            .Select(group => group.First())
            .OrderBy(item => item.Title)
            .ToList();
        ApplyAvailableYouTubeQuizChoices();

        if (playlists.Count > 0)
            _youtubePlaylistsGrid.SelectedIndex = 0;
        else if (_youtubePlaylistVideosGrid is not null)
            _youtubePlaylistVideosGrid.ItemsSource = Array.Empty<YouTubePlaylistVideo>();
    }

    private void BindSelectedYouTubePlaylistVideos(
        YouTubePlaylistItem playlist,
        IReadOnlyList<YouTubePlaylistVideo> videos)
    {
        if (_youtubePlaylistVideosGrid is not null)
            _youtubePlaylistVideosGrid.ItemsSource = videos.OrderBy(item => item.Position).ToList();
        ApplyAvailableYouTubeQuizChoices();
        SetYouTubePlaylistsStatus($"{playlist.Title} • {videos.Count:N0} videos");
    }

    private void TrySaveYouTubePlaylistCache(
        string accountKey,
        IReadOnlyList<YouTubePlaylistItem> playlists)
    {
        if (accountKey.Length == 0)
            return;

        try
        {
            YouTubeManagerCacheStoreInstance.Save(new YouTubeManagerCacheSnapshot
            {
                AccountKey = accountKey,
                RefreshedAtUtc = DateTime.UtcNow,
                Playlists = playlists.ToList(),
                PlaylistVideos = _youtubePlaylistVideoCache.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.Ordinal),
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"YouTube playlist cache: {error.Message}");
        }
    }

    private async Task RefreshAllYouTubePlaylistVideoIdsAsync(
        string accessToken,
        IEnumerable<YouTubePlaylistItem> playlists)
    {
        _youtubePlaylistVideoCache.Clear();
        foreach (var playlist in playlists)
        {
            var videos = await _youtubeManagement.ListPlaylistVideosAsync(accessToken, playlist.Id);
            _youtubePlaylistVideoCache[playlist.Id] = videos;
        }
        RebuildYouTubePlaylistVideoIds();
    }

    private void RebuildYouTubePlaylistVideoIds()
    {
        _youtubePlaylistVideoIds.Clear();
        foreach (var videos in _youtubePlaylistVideoCache.Values)
        {
            foreach (var video in videos)
            {
                if (video.VideoId.Length > 0)
                    _youtubePlaylistVideoIds.Add(video.VideoId);
            }
        }
    }

    private void ApplyAvailableYouTubeQuizChoices()
    {
        if (_youtubeQuizVideoChoice is null)
            return;

        var available = _youtubeAllQuizVideoChoices
            .Where(choice => !_youtubePlaylistVideoIds.Contains(choice.VideoId))
            .ToList();

        _youtubeQuizVideoChoice.ItemsSource = available;
        _youtubeQuizVideoChoice.SelectedIndex = available.Count > 0 ? 0 : -1;
        _youtubeQuizVideoChoice.Visibility = available.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_youtubeQuizVideoEmptyText is not null)
            _youtubeQuizVideoEmptyText.Visibility = available.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncSelectedPlaylistPrivacyChoice()
    {
        if (_youtubePlaylistPrivacyChoice is null ||
            _youtubePlaylistsGrid?.SelectedItem is not YouTubePlaylistItem playlist)
            return;

        _youtubePlaylistPrivacyChoice.SelectedItem =
            string.Equals(playlist.Privacy, "public", StringComparison.OrdinalIgnoreCase)
                ? "Public"
                : "Private";
    }

    private async Task UpdateSelectedPlaylistPrivacyAsync()
    {
        if (_youtubePlaylistsGrid?.SelectedItem is not YouTubePlaylistItem playlist)
        {
            MessageBox.Show(this, "Select a playlist first.", "YouTube Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var privacy = _youtubePlaylistPrivacyChoice?.SelectedItem?.ToString()?.ToLowerInvariant() ?? "private";
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.UpdatePlaylistPrivacyAsync(token, playlist, privacy);
            await RefreshYouTubePlaylistsAsync(true);
            SetYouTubePlaylistsStatus($"{playlist.Title} is now {privacy}.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Update Playlist Privacy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddSelectedQuizToPlaylistAsync()
    {
        if (_youtubePlaylistsGrid?.SelectedItem is not YouTubePlaylistItem playlist || _youtubeQuizVideoChoice?.SelectedItem is not YouTubeQuizChoice quiz)
        {
            MessageBox.Show(this, "Select a playlist and a published quiz video first.", "YouTube Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.AddVideoToPlaylistAsync(token, playlist.Id, quiz.VideoId);
            await RefreshSelectedPlaylistVideosAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Add to Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RemoveSelectedPlaylistVideoAsync()
    {
        if (_youtubePlaylistsGrid?.SelectedItem is not YouTubePlaylistItem playlist || _youtubePlaylistVideosGrid?.SelectedItem is not YouTubePlaylistVideo video)
        {
            MessageBox.Show(this, "Select a video in a playlist first.", "YouTube Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Remove “{video.Title}” from “{playlist.Title}”? The video itself will not be deleted.", "Remove from Playlist", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            await _youtubeManagement.RemovePlaylistVideoAsync(token, video.PlaylistItemId);
            await RefreshSelectedPlaylistVideosAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Remove from Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string Title, string Description, string Privacy)? ShowCreatePlaylistDialog()
    {
        var dialog = new Window
        {
            Title = "Create YouTube Playlist",
            Owner = this,
            Width = 480,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Title", FontWeight = FontWeights.SemiBold });
        var title = new TextBox { Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "Description", FontWeight = FontWeights.SemiBold });
        var description = new TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(description);
        panel.Children.Add(new TextBlock { Text = "Privacy", FontWeight = FontWeights.SemiBold });
        var privacy = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 16) };
        privacy.Items.Add("private");
        privacy.Items.Add("public");
        privacy.Items.Add("unlisted");
        privacy.SelectedIndex = 0;
        panel.Children.Add(privacy);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var create = new Button { Content = "Create", Width = 88, IsDefault = true };
        create.Click += (_, _) =>
        {
            if (title.Text.Trim().Length == 0)
            {
                MessageBox.Show(dialog, "Enter a playlist title.", "Create Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true
            ? (title.Text.Trim(), description.Text.Trim(), privacy.SelectedItem?.ToString() ?? "public")
            : null;
    }

    private void SetYouTubeCommentsStatus(string text)
    {
        if (_youtubeCommentsStatus is not null) _youtubeCommentsStatus.Text = text;
    }

    private void SetYouTubePlaylistsStatus(string text)
    {
        if (_youtubePlaylistsStatus is not null) _youtubePlaylistsStatus.Text = text;
    }

    private void AddYouTubeAnalyticsNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;
        var notesButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizNotesTabIndex.ToString(), StringComparison.Ordinal));
        if (notesButton?.Parent is not StackPanel navigation)
            return;

        var analyticsButton = new Button { Content = "▶   YouTube Manager", Tag = tabIndex.ToString() };
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

                var views = YouTubeAnalyticsMetrics.PreserveHighest(item.History.YouTubeViews, video.Views);
                var likes = YouTubeAnalyticsMetrics.PreserveHighest(item.History.YouTubeLikes, video.Likes);
                _data.UpdateQuizHistoryYouTubeMetrics(item.History.Id, views, likes, video.PublishedAt);
                var quizName = item.History.YouTubeTitle.Trim();
                if (quizName.Length == 0) quizName = video.Title.Length > 0 ? video.Title : item.History.Title;
                rows.Add(new YouTubeAnalyticsRow(
                    item.History.Id,
                    item.History.VideoType,
                    quizName,
                    video.PublishedAt?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "",
                    views,
                    likes,
                    video.Comments,
                    YouTubeAnalyticsMetrics.EngagementRate(views, likes, video.Comments),
                    item.History.YouTubeUrl));
            }

            _youtubeAnalyticsGrid.ItemsSource = rows.OrderByDescending(row => row.Views).ToList();
            var channelId = videos.Values.Select(video => video.ChannelId).FirstOrDefault(id => id.Length > 0) ?? "";
            var channel = await _youtubeVideoAnalytics.FetchChannelAsync(apiKey, channelId);
            _youtubeChannelNameText!.Text = channel is null || channel.Title.Length == 0
                ? "Channel and tracked quiz performance"
                : $"{channel.Title} • channel and tracked quiz performance";

            var totalViews = rows.Sum(row => row.Views);
            var totalLikes = rows.Sum(row => row.Likes);
            var totalComments = rows.Sum(row => row.Comments);
            _youtubeTrackedVideosText!.Text = rows.Count.ToString("N0");
            _youtubeTrackedViewsText!.Text = totalViews.ToString("N0");
            _youtubeTrackedLikesText!.Text = totalLikes.ToString("N0");
            _youtubeTrackedCommentsText!.Text = totalComments.ToString("N0");
            _youtubeTopCategoryText!.Text = QuizHistoryStatistics.Calculate(_data.GetQuizHistory()).TopCategory;
            RefreshYouTubeRecommendation();
            RefreshQuizHistory();
            SetYouTubeAnalyticsStatus("YouTube analytics updated.");
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

    private void RefreshYouTubeRecommendation()
    {
        if (_youtubeNextQuizText is null || _youtubeNextQuizReasonText is null)
            return;

        try
        {
            var categories = _data.GetQuizCategorySummaries()
                .Where(category => category.EnabledCount > 0)
                .Select(category => category.Category);
            var recommendation = YouTubeNextQuizPlanner.Recommend(_data.GetQuizHistory(), categories);
            _youtubeNextQuizText.Text = recommendation.Display;
            _youtubeNextQuizReasonText.Text = recommendation.Reason;
        }
        catch (Exception error)
        {
            Debug.WriteLine($"YouTube recommendation: {error.Message}");
            _youtubeNextQuizText.Text = "Recommendation unavailable";
            _youtubeNextQuizReasonText.Text = "Refresh after checking the question bank.";
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
