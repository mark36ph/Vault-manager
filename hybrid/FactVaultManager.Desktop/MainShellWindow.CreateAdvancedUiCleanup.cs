using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record FactburstAdvancedTool(string Title, string Note, string Route);

public sealed record FactburstAdvancedToolGroup(
    string Title,
    string Note,
    bool Collapsed,
    IReadOnlyList<FactburstAdvancedTool> Tools);

public static class FactburstDailyWorkspaceLayout
{
    private static readonly IReadOnlyDictionary<string, string> CreateStepLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["builder"] = "1   Setup",
            ["draft"] = "2   Questions",
            ["preview"] = "3   Preview",
            ["publish"] = "4   Details",
            ["export"] = "5   Finish",
        };

    public static string CreateStepLabel(string key) =>
        CreateStepLabels.TryGetValue(key ?? "", out var label) ? label : key ?? "";

    public static IReadOnlyList<FactburstAdvancedToolGroup> AdvancedGroups { get; } =
    [
        new FactburstAdvancedToolGroup(
            "Publishing & channel",
            "Use these when a release, platform account or performance check needs closer attention.",
            false,
            [
                new FactburstAdvancedTool("Upload Manager", "Uploads and platform state", "Upload Manager"),
                new FactburstAdvancedTool("Release Readiness", "Detailed release checks", "Release Readiness"),
                new FactburstAdvancedTool("YouTube Manager", "Analytics, comments and playlists", "YouTube Manager"),
                new FactburstAdvancedTool("Facebook Manager", "Facebook publishing tools", "Facebook Manager"),
                new FactburstAdvancedTool("Instagram Manager", "Instagram publishing tools", "Instagram Manager"),
                new FactburstAdvancedTool("Funnel Performance", "Promo-to-full-video tracking", "Funnel Performance"),
            ]),
        new FactburstAdvancedToolGroup(
            "Content & assets",
            "Supporting libraries used when you want to inspect or edit source material directly.",
            false,
            [
                new FactburstAdvancedTool("Questions", "Question bank", "Questions"),
                new FactburstAdvancedTool("Quiz Notes", "Production notes", "Quiz Notes"),
                new FactburstAdvancedTool("Media Library", "Media files", "Media Library"),
            ]),
        new FactburstAdvancedToolGroup(
            "Troubleshooting & legacy",
            "Open these only for diagnostics, manual recovery or older workflows. Routine production should stay in Autopilot.",
            true,
            [
                new FactburstAdvancedTool("Projects", "Legacy project workspace", "Projects"),
                new FactburstAdvancedTool("Production", "Legacy production workspace", "Production"),
                new FactburstAdvancedTool("Asset Review", "Asset diagnostics", "Asset Review"),
            ]),
    ];
}

public partial class MainShellWindow
{
    private bool _createAdvancedUiCleanupInitialized;
    private bool _cleanAdvancedPageApplied;
    private DispatcherTimer? _createAdvancedUiRetryTimer;

    public void InitializeCreateAdvancedUiCleanup()
    {
        if (_createAdvancedUiCleanupInitialized)
            return;

        _createAdvancedUiCleanupInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ApplyCreateAdvancedUiCleanup));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyCreateAdvancedUiCleanup));

        _createAdvancedUiRetryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _createAdvancedUiRetryTimer.Tick += (_, _) =>
        {
            ApplyCreateAdvancedUiCleanup();
            if (_quizWorkspaceNavigationInitialized && _cleanAdvancedPageApplied)
                _createAdvancedUiRetryTimer?.Stop();
        };
        _createAdvancedUiRetryTimer.Start();
        Closed += (_, _) => _createAdvancedUiRetryTimer?.Stop();
    }

    private void ApplyCreateAdvancedUiCleanup()
    {
        ApplyCreateScreenCleanup();
        ApplyAdvancedPageCleanup();
    }

    private void ApplyCreateScreenCleanup()
    {
        if (_quizTabIndex < 0 ||
            _quizTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizTabIndex] is not TabItem quizTab)
        {
            return;
        }

        var quizRoot = quizTab.Content as DependencyObject;
        if (quizRoot is null)
            return;

        foreach (var text in FindVisualChildren<TextBlock>(quizRoot))
        {
            if (string.Equals(text.Text, "Quizzes", StringComparison.Ordinal))
            {
                text.Text = "Create quiz";
            }
            else if (text.Text.StartsWith(
                         "Build a reusable question bank, pick random questions",
                         StringComparison.Ordinal))
            {
                text.Text = "Choose quiz settings and questions here. Autopilot handles routine production; use the later steps only when you want to review or customise a quiz.";
                text.TextWrapping = TextWrapping.Wrap;
            }
            else if (string.Equals(text.Text, "QUIZ WORKFLOW", StringComparison.Ordinal))
            {
                text.Text = "CREATE QUIZ";
            }
        }

        foreach (var pair in _quizWorkspaceNavButtons)
            pair.Value.Content = FactburstDailyWorkspaceLayout.CreateStepLabel(pair.Key);

        ApplyCreateWorkspacePageCopy(
            "builder",
            "Setup",
            "Choose video type, question count, timing and category. Pick random questions when you want to review a single quiz before production.");
        ApplyCreateWorkspacePageCopy(
            "draft",
            "Questions",
            "Review, reorder or replace the selected questions. This is optional when Autopilot is filling the release schedule for you.");
        ApplyCreateWorkspacePageCopy(
            "preview",
            "Preview",
            "Check the finished quiz look and layout when you want to customise the presentation.");
        ApplyCreateWorkspacePageCopy(
            "publish",
            "Details",
            "Review the series, episode and publishing text when a quiz needs manual changes.");
        ApplyCreateWorkspacePageCopy(
            "export",
            "Finish",
            "Autopilot is the normal production path. Voice, branding and manual render controls stay collapsed below unless you need them.");

        foreach (var button in FindVisualChildren<Button>(quizRoot))
        {
            var content = button.Content?.ToString() ?? "";
            if (string.Equals(content, "Continue to Preview", StringComparison.Ordinal))
                button.Content = "Next: Preview";
            else if (string.Equals(content, "Continue to Publish", StringComparison.Ordinal))
                button.Content = "Next: Details";
            else if (string.Equals(content, "Continue to Export", StringComparison.Ordinal))
                button.Content = "Next: Finish";
        }

        foreach (var expander in FindVisualChildren<Expander>(quizRoot))
        {
            if (expander.Header is TextBlock header &&
                string.Equals(header.Text, "Settings & manual render options", StringComparison.Ordinal))
            {
                header.Text = "Manual settings & render";
                expander.ToolTip = "Only open this when you need to override Autopilot production settings or render manually.";
            }
        }
    }

    private void ApplyCreateWorkspacePageCopy(string key, string title, string subtitle)
    {
        if (!_quizWorkspacePages.TryGetValue(key, out var page) ||
            page is not ScrollViewer { Content: StackPanel stack })
        {
            return;
        }

        var text = stack.Children.OfType<TextBlock>().Take(2).ToArray();
        if (text.Length > 0)
            text[0].Text = title;
        if (text.Length > 1)
            text[1].Text = subtitle;
    }

    private void ApplyAdvancedPageCleanup()
    {
        if (_cleanAdvancedPageApplied ||
            _autopilotAdvancedTabIndex < 0 ||
            _autopilotAdvancedTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_autopilotAdvancedTabIndex] is not TabItem advancedTab)
        {
            return;
        }

        advancedTab.Content = BuildCleanAdvancedPage();
        _cleanAdvancedPageApplied = true;
    }

    private FrameworkElement BuildCleanAdvancedPage()
    {
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 28) };
        root.Children.Add(new TextBlock
        {
            Text = "Advanced",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Detailed publishing, source-library and troubleshooting tools. Normal daily production starts from Autopilot.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 20),
        });

        foreach (var group in FactburstDailyWorkspaceLayout.AdvancedGroups)
        {
            if (group.Collapsed)
                root.Children.Add(BuildCollapsedAdvancedGroup(group));
            else
                root.Children.Add(BuildAdvancedGroup(group));
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };
    }

    private FrameworkElement BuildAdvancedGroup(FactburstAdvancedToolGroup group)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        AddAdvancedGroupHeading(section, group.Title, group.Note);
        section.Children.Add(BuildAdvancedToolWrap(group.Tools, quiet: false));
        return section;
    }

    private FrameworkElement BuildCollapsedAdvancedGroup(FactburstAdvancedToolGroup group)
    {
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = group.Note,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });

        return new Expander
        {
            Header = heading,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 10),
            Content = BuildAdvancedToolWrap(group.Tools, quiet: true),
        };
    }

    private static void AddAdvancedGroupHeading(Panel parent, string title, string note)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        parent.Children.Add(new TextBlock
        {
            Text = note,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 10),
        });
    }

    private FrameworkElement BuildAdvancedToolWrap(IReadOnlyList<FactburstAdvancedTool> tools, bool quiet)
    {
        var wrap = new WrapPanel { ItemWidth = 245, ItemHeight = 78 };
        foreach (var tool in tools)
            AddCleanAdvancedButton(wrap, tool, quiet);
        return wrap;
    }

    private void AddCleanAdvancedButton(Panel parent, FactburstAdvancedTool tool, bool quiet)
    {
        var stack = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = tool.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(quiet ? Color.FromRgb(71, 85, 105) : Color.FromRgb(16, 24, 40)),
        });
        stack.Children.Add(new TextBlock
        {
            Text = tool.Note,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var button = new Button
        {
            Content = stack,
            Width = 232,
            Height = 68,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Opacity = quiet ? 0.82 : 1,
            ToolTip = tool.Note,
        };
        button.Click += (_, _) => NavigateLegacy(tool.Route, "Advanced");
        parent.Children.Add(button);
    }
}
