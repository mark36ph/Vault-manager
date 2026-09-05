using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public static class AutopilotNavigationLocator
{
    public static bool IsPrimaryNavigationPanel(IEnumerable<string?> buttonTags, int quizTabIndex)
    {
        ArgumentNullException.ThrowIfNull(buttonTags);

        var tags = buttonTags
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        var quizTag = quizTabIndex.ToString();
        if (!tags.Contains(quizTag, StringComparer.Ordinal))
            return false;

        return tags.Count(value => int.TryParse(value, out _)) >= 4;
    }
}

public static class AutopilotTopBarLocator
{
    public static bool IsHeaderActionPanel(IEnumerable<string?> buttonLabels)
    {
        ArgumentNullException.ThrowIfNull(buttonLabels);
        var labels = buttonLabels
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .ToArray();

        return labels.Any(value => value.Contains("Refresh", StringComparison.OrdinalIgnoreCase)) &&
               labels.Any(value => value.Contains("Updates", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLegacyProductionAction(string? label)
    {
        var text = (label ?? "").Trim();
        return text.Contains("Production", StringComparison.OrdinalIgnoreCase);
    }
}

public partial class MainShellWindow
{
    private static readonly string[] FactburstFirstPaintNavigationKeys =
    {
        "Autopilot",
        "Create",
        "Performance",
        "Library",
        "Website",
        "Web Analytics",
        "Users",
        "Comments",
        "SEO",
        "Advanced",
        "Settings",
    };

    private bool _autopilotShellActivationFixInitialized;
    private bool _autopilotShellActivationFixApplied;
    private DispatcherTimer? _autopilotShellActivationTimer;
    private readonly HashSet<Button> _autopilotGuardedProductionButtons = new();
    private bool _factburstFirstPaintPrepared;
    private int _factburstFirstPaintAttempts;
    private DispatcherTimer? _factburstFirstPaintTimer;

    public void PrepareFactburstFirstPaint()
    {
        if (_factburstFirstPaintPrepared)
            return;

        _factburstFirstPaintPrepared = true;
        Opacity = 0;
        Loaded += FactburstFirstPaint_Loaded;
        Closed += (_, _) => _factburstFirstPaintTimer?.Stop();
    }

    private void FactburstFirstPaint_Loaded(object sender, RoutedEventArgs e)
    {
        TryRevealFactburstFirstPaint();
        if (Opacity >= 1)
            return;

        _factburstFirstPaintTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _factburstFirstPaintTimer.Tick -= FactburstFirstPaintTimer_Tick;
        _factburstFirstPaintTimer.Tick += FactburstFirstPaintTimer_Tick;
        _factburstFirstPaintTimer.Start();
    }

    private void FactburstFirstPaintTimer_Tick(object? sender, EventArgs e) =>
        TryRevealFactburstFirstPaint();

    private void TryRevealFactburstFirstPaint()
    {
        TryActivateAutopilotShellAfterNavigationRebuild();

        if (!IsFactburstFirstPaintReady() && ++_factburstFirstPaintAttempts < 125)
            return;

        _factburstFirstPaintTimer?.Stop();
        Opacity = 1;
    }

    private bool IsFactburstFirstPaintReady()
    {
        if (!_autopilotShellActivationFixApplied ||
            _autopilotNavContainer?.Parent is null)
        {
            return false;
        }

        return FactburstFirstPaintNavigationKeys.All(_autopilotNavButtons.ContainsKey);
    }

    public void InitializeAutopilotShellActivationFix()
    {
        if (_autopilotShellActivationFixInitialized)
            return;
        _autopilotShellActivationFixInitialized = true;

        Loaded += (_, _) => StartAutopilotShellActivationFix();
        ContentRendered += (_, _) => TryActivateAutopilotShellAfterNavigationRebuild();
    }

    private void StartAutopilotShellActivationFix()
    {
        TryActivateAutopilotShellAfterNavigationRebuild();
        if (_autopilotShellActivationFixApplied)
            return;

        _autopilotShellActivationTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _autopilotShellActivationTimer.Tick -= AutopilotShellActivationTimer_Tick;
        _autopilotShellActivationTimer.Tick += AutopilotShellActivationTimer_Tick;
        _autopilotShellActivationTimer.Start();
    }

    private void AutopilotShellActivationTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsActive)
            return;

        TryActivateAutopilotShellAfterNavigationRebuild();
        if (_autopilotShellActivationFixApplied)
            _autopilotShellActivationTimer?.Stop();
    }

    private void TryActivateAutopilotShellAfterNavigationRebuild()
    {
        if (_autopilotShellActivationFixApplied || MainTabs is null || Content is not DependencyObject root)
            return;

        ApplyNavigationSections();
        if (!_navigationSectionsApplied)
            return;

        var navigation = ResolvePrimaryAutopilotNavigationPanel(root);
        if (navigation is null)
            return;

        if (!ReferenceEquals(_autopilotLegacyNavPanel, navigation))
        {
            if (_autopilotNavContainer?.Parent is Panel oldParent)
                oldParent.Children.Remove(_autopilotNavContainer);

            _autopilotNavContainer = null;
            _autopilotNavButtons.Clear();
            _autopilotLegacyNavPanel = navigation;
        }

        EnsureAutopilotPages();
        EnsureAutopilotNavigation();
        CompactLegacyNavigation();
        SimplifyTopBar(root);
        HideLegacyProductionTopBarAction(root);
        EnsureAutopilotNavigationGuard();

        if (_autopilotHomeTabIndex < 0)
            return;

        _autopilotShellActivationFixApplied = true;
        _autopilotFirstUiAttempts = 50;
        MainTabs.SelectedIndex = _autopilotHomeTabIndex;
        SelectAutopilotNav("Autopilot");
        _ = RefreshAutopilotHomeAsync();
    }

    private StackPanel? ResolvePrimaryAutopilotNavigationPanel(DependencyObject root)
    {
        var quizTag = _quizTabIndex.ToString();
        foreach (var candidate in FindVisualChildren<Button>(root)
                     .Where(button => string.Equals(button.Tag?.ToString(), quizTag, StringComparison.Ordinal)))
        {
            if (candidate.Parent is not StackPanel panel)
                continue;

            if (AutopilotNavigationLocator.IsPrimaryNavigationPanel(
                    panel.Children.OfType<Button>().Select(button => button.Tag?.ToString()),
                    _quizTabIndex))
            {
                return panel;
            }
        }

        return null;
    }

    private void HideLegacyProductionTopBarAction(DependencyObject root)
    {
        foreach (var panel in FindVisualChildren<StackPanel>(root))
        {
            var buttons = panel.Children.OfType<Button>().ToArray();
            if (!AutopilotTopBarLocator.IsHeaderActionPanel(buttons.Select(button => Convert.ToString(button.Content))))
                continue;

            foreach (var production in buttons.Where(button =>
                         AutopilotTopBarLocator.IsLegacyProductionAction(Convert.ToString(button.Content))))
            {
                if (_autopilotGuardedProductionButtons.Add(production))
                {
                    production.IsVisibleChanged += (_, _) =>
                    {
                        if (production.Visibility != Visibility.Collapsed)
                            production.Visibility = Visibility.Collapsed;
                    };
                }

                production.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void AutopilotTopBarGuardTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsActive || Content is not DependencyObject root)
            return;

        HideLegacyProductionTopBarAction(root);
    }

    private void EnsureAutopilotNavigationGuard()
    {
        _autopilotNavigationGuardTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _autopilotNavigationGuardTimer.Tick -= AutopilotNavigationGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Tick += AutopilotNavigationGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Tick -= AutopilotTopBarGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Tick += AutopilotTopBarGuardTimer_Tick;
        _autopilotNavigationGuardTimer.Start();
    }
}
