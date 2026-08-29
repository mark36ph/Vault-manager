using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record AutopilotHomeSummary(
    string Health,
    int Scheduled,
    int Ready,
    int ManualTasks,
    int CoverageDays,
    string NextRelease,
    string NextReleaseTime,
    string GrowthCategory);

public static class AutopilotHomePlanner
{
    public static string Health(bool running, int manualTasks) =>
        running ? "Working" : manualTasks > 0 ? "Needs you" : "Healthy";

    public static int ScheduleCoverageDays(IEnumerable<DateTimeOffset> releases, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(releases);
        var latest = releases.Where(value => value >= now).OrderBy(value => value).LastOrDefault();
        if (latest == default) return 0;
        return Math.Max(1, (int)Math.Ceiling((latest.Date - now.Date).TotalDays) + 1);
    }

    public static bool NeedsManualValue(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return false;
        return !text.Equals("Ready", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Done", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Complete", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("N/A", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Not needed", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Scheduled", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
    }

    public static int DueInstagramPromoCount(
        IEnumerable<ScheduledReleaseReadinessRow> rows,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Count(row =>
            string.Equals(row.InstagramPromo, "Next day", StringComparison.OrdinalIgnoreCase) &&
            AutopilotNeedsYouTaskPlanner.PromoDueAt(row.PublishAt) <= now);
    }
}

public partial class MainShellWindow
{
    private const string AutopilotFirstNavTag = "autopilot-first-nav";
    private bool _autopilotFirstUiInitialized;
    private bool _autopilotHomeRefreshing;
    private int _autopilotFirstUiAttempts;
    private int _autopilotHomeTabIndex = -1;
    private int _autopilotAdvancedTabIndex = -1;
    private StackPanel? _autopilotLegacyNavPanel;
    private StackPanel? _autopilotNavContainer;
    private DispatcherTimer? _autopilotNavigationGuardTimer;
    private readonly Dictionary<string, Button> _autopilotNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _autopilotHealthText;
    private TextBlock? _autopilotNextReleaseText;
    private TextBlock? _autopilotNextReleaseTimeText;
    private TextBlock? _autopilotScheduleText;
    private TextBlock? _autopilotScheduleNoteText;
    private TextBlock? _autopilotNeedsText;
    private TextBlock? _autopilotNeedsNoteText;
    private TextBlock? _autopilotGrowthText;
    private TextBlock? _autopilotGrowthNoteText;
    private StackPanel? _autopilotNeedsPanel;
    private TextBlock? _autopilotStatusText;

    public void InitializeAutopilotFirstUi()
    {
        if (_autopilotFirstUiInitialized) return;
        _autopilotFirstUiInitialized = true;

        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(EnsureAutopilotFirstUi));
        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            CompactLegacyNavigation();
            if (MainTabs.SelectedIndex == _autopilotHomeTabIndex)
                await RefreshAutopilotHomeAsync();
        };
    }

    private void EnsureAutopilotFirstUi()
    {
        if (MainTabs is null || Content is not DependencyObject root)
        {
            RetryAutopilotFirstUi();
            return;
        }

        var dashboardButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "⌂   Dashboard", StringComparison.Ordinal));
        if (dashboardButton?.Parent is not StackPanel navigation)
        {
            RetryAutopilotFirstUi();
            return;
        }

        _autopilotLegacyNavPanel = navigation;
        EnsureAutopilotPages();
        EnsureAutopilotNavigation();
        CompactLegacyNavigation();
        SimplifyTopBar(root);

        _autopilotNavigationGuardTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _autopilotNavigationGuardTimer.Tick -= AutopilotNavigationGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Tick += AutopilotNavigationGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Start();

        MainTabs.SelectedIndex = _autopilotHomeTabIndex;
        SelectAutopilotNav("Autopilot");
        _ = RefreshAutopilotHomeAsync();
    }

    private void AutopilotNavigationGuardTimer_Tick(object? sender, EventArgs e)
    {
        CompactLegacyNavigation();
    }

    private void RetryAutopilotFirstUi()
    {
        if (++_autopilotFirstUiAttempts >= 50) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureAutopilotFirstUi();
        };
        timer.Start();
    }

    private void EnsureAutopilotPages()
    {
        if (_autopilotHomeTabIndex < 0)
        {
            var home = new TabItem { Content = BuildAutopilotHomePage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                home.Style = hiddenStyle;
            MainTabs.Items.Add(home);
            _autopilotHomeTabIndex = MainTabs.Items.Count - 1;
        }

        if (_autopilotAdvancedTabIndex < 0)
        {
            var advanced = new TabItem { Content = BuildAutopilotAdvancedPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                advanced.Style = hiddenStyle;
            MainTabs.Items.Add(advanced);
            _autopilotAdvancedTabIndex = MainTabs.Items.Count - 1;
        }
    }

    private void EnsureAutopilotNavigation()
    {
        if (_autopilotLegacyNavPanel is null) return;
        if (_autopilotNavContainer?.Parent is not null) return;

        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        container.Children.Add(new TextBlock
        {
            Text = "FACTBURST",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 0, 0, 8),
        });
        AddAutopilotNavButton(container, "Autopilot", "✦   Autopilot", () => NavigateAutopilotHome());
        AddAutopilotNavButton(container, "Create", "+   Create", () => NavigateLegacy("Quizzes", "Create"));
        AddAutopilotNavButton(container, "Performance", "↗   Performance", () => NavigateLegacy("YouTube Manager", "Performance"));
        AddAutopilotNavButton(container, "Library", "▤   Library", () => NavigateLegacy("Quiz History", "Library"));
        AddAutopilotNavButton(container, "Advanced", "⋯   Advanced", () => NavigateAutopilotAdvanced());
        container.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            Margin = new Thickness(12, 12, 12, 12),
        });
        AddAutopilotNavButton(container, "Settings", "⚙   Settings", () => NavigateLegacy("Settings", "Settings"));

        _autopilotLegacyNavPanel.Children.Insert(0, container);
        _autopilotNavContainer = container;
    }

    private void AddAutopilotNavButton(Panel parent, string key, string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Tag = AutopilotFirstNavTag + ":" + key,
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        button.Click += (_, _) => action();
        parent.Children.Add(button);
        _autopilotNavButtons[key] = button;
    }

    private void CompactLegacyNavigation()
    {
        if (_autopilotLegacyNavPanel is null) return;
        foreach (UIElement child in _autopilotLegacyNavPanel.Children)
        {
            if (ReferenceEquals(child, _autopilotNavContainer))
            {
                child.Visibility = Visibility.Visible;
                continue;
            }
            child.Visibility = Visibility.Collapsed;
        }
    }

    private void SimplifyTopBar(DependencyObject root)
    {
        var production = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(Convert.ToString(button.Content), "▷  Production", StringComparison.Ordinal));
        if (production is not null)
            production.Visibility = Visibility.Collapsed;

        var title = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(text => string.Equals(text.Text, "FactVaultManager", StringComparison.Ordinal));
        if (title is not null)
            title.Text = "Factburst Quiz Manager";
        HeaderStatusText.Text = "Autopilot supervises publishing, promotion and release checks";
    }

    private void NavigateAutopilotHome()
    {
        MainTabs.SelectedIndex = _autopilotHomeTabIndex;
        SelectAutopilotNav("Autopilot");
        _ = RefreshAutopilotHomeAsync();
    }

    private void NavigateAutopilotAdvanced()
    {
        MainTabs.SelectedIndex = _autopilotAdvancedTabIndex;
        SelectAutopilotNav("Advanced");
    }

    private void NavigateLegacy(string fragment, string selectedNav)
    {
        var route = FindLegacyNavigationButton(fragment);
        if (route is null || !int.TryParse(route.Tag?.ToString(), out var tabIndex))
        {
            MessageBox.Show(this,
                $"The {fragment} page is still starting. Try again in a moment.",
                "Factburst Autopilot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MainTabs.SelectedIndex = tabIndex;
        ApplyNavigationSelection(tabIndex);
        SelectAutopilotNav(selectedNav);
    }

    private Button? FindLegacyNavigationButton(string fragment)
    {
        if (_autopilotLegacyNavPanel is null) return null;
        return _autopilotLegacyNavPanel.Children
            .OfType<Button>()
            .Where(button => button.Tag?.ToString()?.StartsWith(AutopilotFirstNavTag, StringComparison.Ordinal) != true)
            .FirstOrDefault(button => (Convert.ToString(button.Content) ?? "")
                .Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectAutopilotNav(string key)
    {
        foreach (var pair in _autopilotNavButtons)
        {
            var selected = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected
                ? new SolidColorBrush(Color.FromRgb(234, 242, 255))
                : Brushes.Transparent;
            pair.Value.Foreground = selected
                ? new SolidColorBrush(Color.FromRgb(23, 92, 211))
                : new SolidColorBrush(Color.FromRgb(52, 64, 84));
            pair.Value.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(23, 92, 211))
                : Brushes.Transparent;
        }
    }

    private FrameworkElement BuildAutopilotHomePage()
    {
        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Factburst Autopilot",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "One place to create, schedule and supervise the channel. The detailed tools stay out of the way unless you need them.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);
        var healthCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(236, 253, 243)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 235, 115)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 10, 16, 10),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var healthStack = new StackPanel { Orientation = Orientation.Horizontal };
        healthStack.Children.Add(new TextBlock
        {
            Text = "●  AUTOPILOT  ",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(2, 122, 72)),
        });
        _autopilotHealthText = new TextBlock
        {
            Text = "Starting",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(2, 122, 72)),
        };
        healthStack.Children.Add(_autopilotHealthText);
        healthCard.Child = healthStack;
        Grid.SetColumn(healthCard, 1);
        header.Children.Add(healthCard);
        root.Children.Add(header);

        var actionCard = new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(13, 36, 92), 0),
                    new(Color.FromRgb(37, 52, 144), 0.62),
                    new(Color.FromRgb(92, 39, 154), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 18, 22, 18),
            Margin = new Thickness(0, 0, 0, 16),
        };
        var actionGrid = new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actionText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        actionText.Children.Add(new TextBlock
        {
            Text = "Generate + Fill Schedule",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        actionText.Children.Add(new TextBlock
        {
            Text = "Autopilot chooses the category mix, renders the quizzes, schedules releases, prepares the website and tracking, creates promos and supervises post-release tasks.",
            Foreground = new SolidColorBrush(Color.FromRgb(207, 220, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 850,
            Margin = new Thickness(0, 5, 20, 0),
        });
        actionGrid.Children.Add(actionText);
        var run = new Button
        {
            Content = "Generate + Fill Schedule",
            MinWidth = 190,
            Height = 44,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            Foreground = Brushes.White,
        };
        run.Click += (_, _) => StartAutopilotFromHome();
        Grid.SetColumn(run, 1);
        actionGrid.Children.Add(run);
        actionCard.Child = actionGrid;
        Grid.SetRow(actionCard, 1);
        root.Children.Add(actionCard);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddHomeStat(stats, 0, "Next release", out _autopilotNextReleaseText, out _autopilotNextReleaseTimeText);
        AddHomeStat(stats, 1, "Schedule", out _autopilotScheduleText, out _autopilotScheduleNoteText);
        AddHomeStat(stats, 2, "Needs you", out _autopilotNeedsText, out _autopilotNeedsNoteText);
        AddHomeStat(stats, 3, "Growth", out _autopilotGrowthText, out _autopilotGrowthNoteText);
        Grid.SetRow(stats, 2);
        root.Children.Add(stats);

        var detailGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });

        var handled = BuildHomePanel("Autopilot is handling");
        if (handled.Child is StackPanel handledStack)
        {
            handledStack.Children.Add(new TextBlock
            {
                Text = "✓  Full-video scheduling and performance-weighted category rotation\n✓  Website releases and branded tracking links\n✓  YouTube category playlists and first comments\n✓  Facebook first comments and release audits\n✓  Winner follow-ups, extra promos and packaging rescue preparation",
                Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
                FontSize = 14,
                LineHeight = 27,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });
        }
        detailGrid.Children.Add(handled);

        var needs = BuildHomePanel("Needs you");
        _autopilotNeedsPanel = needs.Child as StackPanel;
        Grid.SetColumn(needs, 2);
        detailGrid.Children.Add(needs);
        Grid.SetRow(detailGrid, 3);
        root.Children.Add(detailGrid);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _autopilotStatusText = new TextBlock
        {
            Text = "Autopilot is starting...",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(_autopilotStatusText);
        var refresh = new Button { Content = "Refresh status", MinWidth = 112 };
        refresh.Click += async (_, _) => await RefreshAutopilotHomeAsync();
        Grid.SetColumn(refresh, 1);
        footer.Children.Add(refresh);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    private static void AddHomeStat(
        Grid parent,
        int column,
        string label,
        out TextBlock value,
        out TextBlock note)
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
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
        };
        note = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0),
        };
        stack.Children.Add(value);
        stack.Children.Add(note);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private static Border BuildHomePanel(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Child = stack,
        };
    }

    private FrameworkElement BuildAutopilotAdvancedPage()
    {
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 26) };
        root.Children.Add(new TextBlock
        {
            Text = "Advanced tools",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
        });
        root.Children.Add(new TextBlock
        {
            Text = "These pages still exist for diagnostics, manual fixes and detailed control. Normal production should start from Autopilot.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18),
        });

        var wrap = new WrapPanel { ItemWidth = 245, ItemHeight = 78 };
        AddAdvancedButton(wrap, "Upload Manager", "Uploads and platform state", "Upload Manager");
        AddAdvancedButton(wrap, "Release Readiness", "Detailed release checks", "Release Readiness");
        AddAdvancedButton(wrap, "YouTube Manager", "Analytics, comments, playlists", "YouTube Manager");
        AddAdvancedButton(wrap, "Facebook Manager", "Facebook publishing tools", "Facebook Manager");
        AddAdvancedButton(wrap, "Instagram Manager", "Instagram publishing tools", "Instagram Manager");
        AddAdvancedButton(wrap, "Funnel Performance", "Promo-to-full-video tracking", "Funnel Performance");
        AddAdvancedButton(wrap, "Questions", "Question bank", "Questions");
        AddAdvancedButton(wrap, "Quiz Notes", "Production notes", "Quiz Notes");
        AddAdvancedButton(wrap, "Projects", "Legacy project workspace", "Projects");
        AddAdvancedButton(wrap, "Production", "Legacy production workspace", "Production");
        AddAdvancedButton(wrap, "Media Library", "Media files", "Media Library");
        AddAdvancedButton(wrap, "Asset Review", "Asset diagnostics", "Asset Review");
        root.Children.Add(wrap);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    private void AddAdvancedButton(Panel parent, string title, string note, string route)
    {
        var stack = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15 });
        stack.Children.Add(new TextBlock
        {
            Text = note,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        });
        var button = new Button
        {
            Content = stack,
            Width = 232,
            Height = 68,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => NavigateLegacy(route, "Advanced");
        parent.Children.Add(button);
    }

    private void StartAutopilotFromHome()
    {
        NavigateLegacy("Quizzes", "Create");
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (Content is not DependencyObject root) return;
                var button = FindVisualChildren<Button>(root)
                    .FirstOrDefault(value =>
                    {
                        var content = Convert.ToString(value.Content);
                        return string.Equals(content, "Generate + Autopilot", StringComparison.Ordinal) ||
                               string.Equals(content, "Generate + Autopilot...", StringComparison.Ordinal);
                    });
                if (button is not null && button.IsEnabled)
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
    }

    private async Task RefreshAutopilotHomeAsync()
    {
        if (_autopilotHomeRefreshing || _autopilotHomeTabIndex < 0) return;
        _autopilotHomeRefreshing = true;
        try
        {
            EnsureScheduledReleaseReadinessPage();
            if (_scheduledReadinessGrid is not null)
                await RefreshScheduledReleaseReadinessAsync(false);

            var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            var snapshots = YouTubeGrowthSnapshotStore.Load(YouTubeGrowthStorePath())
                .GroupBy(snapshot => snapshot.HistoryId)
                .Select(group => group.OrderByDescending(snapshot => snapshot.CheckedAtUtc).First())
                .ToList();
            var growth = YouTubeGrowthUiSummaryBuilder.Build(BuildYouTubeGrowthCategoryPlan(1), snapshots);
            var rows = _scheduledReadinessRows
                .Where(row => row.PublishAt >= DateTimeOffset.Now.AddHours(-2))
                .OrderBy(row => row.PublishAt)
                .ToList();

            var manual = BuildManualAutopilotTasks(rows, state, snapshots);
            var next = rows.FirstOrDefault(row => row.PublishAt >= DateTimeOffset.Now.AddMinutes(-5));
            var ready = rows.Count(row => row.ReadyCount == row.TotalChecks);
            var coverage = AutopilotHomePlanner.ScheduleCoverageDays(rows.Select(row => row.PublishAt), DateTimeOffset.Now);
            var health = AutopilotHomePlanner.Health(_fullAutopilotRunning, manual.Count);

            if (_autopilotHealthText is not null) _autopilotHealthText.Text = health;
            if (_autopilotNextReleaseText is not null) _autopilotNextReleaseText.Text = next?.Quiz ?? "No release queued";
            if (_autopilotNextReleaseTimeText is not null)
                _autopilotNextReleaseTimeText.Text = next is null ? "Use Generate + Fill Schedule" : next.PublishAt.ToString("ddd dd MMM • HH:mm");
            if (_autopilotScheduleText is not null) _autopilotScheduleText.Text = $"{rows.Count:N0} quizzes";
            if (_autopilotScheduleNoteText is not null)
                _autopilotScheduleNoteText.Text = coverage == 0 ? "Schedule is empty" : $"about {coverage:N0} day{(coverage == 1 ? "" : "s")} covered • {ready:N0} ready";
            if (_autopilotNeedsText is not null) _autopilotNeedsText.Text = manual.Count == 0 ? "Nothing" : manual.Count.ToString("N0");
            if (_autopilotNeedsNoteText is not null)
                _autopilotNeedsNoteText.Text = manual.Count == 0 ? "Autopilot is handling the queue" : "Only tasks requiring your input";
            if (_autopilotGrowthText is not null) _autopilotGrowthText.Text = growth.TopCategory;
            if (_autopilotGrowthNoteText is not null)
                _autopilotGrowthNoteText.Text = growth.Winners > 0 ? $"{growth.Winners:N0} Winner{(growth.Winners == 1 ? "" : "s")} feeding Autopilot" : "Learning from full-video performance";

            RenderManualAutopilotTasks(manual);
            if (_autopilotStatusText is not null)
            {
                var pendingWinner = state.WinnerFollowUps.Count(item => !item.Consumed);
                var pendingPromo = state.WinnerPromoBundles.Count(item => !item.Completed);
                _autopilotStatusText.Text =
                    $"Autopilot {health.ToLowerInvariant()} • {rows.Count:N0} scheduled • {state.YouTubePostReleaseWatchIds.Count:N0} release checks pending" +
                    (pendingWinner > 0 ? $" • {pendingWinner:N0} Winner follow-up queued" : "") +
                    (pendingPromo > 0 ? $" • {pendingPromo:N0} Winner promo bundle pending" : "");
            }
            HeaderStatusText.Text = $"Autopilot: {health} • {rows.Count:N0} scheduled • {manual.Count:N0} need you";
            CompactLegacyNavigation();
        }
        catch (Exception error)
        {
            if (_autopilotStatusText is not null)
                _autopilotStatusText.Text = "Autopilot status could not refresh: " + error.Message;
            Debug.WriteLine("Autopilot home refresh failed: " + error);
        }
        finally
        {
            _autopilotHomeRefreshing = false;
        }
    }

    private sealed record AutopilotManualTask(string Title, string Note, string Route, string ButtonText);

    private List<AutopilotManualTask> BuildManualAutopilotTasks(
        IReadOnlyList<ScheduledReleaseReadinessRow> rows,
        FactburstFullAutopilotState state,
        IReadOnlyList<YouTubeGrowthSnapshot> snapshots)
    {
        var tasks = new List<AutopilotManualTask>();
        var relatedVideoCount = rows.Count(row => RowHasManualState(row, "RelatedVideo", "Related video"));
        if (relatedVideoCount > 0)
        {
            tasks.Add(new AutopilotManualTask(
                $"Set Related Video on {relatedVideoCount:N0} Short{(relatedVideoCount == 1 ? "" : "s")}",
                "YouTube keeps this Studio-only, so Autopilot cannot set it through the API.",
                "Release Readiness",
                "Open tasks"));
        }

        var instagramCount = AutopilotHomePlanner.DueInstagramPromoCount(rows, DateTimeOffset.Now);
        if (instagramCount > 0)
        {
            tasks.Add(new AutopilotManualTask(
                $"Instagram: {instagramCount:N0} promo{(instagramCount == 1 ? "" : "s")}",
                "Only Instagram promos whose posting time has arrived are shown here.",
                "Instagram Manager",
                "Open Instagram"));
        }

        var rescueCount = snapshots.Count(snapshot =>
            string.Equals(snapshot.Label, "Packaging rescue", StringComparison.OrdinalIgnoreCase) &&
            snapshot.RescuePackagePrepared);
        if (rescueCount > 0)
        {
            tasks.Add(new AutopilotManualTask(
                $"Review {rescueCount:N0} packaging rescue{(rescueCount == 1 ? "" : "s")}",
                "Replacement A/B/C title and thumbnail packages are prepared; applying them remains your decision.",
                "YouTube Manager",
                "Review rescue"));
        }

        if (state.ReplyDrafts.Count > 0)
        {
            tasks.Add(new AutopilotManualTask(
                $"Review {state.ReplyDrafts.Count:N0} viewer repl{(state.ReplyDrafts.Count == 1 ? "y" : "ies")}",
                "Autopilot drafted replies but will not speak publicly to viewers without approval.",
                "YouTube Manager",
                "Review replies"));
        }

        var auditAttention = state.PostReleaseAudits.Count(record =>
            !string.IsNullOrWhiteSpace(record.Attention) &&
            (record.Attention.Contains("title differs", StringComparison.OrdinalIgnoreCase) ||
             record.Attention.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)));
        if (auditAttention > 0)
        {
            tasks.Add(new AutopilotManualTask(
                $"Check {auditAttention:N0} release packaging warning{(auditAttention == 1 ? "" : "s")}",
                "Autopilot will repair safe release state, but it will not blindly overwrite a live title or thumbnail.",
                "Release Readiness",
                "Review warning"));
        }
        return tasks;
    }

    private static bool RowHasManualState(ScheduledReleaseReadinessRow row, params string[] propertyHints)
    {
        var properties = row.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (!propertyHints.Any(hint => property.Name.Contains(hint.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)))
                continue;
            var value = Convert.ToString(property.GetValue(row));
            if (AutopilotHomePlanner.NeedsManualValue(value)) return true;
        }
        return false;
    }

    private void RenderManualAutopilotTasks(IReadOnlyList<AutopilotManualTask> tasks)
    {
        if (_autopilotNeedsPanel is null) return;
        while (_autopilotNeedsPanel.Children.Count > 1)
            _autopilotNeedsPanel.Children.RemoveAt(_autopilotNeedsPanel.Children.Count - 1);

        if (tasks.Count == 0)
        {
            _autopilotNeedsPanel.Children.Add(new TextBlock
            {
                Text = "✓ Nothing needs your attention right now.\n\nAutopilot will keep checking releases in the background.",
                Foreground = new SolidColorBrush(Color.FromRgb(2, 122, 72)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });
            return;
        }

        foreach (var task in tasks.Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = task.Title,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            });
            text.Children.Add(new TextBlock
            {
                Text = task.Note,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 10, 0),
            });
            row.Children.Add(text);
            var button = new Button { Content = task.ButtonText, MinWidth = 105, VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => NavigateLegacy(task.Route, task.Route.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ? "Performance" : "Advanced");
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            _autopilotNeedsPanel.Children.Add(row);
        }
    }
}
