using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public sealed record InstagramAnalyticsRow(
    int HistoryId,
    string Quiz,
    string Status,
    string Uploaded,
    long Views,
    long Likes,
    long Comments,
    long Shares,
    double EngagementRate,
    string Url)
{
    public string EngagementDisplay => EngagementRate.ToString("0.00'%'", CultureInfo.InvariantCulture);
}

public sealed record InstagramNextShortRecommendation(string Category, string Reason)
{
    public string Display => $"{Category} Quiz — Short";
}

public static class InstagramNextShortPlanner
{
    public static InstagramNextShortRecommendation Recommend(
        IReadOnlyList<QuizHistorySummary> history,
        IEnumerable<string> availableCategories)
    {
        var categories = availableCategories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categories.Count == 0) categories.Add("General Knowledge");

        var published = history.Where(item => item.PublishedOnInstagram && item.VideoType == "Short").ToList();
        var choice = categories
            .Select((category, order) => new
            {
                Category = category,
                Order = order,
                Count = published.Count(item => string.Equals(
                    QuizYouTubeAnalytics.CategoryName(item), category, StringComparison.OrdinalIgnoreCase)),
            })
            .OrderBy(item => item.Count)
            .ThenBy(item => item.Order)
            .First();
        var reason = choice.Count == 0
            ? $"{choice.Category} does not yet have a tracked Instagram Short."
            : $"{choice.Category} has the fewest tracked Instagram Shorts ({choice.Count:N0}).";
        return new InstagramNextShortRecommendation(choice.Category, reason);
    }
}

public static class InstagramShortMatcher
{
    public static IReadOnlyDictionary<int, InstagramMediaItem> Match(
        IReadOnlyList<QuizHistorySummary> history,
        IReadOnlyList<InstagramMediaItem> media)
    {
        var shorts = history.Where(item => item.VideoType == "Short").ToList();
        var available = media
            .Where(item => string.Equals(item.MediaType, "REELS", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.MediaType, "VIDEO", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.MediaId, StringComparer.Ordinal);
        var matches = new Dictionary<int, InstagramMediaItem>();

        foreach (var item in shorts.Where(item => item.InstagramUrl.Length > 0))
        {
            var match = available.Values.FirstOrDefault(mediaItem =>
                SameInstagramUrl(item.InstagramUrl, mediaItem.Permalink));
            if (match is null) continue;
            matches[item.Id] = match;
            available.Remove(match.MediaId);
        }

        foreach (var item in shorts.Where(item => !matches.ContainsKey(item.Id)))
        {
            var ranked = available.Values
                .Select(mediaItem => new { Media = mediaItem, Score = Score(item, mediaItem) })
                .Where(candidate => candidate.Score >= 80)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Media.PublishedAt)
                .ToList();
            if (ranked.Count == 0 || (ranked.Count > 1 && ranked[0].Score == ranked[1].Score)) continue;
            matches[item.Id] = ranked[0].Media;
            available.Remove(ranked[0].Media.MediaId);
        }
        return matches;
    }

    private static int Score(QuizHistorySummary history, InstagramMediaItem media)
    {
        var source = Normalize(media.Caption);
        if (source.Length == 0) return 0;
        var best = 0;
        foreach (var candidateValue in new[]
                 {
                     history.YouTubeTitle,
                     history.Title,
                     $"{history.SeriesName} {history.EpisodeLabel}",
                 })
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
                var sourceWords = source.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.Ordinal);
                var matched = words.Count(sourceWords.Contains);
                if (matched >= 3 && matched * 100 / words.Count >= 60)
                    best = Math.Max(best, 80 + matched);
            }
        }

        var category = Normalize(QuizYouTubeAnalytics.CategoryName(history));
        if (history.EpisodeLabel.Length > 0 &&
            media.Caption.Contains(history.EpisodeLabel, StringComparison.OrdinalIgnoreCase) &&
            category.Length > 0 &&
            source.Contains(category, StringComparison.Ordinal))
            best = Math.Max(best, 90);
        return best;
    }

    private static bool SameInstagramUrl(string left, string right) =>
        left.Trim().TrimEnd('/').Equals(right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

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
    private bool _instagramManagerPageInitialized;
    private bool _instagramManagerPageRefreshing;
    private int _instagramManagerTabIndex = -1;
    private readonly InstagramManagementService _instagramManagement = new();
    private DataGrid? _instagramAnalyticsGrid;
    private TextBlock? _instagramAccountText;
    private TextBlock? _instagramTrackedShortsText;
    private TextBlock? _instagramViewsText;
    private TextBlock? _instagramLikesText;
    private TextBlock? _instagramCommentsText;
    private TextBlock? _instagramSharesText;
    private TextBlock? _instagramNextShortText;
    private TextBlock? _instagramNextShortReasonText;
    private TextBlock? _instagramManagerStatus;
    private IReadOnlyDictionary<int, InstagramMediaItem> _instagramMediaMatches =
        new Dictionary<int, InstagramMediaItem>();

    private void InitializeInstagramManagerPage()
    {
        if (_instagramManagerPageInitialized || MainTabs is null) return;
        _instagramManagerPageInitialized = true;
        var tab = new TabItem { Content = BuildInstagramManagerPage() };
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                await RefreshInstagramManagerAsync(false);
        };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle) tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _instagramManagerTabIndex = MainTabs.Items.Count - 1;
        AddInstagramNavigationButton(_instagramManagerTabIndex);
    }

    private FrameworkElement BuildInstagramManagerPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(new TextBlock
        {
            Text = "Instagram Manager",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(225, 48, 108), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.65,
            },
        });
        _instagramAccountText = new TextBlock
        {
            Text = "Track Reel performance for the Instagram account linked to Factburst Quiz.",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        };
        heading.Children.Add(_instagramAccountText);
        root.Children.Add(heading);

        var analytics = BuildInstagramAnalyticsSection();
        Grid.SetRow(analytics, 1);
        root.Children.Add(analytics);
        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(64, 34, 118), 0.55),
                    new(Color.FromRgb(193, 53, 132), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private FrameworkElement BuildInstagramAnalyticsSection()
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
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Track the Instagram Reels created from your quiz Shorts.",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        header.Children.Add(heading);
        var refresh = new Button
        {
            Content = "Refresh from Instagram",
            MinWidth = 174,
            MinHeight = 36,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(225, 48, 108));
        refresh.Click += async (_, _) => await RefreshInstagramManagerAsync(true);
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 5; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shorts = BuildQuizHistoryStatCard("Tracked Shorts", Color.FromRgb(225, 48, 108));
        _instagramTrackedShortsText = shorts.Value;
        shorts.Card.Margin = new Thickness(0, 0, 5, 0);
        stats.Children.Add(shorts.Card);
        var views = BuildQuizHistoryStatCard("Instagram views", Color.FromRgb(131, 58, 180));
        _instagramViewsText = views.Value;
        views.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(views.Card, 1);
        stats.Children.Add(views.Card);
        var likes = BuildQuizHistoryStatCard("Likes", Color.FromRgb(253, 29, 29));
        _instagramLikesText = likes.Value;
        likes.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(likes.Card, 2);
        stats.Children.Add(likes.Card);
        var comments = BuildQuizHistoryStatCard("Comments", Color.FromRgb(252, 175, 69));
        _instagramCommentsText = comments.Value;
        comments.Card.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(comments.Card, 3);
        stats.Children.Add(comments.Card);
        var shares = BuildQuizHistoryStatCard("Shares", Color.FromRgb(70, 235, 115));
        _instagramSharesText = shares.Value;
        shares.Card.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(shares.Card, 4);
        stats.Children.Add(shares.Card);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        var next = BuildQuizHistoryStatCard("Next Instagram Short to create", Color.FromRgb(255, 202, 45));
        _instagramNextShortText = next.Value;
        _instagramNextShortText.FontSize = 22;
        if (next.Card.Child is StackPanel nextContent)
        {
            _instagramNextShortReasonText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            nextContent.Children.Add(_instagramNextShortReasonText);
        }
        next.Card.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(next.Card, 2);
        root.Children.Add(next.Card);

        _instagramAnalyticsGrid = BuildInstagramGrid();
        var table = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 48, 108)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(225, 48, 108), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.4,
            },
            Child = _instagramAnalyticsGrid,
        };
        Grid.SetRow(table, 3);
        root.Children.Add(table);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _instagramManagerStatus = new TextBlock
        {
            Text = "Refresh to find Instagram Reels automatically.",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 205, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(_instagramManagerStatus);
        var edit = new Button { Content = "Edit selected Short", MinWidth = 138, MinHeight = 36 };
        StyleQuizHistoryButton(edit, Color.FromRgb(204, 70, 255));
        edit.Click += (_, _) => ShowSelectedInstagramShort();
        Grid.SetColumn(edit, 1);
        footer.Children.Add(edit);
        var open = new Button
        {
            Content = "Open selected Reel",
            MinWidth = 138,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(open, Color.FromRgb(255, 202, 45));
        open.Click += (_, _) => OpenSelectedInstagramPost();
        Grid.SetColumn(open, 2);
        footer.Children.Add(open);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        RefreshInstagramRows();
        return root;
    }

    private DataGrid BuildInstagramGrid()
    {
        var grid = BuildYouTubeAnalyticsGrid();
        grid.Columns.Clear();
        grid.Columns.Add(TextColumn("Status", nameof(InstagramAnalyticsRow.Status), 92));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz Short",
            Binding = new Binding(nameof(InstagramAnalyticsRow.Quiz)),
            SortMemberPath = nameof(InstagramAnalyticsRow.Quiz),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(TextColumn("Uploaded", nameof(InstagramAnalyticsRow.Uploaded), 104));
        grid.Columns.Add(NumberColumn("Views", nameof(InstagramAnalyticsRow.Views), 86));
        grid.Columns.Add(NumberColumn("Likes", nameof(InstagramAnalyticsRow.Likes), 90));
        grid.Columns.Add(NumberColumn("Comments", nameof(InstagramAnalyticsRow.Comments), 88));
        grid.Columns.Add(NumberColumn("Shares", nameof(InstagramAnalyticsRow.Shares), 78));
        grid.Columns.Add(TextColumn("Engagement", nameof(InstagramAnalyticsRow.EngagementDisplay), 104));
        grid.MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.ChangedButton == MouseButton.Left) ShowSelectedInstagramShort();
        };
        return grid;
    }

    private void RefreshInstagramRows(
        IReadOnlyDictionary<int, InstagramMediaItem>? matches = null)
    {
        if (matches is not null) _instagramMediaMatches = matches;
        var history = _data.GetQuizHistory().Where(item => item.VideoType == "Short").ToList();
        var rows = history.Select(item =>
        {
            _instagramMediaMatches.TryGetValue(item.Id, out var media);
            var views = media?.Views ?? 0;
            var likes = media?.Likes ?? 0;
            var comments = media?.Comments ?? 0;
            var shares = media?.Shares ?? 0;
            return new InstagramAnalyticsRow(
                item.Id,
                item.YouTubeTitle.Length > 0 ? item.YouTubeTitle : item.Title,
                item.PublishedOnInstagram ? "Published" : "Not linked",
                media?.PublishedAt?.ToLocalTime().ToString("dd-MM-yyyy") ?? item.InstagramUploadDateDisplay,
                views,
                likes,
                comments,
                shares,
                YouTubeAnalyticsMetrics.EngagementRate(views, likes, comments + shares),
                media?.Permalink ?? item.InstagramUrl);
        }).ToList();
        if (_instagramAnalyticsGrid is not null) _instagramAnalyticsGrid.ItemsSource = rows;

        var tracked = rows.Where(row => row.Status == "Published").ToList();
        if (_instagramTrackedShortsText is not null) _instagramTrackedShortsText.Text = tracked.Count.ToString("N0");
        if (_instagramViewsText is not null) _instagramViewsText.Text = tracked.Sum(item => item.Views).ToString("N0");
        if (_instagramLikesText is not null) _instagramLikesText.Text = tracked.Sum(item => item.Likes).ToString("N0");
        if (_instagramCommentsText is not null) _instagramCommentsText.Text = tracked.Sum(item => item.Comments).ToString("N0");
        if (_instagramSharesText is not null) _instagramSharesText.Text = tracked.Sum(item => item.Shares).ToString("N0");
        RefreshInstagramRecommendation(history);
    }

    private void RefreshInstagramRecommendation(IReadOnlyList<QuizHistorySummary> history)
    {
        if (_instagramNextShortText is null || _instagramNextShortReasonText is null) return;
        var categories = _data.GetQuizCategorySummaries()
            .Where(item => item.EnabledCount > 0)
            .Select(item => item.Category);
        var recommendation = InstagramNextShortPlanner.Recommend(history, categories);
        _instagramNextShortText.Text = recommendation.Display;
        _instagramNextShortReasonText.Text = recommendation.Reason;
    }

    private async Task RefreshInstagramManagerAsync(bool showErrors)
    {
        if (_instagramManagerPageRefreshing || _instagramAnalyticsGrid is null) return;
        var token = _data.LoadSettings().InstagramAccessToken.Trim();
        if (token.Length == 0)
        {
            RefreshInstagramRows();
            SetInstagramStatus("Add the Instagram access token in Settings → Instagram. Saved publication details are shown.");
            return;
        }

        try
        {
            _instagramManagerPageRefreshing = true;
            SetInstagramStatus("Finding Instagram Reels and updating analytics...");
            var history = _data.GetQuizHistory().Where(item => item.VideoType == "Short").ToList();
            var result = await _instagramManagement.ListMediaAsync(token);
            var matches = InstagramShortMatcher.Match(history, result.Media);
            foreach (var item in history)
            {
                if (!matches.TryGetValue(item.Id, out var media)) continue;
                _data.UpdateQuizHistoryInstagramPublication(
                    item.Id,
                    true,
                    media.Permalink,
                    media.PublishedAt);
            }
            RefreshInstagramRows(matches);
            if (_instagramAccountText is not null)
                _instagramAccountText.Text = $"@{result.Username} • {result.AccountType} • {result.MediaCount:N0} total posts";
            var unmatched = Math.Max(0, result.Media.Count - matches.Count);
            SetInstagramStatus(matches.Count == 0
                ? $"Found {result.Media.Count:N0} recent Instagram post(s), but none matched a quiz Short."
                : unmatched == 0
                    ? $"Found and updated {matches.Count:N0} Instagram Reel(s)."
                    : $"Updated {matches.Count:N0} matched Reel(s); {unmatched:N0} recent post(s) were not matched.");
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Instagram analytics: {error.Message}");
            RefreshInstagramRows();
            SetInstagramStatus("Instagram analytics update failed. Saved publication details are still shown.");
            if (showErrors)
                MessageBox.Show(this, error.Message, "Refresh Instagram", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _instagramManagerPageRefreshing = false;
        }
    }

    private void ShowSelectedInstagramShort()
    {
        if (_instagramAnalyticsGrid?.SelectedItem is not InstagramAnalyticsRow row) return;
        var history = _data.GetQuizHistory().FirstOrDefault(item => item.Id == row.HistoryId);
        if (history is null) return;

        var dialog = new Window
        {
            Title = $"Instagram Reel — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 680,
            Height = 350,
            MinWidth = 620,
            MinHeight = 320,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        var published = new CheckBox
        {
            Content = "Published on Instagram",
            IsChecked = history.PublishedOnInstagram,
            FontWeight = FontWeights.SemiBold,
        };
        panel.Children.Add(published);
        panel.Children.Add(new TextBlock
        {
            Text = "Instagram Reel link",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 4),
        });
        var url = new TextBox
        {
            Text = row.Url,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(url);
        panel.Children.Add(new TextBlock
        {
            Text = "Upload date",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 4),
        });
        var uploadDate = new DatePicker
        {
            SelectedDate = QuizYouTubeAnalytics.ParseUploadDate(history.InstagramUploadDate),
            SelectedDateFormat = DatePickerFormat.Short,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 150,
        };
        panel.Children.Add(uploadDate);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
        };
        var open = new Button { Content = "Open Reel", MinWidth = 92 };
        open.Click += (_, _) =>
        {
            try
            {
                var reelUrl = QuizInstagramPublication.NormalizeUrl(url.Text);
                if (reelUrl.Length == 0) throw new InvalidOperationException("Enter the Instagram Reel link first.");
                Process.Start(new ProcessStartInfo(reelUrl) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Open Instagram Reel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        actions.Children.Add(open);
        actions.Children.Add(new Button
        {
            Content = "Cancel",
            MinWidth = 82,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        });
        var save = new Button
        {
            Content = "Save",
            MinWidth = 82,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        save.Click += (_, _) =>
        {
            try
            {
                if (!_data.UpdateQuizHistoryInstagramPublication(
                        history.Id,
                        published.IsChecked == true,
                        url.Text,
                        uploadDate.SelectedDate))
                    throw new InvalidOperationException("The selected quiz-history entry no longer exists.");
                dialog.DialogResult = true;
                RefreshInstagramRows();
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Save Instagram Reel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        actions.Children.Add(save);
        panel.Children.Add(actions);
        dialog.ShowDialog();
    }

    private void OpenSelectedInstagramPost()
    {
        if (_instagramAnalyticsGrid?.SelectedItem is not InstagramAnalyticsRow row || row.Url.Length == 0) return;
        Process.Start(new ProcessStartInfo(row.Url) { UseShellExecute = true });
    }

    private void AddInstagramNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root) return;
        var facebook = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _facebookAnalyticsTabIndex.ToString(), StringComparison.Ordinal));
        if (facebook?.Parent is not StackPanel navigation) return;
        var button = new Button { Content = "◎  Instagram Manager", Tag = tabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle) button.Style = navStyle;
        button.Click += Navigate_Click;
        var index = navigation.Children.IndexOf(facebook);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, index + 1), button);
    }

    private void SetInstagramStatus(string text)
    {
        if (_instagramManagerStatus is not null) _instagramManagerStatus.Text = text;
    }
}

