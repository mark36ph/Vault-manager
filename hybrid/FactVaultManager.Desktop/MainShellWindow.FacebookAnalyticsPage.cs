using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public sealed record FacebookAnalyticsRow(
    int HistoryId,
    string Quiz,
    string Status,
    string Uploaded,
    long Views,
    long Reactions,
    long Comments,
    long Shares,
    double EngagementRate,
    string Url)
{
    public string EngagementDisplay => EngagementRate.ToString("0.00'%'", CultureInfo.InvariantCulture);
}

public sealed record FacebookNextShortRecommendation(string Category, string Reason)
{
    public string Display => $"{Category} Quiz — Short";
}

public static class FacebookNextShortPlanner
{
    public static FacebookNextShortRecommendation Recommend(
        IReadOnlyList<QuizHistorySummary> history,
        IEnumerable<string> availableCategories)
    {
        var categories = availableCategories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categories.Count == 0) categories.Add("General Knowledge");

        var published = history.Where(item => item.PublishedOnFacebook && item.VideoType == "Short").ToList();
        var choice = categories
            .Select((category, order) => new
            {
                Category = category,
                Order = order,
                Count = published.Count(item => string.Equals(
                    QuizYouTubeAnalytics.CategoryName(item), category, StringComparison.OrdinalIgnoreCase)),
                Views = published.Where(item => string.Equals(
                        QuizYouTubeAnalytics.CategoryName(item), category, StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.FacebookViews),
            })
            .OrderBy(item => item.Count)
            .ThenByDescending(item => item.Views)
            .ThenBy(item => item.Order)
            .First();
        var reason = choice.Count == 0
            ? $"{choice.Category} does not yet have a tracked Facebook Short."
            : $"{choice.Category} has the fewest tracked Facebook Shorts ({choice.Count:N0}).";
        return new FacebookNextShortRecommendation(choice.Category, reason);
    }
}

public static class FacebookShortMatcher
{
    public static IReadOnlyDictionary<int, FacebookPageVideo> Match(
        IReadOnlyList<QuizHistorySummary> history,
        IReadOnlyList<FacebookPageVideo> videos)
    {
        var shorts = history.Where(item => item.VideoType == "Short").ToList();
        var available = videos.ToDictionary(item => item.VideoId, StringComparer.Ordinal);
        var matches = new Dictionary<int, FacebookPageVideo>();

        foreach (var item in shorts.Where(item => item.FacebookUrl.Length > 0))
        {
            var videoId = FacebookReelAnalyticsService.TryGetReelId(item.FacebookUrl);
            if (videoId is not null && available.Remove(videoId, out var video))
                matches[item.Id] = video;
        }

        foreach (var item in shorts.Where(item => !matches.ContainsKey(item.Id)))
        {
            var ranked = available.Values
                .Select(video => new { Video = video, Score = Score(item, video) })
                .Where(candidate => candidate.Score >= 80)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Video.PublishedAt)
                .ToList();
            if (ranked.Count == 0 || (ranked.Count > 1 && ranked[0].Score == ranked[1].Score)) continue;
            matches[item.Id] = ranked[0].Video;
            available.Remove(ranked[0].Video.VideoId);
        }
        return matches;
    }

    private static int Score(QuizHistorySummary history, FacebookPageVideo video)
    {
        var source = Normalize($"{video.Title} {video.Description}");
        if (source.Length == 0) return 0;
        var candidates = new[]
        {
            history.YouTubeTitle,
            history.Title,
            $"{history.SeriesName} {history.EpisodeLabel}",
        };

        var best = 0;
        foreach (var candidateValue in candidates)
        {
            var candidate = Normalize(candidateValue);
            if (candidate.Length < 6) continue;
            if (source == candidate) best = Math.Max(best, 100);
            else if (source.Contains(candidate, StringComparison.Ordinal)) best = Math.Max(best, 95);
            else
            {
                var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => word.Length >= 3)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (words.Count < 3) continue;
                var sourceWords = source.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var matched = words.Count(sourceWords.Contains);
                if (matched >= 3 && matched * 100 / words.Count >= 75)
                    best = Math.Max(best, 80 + matched);
            }
        }
        return best;
    }

    private static string Normalize(string? value)
    {
        var characters = (value ?? "").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public partial class MainShellWindow
{
    private bool _facebookAnalyticsPageInitialized;
    private bool _facebookAnalyticsPageRefreshing;
    private int _facebookAnalyticsTabIndex = -1;
    private DataGrid? _facebookAnalyticsGrid;
    private TextBlock? _facebookTrackedShortsText;
    private TextBlock? _facebookTrackedViewsText;
    private TextBlock? _facebookTrackedReactionsText;
    private TextBlock? _facebookTrackedCommentsText;
    private TextBlock? _facebookTrackedSharesText;
    private TextBlock? _facebookNextShortText;
    private TextBlock? _facebookNextShortReasonText;
    private TextBlock? _facebookAnalyticsStatus;
    private readonly FacebookReelAnalyticsService _facebookAnalytics = new();
    private readonly FacebookCommentManagementService _facebookComments = new();
    private ContentControl? _facebookManagerContent;
    private readonly Dictionary<string, Button> _facebookManagerButtons = new(StringComparer.OrdinalIgnoreCase);
    private string _facebookManagerSection = "analytics";
    private DataGrid? _facebookCommentsGrid;
    private ComboBox? _facebookCommentFilter;
    private TextBox? _facebookReplyText;
    private TextBlock? _facebookNeedsReplyCountText;
    private TextBlock? _facebookCommentsStatus;
    private readonly HashSet<string> _facebookHandledCommentIds = new(StringComparer.Ordinal);
    private IReadOnlyList<FacebookCommentItem> _facebookLoadedComments = Array.Empty<FacebookCommentItem>();
    private string _facebookCommentsPageName = "Facebook Page";

    private void InitializeFacebookAnalyticsPage()
    {
        if (_facebookAnalyticsPageInitialized || MainTabs is null) return;
        _facebookAnalyticsPageInitialized = true;
        var tab = new TabItem { Content = BuildFacebookAnalyticsPage() };
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                await RefreshCurrentFacebookManagerSectionAsync(false);
        };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle) tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _facebookAnalyticsTabIndex = MainTabs.Items.Count - 1;
        AddFacebookAnalyticsNavigationButton(_facebookAnalyticsTabIndex);
    }

    private FrameworkElement BuildFacebookAnalyticsPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(new TextBlock
        {
            Text = "Facebook Manager",
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
            Text = "Track Reel performance and manage viewer comments from your Page.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        root.Children.Add(heading);

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        AddFacebookManagerButton(navigation, "analytics", "Analytics");
        AddFacebookManagerButton(navigation, "comments", "Comments");
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);

        _facebookManagerContent = new ContentControl();
        Grid.SetRow(_facebookManagerContent, 2);
        root.Children.Add(_facebookManagerContent);
        SelectFacebookManagerSection("analytics");
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

    private FrameworkElement BuildFacebookAnalyticsSection()
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
        heading.Children.Add(new TextBlock
        {
            Text = "Track the Facebook Reels created from your quiz Shorts.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
        });
        header.Children.Add(heading);
        var refresh = new Button { Content = "Refresh from Facebook", MinWidth = 164, MinHeight = 36, VerticalAlignment = VerticalAlignment.Bottom };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshFacebookAnalyticsPageAsync(true);
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 5; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shorts = BuildQuizHistoryStatCard("Tracked Shorts", Color.FromRgb(248, 90, 105));
        _facebookTrackedShortsText = shorts.Value;
        shorts.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(shorts.Card);
        var views = BuildQuizHistoryStatCard("Facebook views", Color.FromRgb(0, 204, 255));
        _facebookTrackedViewsText = views.Value;
        views.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(views.Card, 1);
        stats.Children.Add(views.Card);
        var reactions = BuildQuizHistoryStatCard("Likes", Color.FromRgb(248, 90, 105));
        _facebookTrackedReactionsText = reactions.Value;
        reactions.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(reactions.Card, 2);
        stats.Children.Add(reactions.Card);
        var comments = BuildQuizHistoryStatCard("Comments", Color.FromRgb(204, 70, 255));
        _facebookTrackedCommentsText = comments.Value;
        comments.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(comments.Card, 3);
        stats.Children.Add(comments.Card);
        var shares = BuildQuizHistoryStatCard("Shares", Color.FromRgb(70, 235, 115));
        _facebookTrackedSharesText = shares.Value;
        shares.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(shares.Card, 4);
        stats.Children.Add(shares.Card);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        var next = BuildQuizHistoryStatCard("Next Facebook Short to create", Color.FromRgb(255, 202, 45));
        _facebookNextShortText = next.Value;
        _facebookNextShortText.FontSize = 22;
        if (next.Card.Child is StackPanel nextContent)
        {
            _facebookNextShortReasonText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(184, 201, 235)),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            nextContent.Children.Add(_facebookNextShortReasonText);
        }
        next.Card.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(next.Card, 2);
        root.Children.Add(next.Card);

        _facebookAnalyticsGrid = BuildFacebookAnalyticsGrid();
        var table = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.4,
            },
            Child = _facebookAnalyticsGrid,
        };
        Grid.SetRow(table, 3);
        root.Children.Add(table);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _facebookAnalyticsStatus = new TextBlock { Text = "Refresh to find Facebook Reels automatically.", Foreground = QuizMutedBrush(), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_facebookAnalyticsStatus);
        var edit = new Button { Content = "Edit selected Short", MinWidth = 138, MinHeight = 36 };
        StyleQuizHistoryButton(edit, Color.FromRgb(204, 70, 255));
        edit.Click += (_, _) => ShowSelectedFacebookShort();
        Grid.SetColumn(edit, 1);
        footer.Children.Add(edit);
        var open = new Button { Content = "Open selected Reel", MinWidth = 138, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        StyleQuizHistoryButton(open, Color.FromRgb(255, 202, 45));
        open.Click += (_, _) => OpenSelectedFacebookReel();
        Grid.SetColumn(open, 2);
        footer.Children.Add(open);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        RefreshFacebookRows();
        return root;
    }

    private void AddFacebookManagerButton(Panel parent, string key, string text)
    {
        var button = new Button { Content = text, MinWidth = 112, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        if (FindResource("YouTubeManagerTabButtonStyle") is Style managerButtonStyle)
            button.Style = managerButtonStyle;
        button.Click += async (_, _) =>
        {
            SelectFacebookManagerSection(key);
            await RefreshCurrentFacebookManagerSectionAsync(false);
        };
        _facebookManagerButtons[key] = button;
        parent.Children.Add(button);
    }

    private void SelectFacebookManagerSection(string key)
    {
        if (_facebookManagerContent is null) return;
        _facebookManagerSection = key;
        _facebookManagerContent.Content = key == "comments"
            ? BuildFacebookCommentsSection()
            : BuildFacebookAnalyticsSection();
        foreach (var pair in _facebookManagerButtons)
            pair.Value.Tag = pair.Key == key ? "Selected" : null;
    }

    private Task RefreshCurrentFacebookManagerSectionAsync(bool showErrors) =>
        _facebookManagerSection == "comments"
            ? RefreshFacebookCommentsAsync(showErrors)
            : RefreshFacebookAnalyticsPageAsync(showErrors);

    private FrameworkElement BuildFacebookCommentsSection()
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
        title.Children.Add(new TextBlock
        {
            Text = "Comments",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        title.Children.Add(new TextBlock
        {
            Text = "Reply to viewers and moderate comments across your Facebook Reels.",
            Foreground = QuizMutedBrush(),
        });
        toolbar.Children.Add(title);

        _facebookNeedsReplyCountText = new TextBlock
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
                    _facebookNeedsReplyCountText,
                },
            },
        };
        Grid.SetColumn(needsReplyBadge, 1);
        toolbar.Children.Add(needsReplyBadge);

        _facebookCommentFilter = new ComboBox { Width = 132, Height = 34, Margin = new Thickness(8, 0, 8, 0) };
        _facebookCommentFilter.Items.Add("Needs reply");
        _facebookCommentFilter.Items.Add("Newest");
        _facebookCommentFilter.Items.Add("Hidden");
        _facebookCommentFilter.SelectedIndex = 0;
        _facebookCommentFilter.SelectionChanged += (_, _) => ApplyFacebookCommentFilter();
        Grid.SetColumn(_facebookCommentFilter, 2);
        toolbar.Children.Add(_facebookCommentFilter);

        var refresh = new Button { Content = "Refresh comments", MinWidth = 132, MinHeight = 34 };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshFacebookCommentsAsync(true);
        Grid.SetColumn(refresh, 3);
        toolbar.Children.Add(refresh);
        root.Children.Add(toolbar);

        _facebookCommentsGrid = BuildManagerGrid();
        _facebookCommentsGrid.RowHeight = double.NaN;
        _facebookCommentsGrid.MinRowHeight = 44;
        _facebookCommentsGrid.Columns.Add(FacebookCommentAuthorColumn());
        _facebookCommentsGrid.Columns.Add(FacebookCommentReelColumn());
        _facebookCommentsGrid.Columns.Add(WrappedTextColumn(
            "Comment",
            nameof(FacebookCommentItem.Message),
            new DataGridLength(1, DataGridLengthUnitType.Star)));
        _facebookCommentsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Published",
            Binding = new Binding(nameof(FacebookCommentItem.CreatedAt)) { StringFormat = "dd-MM-yyyy HH:mm" },
            Width = new DataGridLength(132),
        });
        _facebookCommentsGrid.Columns.Add(NumberColumn("Likes", nameof(FacebookCommentItem.LikeCount), 62));
        _facebookCommentsGrid.Columns.Add(NumberColumn("Replies", nameof(FacebookCommentItem.ReplyCount), 68));
        _facebookCommentsGrid.Columns.Add(TextColumn("Hidden", nameof(FacebookCommentItem.IsHidden), 66));
        var commentsCard = ManagerCard(_facebookCommentsGrid);
        Grid.SetRow(commentsCard, 1);
        root.Children.Add(commentsCard);

        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 5; index++)
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _facebookReplyText = new TextBox
        {
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(_facebookReplyText);
        AddCommentAction(actions, 1, "Open Reel", Color.FromRgb(255, 202, 45), () =>
        {
            OpenSelectedFacebookCommentReel();
            return Task.CompletedTask;
        });
        AddCommentAction(actions, 2, "Like / unlike", Color.FromRgb(204, 70, 255), ToggleSelectedFacebookCommentLikeAsync);
        AddCommentAction(actions, 3, "Reply", Color.FromRgb(0, 204, 255), ReplyToSelectedFacebookCommentAsync);
        AddCommentAction(actions, 4, "Hide / unhide", Color.FromRgb(70, 235, 115), ToggleSelectedFacebookCommentHiddenAsync);
        AddCommentAction(actions, 5, "Delete", Color.FromRgb(248, 90, 105), DeleteSelectedFacebookCommentAsync);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        _facebookCommentsStatus = new TextBlock
        {
            Text = "Add the Facebook Page access token in Settings to manage comments.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(_facebookCommentsStatus, 3);
        root.Children.Add(_facebookCommentsStatus);
        return root;
    }

    private DataGridTemplateColumn FacebookCommentReelColumn()
    {
        var link = new FrameworkElementFactory(typeof(TextBlock));
        link.SetBinding(TextBlock.TextProperty, new Binding(nameof(FacebookCommentItem.ReelTitle)));
        link.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        link.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        link.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255)));
        link.SetValue(TextBlock.CursorProperty, Cursors.Hand);
        link.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 4));
        link.SetValue(TextBlock.ToolTipProperty, "Open this Reel on Facebook");
        link.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(FacebookCommentReel_MouseLeftButtonUp));
        return new DataGridTemplateColumn
        {
            Header = "Reel",
            CellTemplate = new DataTemplate { VisualTree = link },
            SortMemberPath = nameof(FacebookCommentItem.ReelTitle),
            Width = new DataGridLength(250),
        };
    }

    private DataGridTemplateColumn FacebookCommentAuthorColumn()
    {
        var link = new FrameworkElementFactory(typeof(TextBlock));
        link.SetBinding(TextBlock.TextProperty, new Binding(nameof(FacebookCommentItem.Author)));
        link.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        link.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
        link.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255)));
        link.SetValue(TextBlock.CursorProperty, Cursors.Hand);
        link.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 4));
        link.SetValue(TextBlock.ToolTipProperty, "Open this author's Facebook profile when available");
        link.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(FacebookCommentAuthor_MouseLeftButtonUp));
        return new DataGridTemplateColumn
        {
            Header = "Author",
            CellTemplate = new DataTemplate { VisualTree = link },
            SortMemberPath = nameof(FacebookCommentItem.Author),
            Width = new DataGridLength(132),
        };
    }

    private DataGrid BuildFacebookAnalyticsGrid()
    {
        var grid = BuildYouTubeAnalyticsGrid();
        grid.Columns.Clear();
        grid.Columns.Add(TextColumn("Status", nameof(FacebookAnalyticsRow.Status), 92));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz Short",
            Binding = new Binding(nameof(FacebookAnalyticsRow.Quiz)),
            SortMemberPath = nameof(FacebookAnalyticsRow.Quiz),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(TextColumn("Uploaded", nameof(FacebookAnalyticsRow.Uploaded), 104));
        grid.Columns.Add(NumberColumn("Views", nameof(FacebookAnalyticsRow.Views), 86));
        grid.Columns.Add(NumberColumn("Likes", nameof(FacebookAnalyticsRow.Reactions), 90));
        grid.Columns.Add(NumberColumn("Comments", nameof(FacebookAnalyticsRow.Comments), 88));
        grid.Columns.Add(NumberColumn("Shares", nameof(FacebookAnalyticsRow.Shares), 78));
        grid.Columns.Add(TextColumn("Engagement", nameof(FacebookAnalyticsRow.EngagementDisplay), 104));
        grid.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.ChangedButton == MouseButton.Left) ShowSelectedFacebookShort();
        };
        return grid;
    }

    private async Task RefreshFacebookCommentsAsync(bool showErrors)
    {
        if (_facebookCommentsGrid is null) return;
        var token = _data.LoadSettings().FacebookPageAccessToken.Trim();
        if (token.Length == 0)
        {
            _facebookLoadedComments = Array.Empty<FacebookCommentItem>();
            ApplyFacebookCommentFilter();
            SetFacebookCommentsStatus("Add the Facebook Page access token in Settings → Facebook to manage comments.");
            return;
        }

        try
        {
            SetFacebookCommentsStatus("Loading comments from Facebook Reels...");
            var page = await _facebookAnalytics.ListPageVideosAsync(token);
            _facebookLoadedComments = await _facebookComments.ListCommentsAsync(token, page);
            _facebookCommentsPageName = page.PageName.Length > 0 ? page.PageName : "Facebook Page";
            ApplyFacebookCommentFilter();
        }
        catch (Exception error)
        {
            SetFacebookCommentsStatus(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Facebook Comments", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFacebookCommentFilter()
    {
        var selection = _facebookCommentFilter?.SelectedItem?.ToString() ?? "Needs reply";
        var needsReply = FacebookCommentInbox.Filter(
            _facebookLoadedComments,
            "Needs reply",
            _facebookHandledCommentIds);
        if (_facebookNeedsReplyCountText is not null)
            _facebookNeedsReplyCountText.Text = needsReply.Count.ToString("N0");
        var rows = FacebookCommentInbox.Filter(
            _facebookLoadedComments,
            selection,
            _facebookHandledCommentIds);
        if (_facebookCommentsGrid is not null) _facebookCommentsGrid.ItemsSource = rows;
        SetFacebookCommentsStatus(selection == "Needs reply"
            ? $"{_facebookCommentsPageName} • {rows.Count:N0} viewer comments need a reply"
            : $"{_facebookCommentsPageName} • {rows.Count:N0} {selection.ToLowerInvariant()} comments");
    }

    private FacebookCommentItem? SelectedFacebookComment(bool showMessage = true)
    {
        if (_facebookCommentsGrid?.SelectedItem is FacebookCommentItem comment) return comment;
        if (showMessage)
            MessageBox.Show(this, "Select a comment first.", "Facebook Comments", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void FacebookCommentReel_MouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: FacebookCommentItem comment }) return;
        OpenFacebookUrl(comment.ReelUrl);
        eventArgs.Handled = true;
    }

    private void FacebookCommentAuthor_MouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: FacebookCommentItem comment } ||
            comment.AuthorProfileUrl.Length == 0) return;
        OpenFacebookUrl(comment.AuthorProfileUrl);
        eventArgs.Handled = true;
    }

    private void OpenSelectedFacebookCommentReel()
    {
        var comment = SelectedFacebookComment();
        if (comment is not null) OpenFacebookUrl(comment.ReelUrl);
    }

    private static void OpenFacebookUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        var host = uri.Host.TrimEnd('.');
        if (!(string.Equals(host, "facebook.com", StringComparison.OrdinalIgnoreCase) ||
              host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase))) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async Task ReplyToSelectedFacebookCommentAsync()
    {
        var comment = SelectedFacebookComment();
        if (comment is null) return;
        try
        {
            var reply = _facebookReplyText?.Text.Trim() ?? "";
            await _facebookComments.ReplyAsync(FacebookPageToken(), comment.Id, reply);
            _facebookHandledCommentIds.Add(comment.Id);
            if (_facebookReplyText is not null) _facebookReplyText.Clear();
            await RefreshFacebookCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Reply to Facebook Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ToggleSelectedFacebookCommentLikeAsync()
    {
        var comment = SelectedFacebookComment();
        if (comment is null) return;
        try
        {
            await _facebookComments.SetLikedAsync(FacebookPageToken(), comment.Id, !comment.IsLiked);
            await RefreshFacebookCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Like Facebook Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ToggleSelectedFacebookCommentHiddenAsync()
    {
        var comment = SelectedFacebookComment();
        if (comment is null) return;
        var action = comment.IsHidden ? "unhide" : "hide";
        if (MessageBox.Show(this, $"Do you want to {action} this comment from {comment.Author}?",
                "Confirm Comment Action", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await _facebookComments.SetHiddenAsync(FacebookPageToken(), comment.Id, !comment.IsHidden);
            await RefreshFacebookCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Hide Facebook Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DeleteSelectedFacebookCommentAsync()
    {
        var comment = SelectedFacebookComment();
        if (comment is null) return;
        if (MessageBox.Show(this,
                $"Permanently delete this comment from {comment.Author}? This cannot be undone.",
                "Delete Facebook Comment", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await _facebookComments.DeleteAsync(FacebookPageToken(), comment.Id);
            _facebookHandledCommentIds.Remove(comment.Id);
            await RefreshFacebookCommentsAsync(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Facebook Comment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string FacebookPageToken()
    {
        var token = _data.LoadSettings().FacebookPageAccessToken.Trim();
        if (token.Length == 0)
            throw new InvalidOperationException("Add the Facebook Page access token in Settings first.");
        return token;
    }

    private void SetFacebookCommentsStatus(string text)
    {
        if (_facebookCommentsStatus is not null) _facebookCommentsStatus.Text = text;
    }

    private void RefreshFacebookRows()
    {
        var history = _data.GetQuizHistory().Where(item => item.VideoType == "Short").ToList();
        var rows = history.Select(item => new FacebookAnalyticsRow(
            item.Id,
            item.YouTubeTitle.Length > 0 ? item.YouTubeTitle : item.Title,
            item.PublishedOnFacebook ? "Published" : "Not linked",
            item.FacebookUploadDateDisplay,
            item.FacebookViews,
            item.FacebookReactions,
            item.FacebookComments,
            item.FacebookShares,
            YouTubeAnalyticsMetrics.EngagementRate(item.FacebookViews,
                item.FacebookReactions, item.FacebookComments + item.FacebookShares),
            item.FacebookUrl)).ToList();
        if (_facebookAnalyticsGrid is not null) _facebookAnalyticsGrid.ItemsSource = rows;
        var tracked = history.Where(item => item.PublishedOnFacebook).ToList();
        if (_facebookTrackedShortsText is not null) _facebookTrackedShortsText.Text = tracked.Count.ToString("N0");
        if (_facebookTrackedViewsText is not null) _facebookTrackedViewsText.Text = tracked.Sum(item => item.FacebookViews).ToString("N0");
        if (_facebookTrackedReactionsText is not null) _facebookTrackedReactionsText.Text = tracked.Sum(item => item.FacebookReactions).ToString("N0");
        if (_facebookTrackedCommentsText is not null) _facebookTrackedCommentsText.Text = tracked.Sum(item => item.FacebookComments).ToString("N0");
        if (_facebookTrackedSharesText is not null) _facebookTrackedSharesText.Text = tracked.Sum(item => item.FacebookShares).ToString("N0");
        RefreshFacebookRecommendation(history);
    }

    private void RefreshFacebookRecommendation(IReadOnlyList<QuizHistorySummary> history)
    {
        if (_facebookNextShortText is null || _facebookNextShortReasonText is null) return;
        var categories = _data.GetQuizCategorySummaries().Where(item => item.EnabledCount > 0).Select(item => item.Category);
        var recommendation = FacebookNextShortPlanner.Recommend(history, categories);
        _facebookNextShortText.Text = recommendation.Display;
        _facebookNextShortReasonText.Text = recommendation.Reason;
    }

    private async Task RefreshFacebookAnalyticsPageAsync(bool showErrors)
    {
        if (_facebookAnalyticsPageRefreshing) return;
        var token = _data.LoadSettings().FacebookPageAccessToken.Trim();
        if (token.Length == 0)
        {
            RefreshFacebookRows();
            SetFacebookStatus("Add the Facebook Page access token in Settings → Facebook. Saved figures are shown.");
            return;
        }

        try
        {
            _facebookAnalyticsPageRefreshing = true;
            SetFacebookStatus("Finding Facebook Reels and updating analytics...");
            var history = _data.GetQuizHistory().Where(item => item.VideoType == "Short").ToList();
            var page = await _facebookAnalytics.ListPageVideosAsync(token);
            var matches = FacebookShortMatcher.Match(history, page.Videos);
            var updated = 0;
            foreach (var item in history)
            {
                if (!matches.TryGetValue(item.Id, out var discovered)) continue;
                var reel = await _facebookAnalytics.FetchAsync(token, discovered.VideoId);
                _data.UpdateQuizHistoryFacebookAnalytics(
                    item.Id,
                    true,
                    FacebookReelAnalyticsService.ResolveReelUrl(
                        discovered.VideoId, reel.PermalinkUrl, discovered.PermalinkUrl),
                    YouTubeAnalyticsMetrics.PreserveHighest(item.FacebookViews, reel.Views),
                    YouTubeAnalyticsMetrics.PreserveHighest(item.FacebookReactions, reel.Reactions),
                    YouTubeAnalyticsMetrics.PreserveHighest(item.FacebookComments, reel.Comments),
                    YouTubeAnalyticsMetrics.PreserveHighest(item.FacebookShares, reel.Shares),
                    reel.PublishedAt);
                updated++;
            }
            RefreshFacebookRows();
            var unmatched = Math.Max(0, page.Videos.Count - matches.Count);
            var pageLabel = page.PageName.Length > 0 ? page.PageName : "Facebook Page";
            SetFacebookStatus(matches.Count == 0
                ? $"Found {page.Videos.Count:N0} video(s) on {pageLabel}, but none matched a quiz Short by title."
                : unmatched == 0
                    ? $"Found and updated {updated:N0} Facebook Reels from {pageLabel}."
                    : $"Updated {updated:N0} matched Reels from {pageLabel}; {unmatched:N0} Page video(s) were not matched.");
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Facebook analytics: {error.Message}");
            SetFacebookStatus("Facebook analytics update failed. Saved figures are still shown.");
            if (showErrors) MessageBox.Show(this, error.Message, "Facebook Analytics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _facebookAnalyticsPageRefreshing = false;
        }
    }

    private void ShowSelectedFacebookShort()
    {
        if (_facebookAnalyticsGrid?.SelectedItem is not FacebookAnalyticsRow row) return;
        var history = _data.GetQuizHistory().FirstOrDefault(item => item.Id == row.HistoryId);
        if (history is null) return;
        ShowFacebookShortDialog(history);
    }

    private void ShowFacebookShortDialog(QuizHistorySummary history)
    {
        var dialog = new Window
        {
            Title = $"Facebook Reel — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this, Width = 680, Height = 520, MinWidth = 620, MinHeight = 470,
            ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title,
            FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        var published = new CheckBox { Content = "Published on Facebook", IsChecked = history.PublishedOnFacebook, FontWeight = FontWeights.SemiBold };
        panel.Children.Add(published);
        panel.Children.Add(new TextBlock { Text = "Facebook Reel link", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        var url = new TextBox { Text = history.FacebookUrl, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(url);

        var metrics = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        for (var index = 0; index < 5; index++)
        {
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (index < 4) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        }
        TextBox Metric(string label, long value, int column)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
            stack.Children.Add(box);
            Grid.SetColumn(stack, column);
            metrics.Children.Add(stack);
            return box;
        }
        var views = Metric("Views", history.FacebookViews, 0);
        var reactions = Metric("Likes", history.FacebookReactions, 2);
        var comments = Metric("Comments", history.FacebookComments, 4);
        var shares = Metric("Shares", history.FacebookShares, 6);
        var dateStack = new StackPanel();
        dateStack.Children.Add(new TextBlock { Text = "Upload date", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var uploadDate = new DatePicker { SelectedDate = QuizFacebookAnalytics.ParseUploadDate(history.FacebookUploadDate), SelectedDateFormat = DatePickerFormat.Short, MinHeight = 34 };
        dateStack.Children.Add(uploadDate);
        Grid.SetColumn(dateStack, 8);
        metrics.Children.Add(dateStack);
        panel.Children.Add(metrics);
        panel.Children.Add(new TextBlock
        {
            Text = "Figures update from Facebook when a Page access token is saved. You can also correct them manually.",
            Foreground = QuizMutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var open = new Button { Content = "Open Reel", MinWidth = 92 };
        open.Click += (_, _) =>
        {
            try
            {
                var reelUrl = QuizFacebookPublication.NormalizeUrl(url.Text);
                if (reelUrl.Length == 0) throw new InvalidOperationException("Enter the Facebook Reel link first.");
                Process.Start(new ProcessStartInfo(reelUrl) { UseShellExecute = true });
            }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, "Open Facebook Reel", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        actions.Children.Add(open);
        actions.Children.Add(new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        var save = new Button { Content = "Save", MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                if (!_data.UpdateQuizHistoryFacebookAnalytics(
                        history.Id, published.IsChecked == true, url.Text,
                        QuizFacebookAnalytics.ParseMetric(views.Text, "Views"),
                        QuizFacebookAnalytics.ParseMetric(reactions.Text, "Reactions"),
                        QuizFacebookAnalytics.ParseMetric(comments.Text, "Comments"),
                        QuizFacebookAnalytics.ParseMetric(shares.Text, "Shares"),
                        uploadDate.SelectedDate))
                    throw new InvalidOperationException("The selected quiz-history entry no longer exists.");
                dialog.DialogResult = true;
                RefreshFacebookRows();
            }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, "Save Facebook Reel", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        actions.Children.Add(save);
        panel.Children.Add(actions);
        dialog.ShowDialog();
    }

    private void OpenSelectedFacebookReel()
    {
        if (_facebookAnalyticsGrid?.SelectedItem is not FacebookAnalyticsRow row || row.Url.Length == 0) return;
        Process.Start(new ProcessStartInfo(row.Url) { UseShellExecute = true });
    }

    private void AddFacebookAnalyticsNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root) return;
        var youtube = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _youtubeAnalyticsTabIndex.ToString(), StringComparison.Ordinal));
        if (youtube?.Parent is not StackPanel navigation) return;
        var button = new Button { Content = "f   Facebook Manager", Tag = tabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle) button.Style = navStyle;
        button.Click += Navigate_Click;
        var index = navigation.Children.IndexOf(youtube);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, index + 1), button);
    }

    private void SetFacebookStatus(string text)
    {
        if (_facebookAnalyticsStatus is not null) _facebookAnalyticsStatus.Text = text;
    }
}
