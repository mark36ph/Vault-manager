using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record WebsiteAnalyticsFunnelRow(string Stage, long Count, string Conversion);

public sealed record WebsiteAnalyticsQuizRow(
    string Quiz,
    long Opens,
    long Starts,
    long Completed,
    long Shared,
    long YouTube,
    string Completion);

public sealed record WebsiteAnalyticsSourceRow(
    string Source,
    long Opens,
    long Starts,
    long Completed,
    long Shared,
    long YouTube);

public partial class MainShellWindow
{
    private bool _websiteAnalyticsInitialized;
    private bool _websiteAnalyticsRefreshing;
    private int _websiteAnalyticsTabIndex = -1;
    private DispatcherTimer? _websiteAnalyticsGuardTimer;
    private ComboBox? _websiteAnalyticsPeriod;
    private TextBlock? _websiteAnalyticsViewsText;
    private TextBlock? _websiteAnalyticsOpensText;
    private TextBlock? _websiteAnalyticsCompletionText;
    private TextBlock? _websiteAnalyticsYouTubeText;
    private DataGrid? _websiteAnalyticsFunnelGrid;
    private DataGrid? _websiteAnalyticsQuizGrid;
    private DataGrid? _websiteAnalyticsSourceGrid;
    private TextBlock? _websiteAnalyticsStatusText;

    public void InitializeWebsiteAnalyticsPage()
    {
        if (_websiteAnalyticsInitialized) return;
        _websiteAnalyticsInitialized = true;

        _websiteAnalyticsGuardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(550),
        };
        _websiteAnalyticsGuardTimer.Tick += (_, _) => EnsureWebsiteAnalyticsPage();
        _websiteAnalyticsGuardTimer.Start();
        Closed += (_, _) => _websiteAnalyticsGuardTimer?.Stop();

        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            if (MainTabs.SelectedIndex == _websiteAnalyticsTabIndex)
                await RefreshWebsiteAnalyticsAsync(false);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteAnalyticsPage));
    }

    private void EnsureWebsiteAnalyticsPage()
    {
        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null) return;

        if (_websiteAnalyticsTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildWebsiteAnalyticsPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _websiteAnalyticsTabIndex = MainTabs.Items.Count - 1;
        }

        if (_autopilotNavButtons.ContainsKey("Web Analytics"))
        {
            _websiteAnalyticsGuardTimer?.Stop();
            return;
        }

        var button = new Button
        {
            Content = "▥   Analytics",
            Tag = AutopilotFirstNavTag + ":Web Analytics",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        button.Click += (_, _) => NavigateWebsiteAnalytics();

        var website = _autopilotNavButtons.TryGetValue("Website", out var websiteButton) ? websiteButton : null;
        var users = _autopilotNavButtons.TryGetValue("Users", out var usersButton) ? usersButton : null;
        var advanced = _autopilotNavButtons.TryGetValue("Advanced", out var advancedButton) ? advancedButton : null;
        var index = website is not null
            ? _autopilotNavContainer.Children.IndexOf(website) + 1
            : users is not null
                ? _autopilotNavContainer.Children.IndexOf(users)
                : advanced is not null
                    ? _autopilotNavContainer.Children.IndexOf(advanced)
                    : _autopilotNavContainer.Children.Count;
        _autopilotNavContainer.Children.Insert(Math.Clamp(index, 0, _autopilotNavContainer.Children.Count), button);
        _autopilotNavButtons["Web Analytics"] = button;
        _websiteAnalyticsGuardTimer?.Stop();
    }

    private void NavigateWebsiteAnalytics()
    {
        EnsureWebsiteAnalyticsPage();
        if (_websiteAnalyticsTabIndex < 0) return;
        MainTabs.SelectedIndex = _websiteAnalyticsTabIndex;
        SelectAutopilotNav("Web Analytics");
        _ = RefreshWebsiteAnalyticsAsync(false);
    }

    private FrameworkElement BuildWebsiteAnalyticsPage()
    {
        var root = new Grid { Margin = new Thickness(26, 22, 26, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.75, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Website analytics",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Conversion performance for factburstquiz.com. Analytics remain in this admin app rather than on the public website.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 20, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _websiteAnalyticsPeriod = new ComboBox
        {
            Width = 116,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _websiteAnalyticsPeriod.Items.Add(new ComboBoxItem { Content = "7 days", Tag = 7 });
        _websiteAnalyticsPeriod.Items.Add(new ComboBoxItem { Content = "30 days", Tag = 30 });
        _websiteAnalyticsPeriod.Items.Add(new ComboBoxItem { Content = "90 days", Tag = 90 });
        _websiteAnalyticsPeriod.SelectedIndex = 1;
        _websiteAnalyticsPeriod.SelectionChanged += async (_, _) =>
        {
            if (_websiteAnalyticsTabIndex >= 0 && MainTabs.SelectedIndex == _websiteAnalyticsTabIndex)
                await RefreshWebsiteAnalyticsAsync(false);
        };
        var refresh = new Button { Content = "Refresh", MinWidth = 92, MinHeight = 36 };
        refresh.Click += async (_, _) => await RefreshWebsiteAnalyticsAsync(true);
        actions.Children.Add(_websiteAnalyticsPeriod);
        actions.Children.Add(refresh);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddWebsiteAnalyticsStat(stats, 0, "Site views", out _websiteAnalyticsViewsText);
        AddWebsiteAnalyticsStat(stats, 1, "Quiz opens", out _websiteAnalyticsOpensText);
        AddWebsiteAnalyticsStat(stats, 2, "Completion rate", out _websiteAnalyticsCompletionText);
        AddWebsiteAnalyticsStat(stats, 3, "YouTube CTR", out _websiteAnalyticsYouTubeText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _websiteAnalyticsFunnelGrid = BuildWebsiteAnalyticsGrid();
        _websiteAnalyticsFunnelGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Funnel stage",
            Binding = new Binding(nameof(WebsiteAnalyticsFunnelRow.Stage)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteAnalyticsFunnelGrid.Columns.Add(AnalyticsTextColumn("Count", nameof(WebsiteAnalyticsFunnelRow.Count), 110));
        _websiteAnalyticsFunnelGrid.Columns.Add(AnalyticsTextColumn("Conversion", nameof(WebsiteAnalyticsFunnelRow.Conversion), 130));
        var funnelCard = BuildWebsiteAnalyticsSectionCard("Conversion funnel", "How visitors move from quiz discovery to completion, sharing and YouTube.", _websiteAnalyticsFunnelGrid);
        Grid.SetRow(funnelCard, 2);
        root.Children.Add(funnelCard);

        var lower = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

        _websiteAnalyticsQuizGrid = BuildWebsiteAnalyticsGrid();
        _websiteAnalyticsQuizGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(WebsiteAnalyticsQuizRow.Quiz)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("Opens", nameof(WebsiteAnalyticsQuizRow.Opens), 66));
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("Starts", nameof(WebsiteAnalyticsQuizRow.Starts), 66));
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("Done", nameof(WebsiteAnalyticsQuizRow.Completed), 62));
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("Shared", nameof(WebsiteAnalyticsQuizRow.Shared), 68));
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("YouTube", nameof(WebsiteAnalyticsQuizRow.YouTube), 70));
        _websiteAnalyticsQuizGrid.Columns.Add(AnalyticsTextColumn("Completion", nameof(WebsiteAnalyticsQuizRow.Completion), 88));
        var quizCard = BuildWebsiteAnalyticsSectionCard("Top quizzes", "Performance by quiz for the selected period.", _websiteAnalyticsQuizGrid);
        quizCard.Margin = new Thickness(0, 0, 7, 0);
        lower.Children.Add(quizCard);

        _websiteAnalyticsSourceGrid = BuildWebsiteAnalyticsGrid();
        _websiteAnalyticsSourceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Source",
            Binding = new Binding(nameof(WebsiteAnalyticsSourceRow.Source)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteAnalyticsSourceGrid.Columns.Add(AnalyticsTextColumn("Opens", nameof(WebsiteAnalyticsSourceRow.Opens), 62));
        _websiteAnalyticsSourceGrid.Columns.Add(AnalyticsTextColumn("Done", nameof(WebsiteAnalyticsSourceRow.Completed), 58));
        _websiteAnalyticsSourceGrid.Columns.Add(AnalyticsTextColumn("Share", nameof(WebsiteAnalyticsSourceRow.Shared), 58));
        _websiteAnalyticsSourceGrid.Columns.Add(AnalyticsTextColumn("YT", nameof(WebsiteAnalyticsSourceRow.YouTube), 48));
        var sourceCard = BuildWebsiteAnalyticsSectionCard("Traffic sources", "Where quiz engagement originated.", _websiteAnalyticsSourceGrid);
        sourceCard.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(sourceCard, 1);
        lower.Children.Add(sourceCard);

        Grid.SetRow(lower, 3);
        root.Children.Add(lower);

        _websiteAnalyticsStatusText = new TextBlock
        {
            Text = "Website analytics will appear here once visitor activity has been collected.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_websiteAnalyticsStatusText, 4);
        root.Children.Add(_websiteAnalyticsStatusText);

        return root;
    }

    private async Task RefreshWebsiteAnalyticsAsync(bool showErrors)
    {
        if (_websiteAnalyticsRefreshing || _websiteAnalyticsFunnelGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            SetWebsiteAnalyticsUnavailable("Link Tracker is not configured. Open Website settings to add the Cloudflare tracker key.");
            return;
        }

        _websiteAnalyticsRefreshing = true;
        if (_websiteAnalyticsStatusText is not null)
            _websiteAnalyticsStatusText.Text = "Loading website analytics…";
        try
        {
            var days = SelectedWebsiteAnalyticsDays();
            using var client = new FactburstWebsiteAnalyticsAdminClient();
            var summary = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey, days);
            ApplyWebsiteAnalytics(summary);
        }
        catch (Exception ex)
        {
            SetWebsiteAnalyticsUnavailable(ex.Message);
            if (showErrors)
                MessageBox.Show(ex.Message, "Website analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _websiteAnalyticsRefreshing = false;
        }
    }

    private void ApplyWebsiteAnalytics(FactburstWebsiteAnalyticsSummary summary)
    {
        var events = summary.Events;
        var homeViews = EventCount(events, "home_view");
        var directoryViews = EventCount(events, "quiz_directory_view");
        var clicks = EventCount(events, "quiz_link_clicked");
        var opens = EventCount(events, "quiz_opened");
        var starts = EventCount(events, "quiz_started");
        var completed = EventCount(events, "quiz_completed");
        var shared = EventCount(events, "score_shared");
        var youtube = EventCount(events, "youtube_clicked");

        if (_websiteAnalyticsViewsText is not null) _websiteAnalyticsViewsText.Text = (homeViews + directoryViews).ToString("N0");
        if (_websiteAnalyticsOpensText is not null) _websiteAnalyticsOpensText.Text = opens.ToString("N0");
        if (_websiteAnalyticsCompletionText is not null) _websiteAnalyticsCompletionText.Text = Percentage(completed, starts);
        if (_websiteAnalyticsYouTubeText is not null) _websiteAnalyticsYouTubeText.Text = Percentage(youtube, completed);

        if (_websiteAnalyticsFunnelGrid is not null)
        {
            _websiteAnalyticsFunnelGrid.ItemsSource = new[]
            {
                new WebsiteAnalyticsFunnelRow("Quiz links clicked", clicks, "—"),
                new WebsiteAnalyticsFunnelRow("Quiz opened", opens, Percentage(opens, clicks)),
                new WebsiteAnalyticsFunnelRow("Quiz started", starts, Percentage(starts, opens)),
                new WebsiteAnalyticsFunnelRow("Quiz completed", completed, Percentage(completed, starts)),
                new WebsiteAnalyticsFunnelRow("Score shared", shared, Percentage(shared, completed)),
                new WebsiteAnalyticsFunnelRow("YouTube clicked", youtube, Percentage(youtube, completed)),
            };
        }

        if (_websiteAnalyticsQuizGrid is not null)
        {
            _websiteAnalyticsQuizGrid.ItemsSource = summary.Quizzes
                .Select(pair =>
                {
                    var values = pair.Value;
                    var quizOpens = EventCount(values, "quiz_opened");
                    var quizStarts = EventCount(values, "quiz_started");
                    var quizCompleted = EventCount(values, "quiz_completed");
                    return new WebsiteAnalyticsQuizRow(
                        summary.QuizTitles.TryGetValue(pair.Key, out var title) && !string.IsNullOrWhiteSpace(title) ? title : pair.Key,
                        quizOpens,
                        quizStarts,
                        quizCompleted,
                        EventCount(values, "score_shared"),
                        EventCount(values, "youtube_clicked"),
                        Percentage(quizCompleted, quizStarts));
                })
                .OrderByDescending(row => row.Completed)
                .ThenByDescending(row => row.Opens)
                .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (_websiteAnalyticsSourceGrid is not null)
        {
            _websiteAnalyticsSourceGrid.ItemsSource = summary.Sources
                .Select(pair => new WebsiteAnalyticsSourceRow(
                    FriendlyAnalyticsSource(pair.Key),
                    EventCount(pair.Value, "quiz_opened"),
                    EventCount(pair.Value, "quiz_started"),
                    EventCount(pair.Value, "quiz_completed"),
                    EventCount(pair.Value, "score_shared"),
                    EventCount(pair.Value, "youtube_clicked")))
                .OrderByDescending(row => row.Opens)
                .ThenByDescending(row => row.Completed)
                .ThenBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (_websiteAnalyticsStatusText is not null)
        {
            var range = !string.IsNullOrWhiteSpace(summary.From) && !string.IsNullOrWhiteSpace(summary.To)
                ? $"{summary.From} to {summary.To}"
                : $"last {summary.Days} days";
            _websiteAnalyticsStatusText.Text = completed == 0 && opens == 0
                ? $"No quiz activity has been recorded for {range} yet."
                : $"Showing aggregate website activity for {range}. No visitor identities are stored in this analytics feed.";
        }
    }

    private void SetWebsiteAnalyticsUnavailable(string message)
    {
        if (_websiteAnalyticsViewsText is not null) _websiteAnalyticsViewsText.Text = "—";
        if (_websiteAnalyticsOpensText is not null) _websiteAnalyticsOpensText.Text = "—";
        if (_websiteAnalyticsCompletionText is not null) _websiteAnalyticsCompletionText.Text = "—";
        if (_websiteAnalyticsYouTubeText is not null) _websiteAnalyticsYouTubeText.Text = "—";
        if (_websiteAnalyticsFunnelGrid is not null) _websiteAnalyticsFunnelGrid.ItemsSource = Array.Empty<WebsiteAnalyticsFunnelRow>();
        if (_websiteAnalyticsQuizGrid is not null) _websiteAnalyticsQuizGrid.ItemsSource = Array.Empty<WebsiteAnalyticsQuizRow>();
        if (_websiteAnalyticsSourceGrid is not null) _websiteAnalyticsSourceGrid.ItemsSource = Array.Empty<WebsiteAnalyticsSourceRow>();
        if (_websiteAnalyticsStatusText is not null) _websiteAnalyticsStatusText.Text = message;
    }

    private int SelectedWebsiteAnalyticsDays() =>
        _websiteAnalyticsPeriod?.SelectedItem is ComboBoxItem { Tag: int days } ? days : 30;

    private static long EventCount(IReadOnlyDictionary<string, long> events, string name) =>
        events.TryGetValue(name, out var value) ? Math.Max(0, value) : 0;

    private static string Percentage(long numerator, long denominator) =>
        denominator <= 0
            ? "—"
            : (Math.Max(0, numerator) * 100.0 / denominator).ToString("0.0'%'", CultureInfo.InvariantCulture);

    private static string FriendlyAnalyticsSource(string source) => source.Trim().ToLowerInvariant() switch
    {
        "home" => "Home",
        "quizzes" => "Quiz library",
        "leaderboard" => "Leaderboard",
        "profile" => "Profile",
        "quiz" => "Another quiz",
        "external" => "External",
        "direct" => "Direct",
        "internal" => "Other internal",
        _ => string.IsNullOrWhiteSpace(source) ? "Unknown" : source,
    };

    private static DataGrid BuildWebsiteAnalyticsGrid() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        BorderThickness = new Thickness(0),
        Background = Brushes.White,
        RowBackground = Brushes.White,
        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
        HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)),
    };

    private static DataGridTextColumn AnalyticsTextColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width),
    };

    private static Border BuildWebsiteAnalyticsSectionCard(string title, string subtitle, UIElement content)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Margin = new Thickness(15, 12, 15, 10) };
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(heading);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = root,
        };
    }

    private static void AddWebsiteAnalyticsStat(Grid parent, int column, string label, out TextBlock value)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        });
        value = new TextBlock
        {
            Text = "—",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 5, 0, 0),
        };
        stack.Children.Add(value);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }
}
