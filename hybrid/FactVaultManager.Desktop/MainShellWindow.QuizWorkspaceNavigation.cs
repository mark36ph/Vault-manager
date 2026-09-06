using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizWorkspaceNavigationInitialized;
    private ContentControl? _quizWorkspaceContentHost;
    private readonly Dictionary<string, Button> _quizWorkspaceNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _quizWorkspacePages = new(StringComparer.OrdinalIgnoreCase);
    private string _quizWorkspaceSelectedPage = "builder";
    private bool _quizPreviewPageBuilt;

    public void InitializeQuizWorkspaceNavigationForApp()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(InitializeQuizWorkspaceNavigation));
    }

    private void InitializeQuizWorkspaceNavigation()
    {
        using var perf = PerformanceDiagnostics.Measure("QuizWorkspace.Initialize");

        if (_quizWorkspaceNavigationInitialized ||
            _quizTabIndex < 0 ||
            _quizTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizTabIndex] is not TabItem quizTab)
        {
            return;
        }

        var outerScrollViewer = quizTab.Content as ScrollViewer;
        var root = quizTab.Content as Grid ?? outerScrollViewer?.Content as Grid;
        if (root is null ||
            _quizDraftGrid?.Parent is not Grid draft ||
            draft.Parent is not Border draftCard)
        {
            return;
        }

        var settingsCard = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1);
        var oldWorkspace = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 2);
        var appendedCards = draft.Children
            .OfType<Border>()
            .OrderBy(Grid.GetRow)
            .ToArray();

        if (appendedCards.Length < 2 && oldWorkspace is not null)
        {
            appendedCards = oldWorkspace.Children
                .OfType<Border>()
                .Where(card => !ReferenceEquals(card, draftCard))
                .OrderBy(Grid.GetRow)
                .ToArray();
        }

        if (settingsCard is null || oldWorkspace is null || appendedCards.Length < 2)
            return;

        if (outerScrollViewer is not null)
        {
            outerScrollViewer.Content = null;
            quizTab.Content = null;
            quizTab.Content = root;
        }

        var draftControlsCard = appendedCards[0];
        var exportCard = appendedCards[^1];

        _quizWorkspaceNavigationInitialized = true;

        DetachQuizWorkspaceElement(settingsCard);
        DetachQuizWorkspaceElement(draftCard);
        DetachQuizWorkspaceElement(draftControlsCard);
        DetachQuizWorkspaceElement(exportCard);
        root.Children.Remove(oldWorkspace);

        settingsCard.Margin = new Thickness(0);
        StyleQuizWorkspaceCard(draftCard);
        draftCard.Margin = new Thickness(0);
        StyleQuizWorkspaceCard(draftControlsCard);
        draftControlsCard.Margin = new Thickness(0, 10, 0, 0);
        StyleQuizWorkspaceCard(exportCard);
        exportCard.Margin = new Thickness(0);

        if (root.RowDefinitions.Count >= 3)
        {
            root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions[2].Height = new GridLength(0);
        }

        var workspace = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        var sidebar = QuizCard(new Thickness(8));
        workspace.Children.Add(sidebar);
        var sidebarStack = new StackPanel();
        sidebar.Child = sidebarStack;
        sidebarStack.Children.Add(new TextBlock
        {
            Text = "QUIZ WORKFLOW",
            Foreground = new SolidColorBrush(Color.FromRgb(152, 162, 179)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 8, 6, 8),
        });

        AddQuizWorkspaceNav(sidebarStack, "builder", "Builder");
        AddQuizWorkspaceNav(sidebarStack, "draft", "Draft");
        AddQuizWorkspaceNav(sidebarStack, "preview", "Preview");
        AddQuizWorkspaceNav(sidebarStack, "publish", "Publish");
        AddQuizWorkspaceNav(sidebarStack, "export", "Export");

        var contentBorder = QuizCard(new Thickness(18));
        Grid.SetColumn(contentBorder, 2);
        workspace.Children.Add(contentBorder);
        _quizWorkspaceContentHost = new ContentControl();
        contentBorder.Child = _quizWorkspaceContentHost;

        _quizWorkspacePages["builder"] = BuildQuizWorkspacePage(
            "Builder",
            "Choose the question count, timing, category, difficulty, and rotation rules used to build a quiz draft.",
            settingsCard);
        _quizWorkspacePages["draft"] = BuildQuizWorkspacePage(
            "Draft",
            "Review the selected questions, change their order, replace or remove questions, and control answer shuffling.",
            draftCard,
            draftControlsCard,
            BuildQuizWorkflowContinueButton("preview", "Continue to Preview"));

        // Preview is intentionally lazy: constructing its controls and initial image was the
        // most expensive measured workspace operation (~228 ms). Build it only when selected.
        _quizWorkspacePages["preview"] = BuildQuizWorkspacePage(
            "Preview",
            "Preview the actual quiz cards, choose a visual theme, position the logo, save presets, and run layout preflight checks.");

        _quizWorkspacePages["publish"] = BuildQuizWorkspacePage(
            "Publish",
            "Manage quiz series and episode numbering, then prepare editable YouTube title, description, hashtags, and pinned-comment metadata.",
            BuildQuizPublishingPanel(),
            BuildQuizWorkflowContinueButton("export", "Continue to Export"));
        _quizWorkspacePages["export"] = BuildQuizWorkspacePage(
            "Export",
            "For normal production, use Autopilot. Open the settings section only when you want to change voice, branding, effects, music, video format, or use a manual render.",
            BuildQuizAutopilotPrimaryPanel(),
            BuildQuizExportSettingsExpander(exportCard));

        SelectQuizWorkspacePage(_quizWorkspaceSelectedPage);
    }

    private void AddQuizWorkspaceNav(Panel parent, string key, string text)
    {
        var button = new Button
        {
            Content = text,
            Tag = key,
            Height = 36,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        };
        button.Click += (_, _) => SelectQuizWorkspacePage(key);
        _quizWorkspaceNavButtons[key] = button;
        parent.Children.Add(button);
    }

    private void SelectQuizWorkspacePage(string key)
    {
        using var perf = PerformanceDiagnostics.Measure($"QuizWorkspace.Navigate.{key}");

        if (_quizWorkspaceContentHost is null || !_quizWorkspacePages.TryGetValue(key, out var page))
            return;

        if (string.Equals(key, "preview", StringComparison.OrdinalIgnoreCase) && !_quizPreviewPageBuilt)
        {
            using var buildPerf = PerformanceDiagnostics.Measure("QuizWorkspace.BuildPreviewPage");
            page = BuildQuizWorkspacePage(
                "Preview",
                "Preview the actual quiz cards, choose a visual theme, position the logo, save presets, and run layout preflight checks.",
                BuildQuizPreviewPanel(),
                BuildQuizWorkflowContinueButton("publish", "Continue to Publish"));
            _quizWorkspacePages["preview"] = page;
            _quizPreviewPageBuilt = true;
        }

        _quizWorkspaceSelectedPage = key;
        _quizWorkspaceContentHost.Content = page;
        foreach (var pair in _quizWorkspaceNavButtons)
        {
            var selected = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = selected
                ? new SolidColorBrush(Color.FromRgb(234, 241, 255))
                : Brushes.Transparent;
            pair.Value.Foreground = selected
                ? new SolidColorBrush(Color.FromRgb(23, 92, 211))
                : new SolidColorBrush(Color.FromRgb(52, 64, 84));
            pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (string.Equals(key, "preview", StringComparison.OrdinalIgnoreCase))
        {
            using var refreshPerf = PerformanceDiagnostics.Measure("QuizWorkspace.RefreshPreview");
            RefreshQuizPreview();
        }
        else if (string.Equals(key, "publish", StringComparison.OrdinalIgnoreCase))
        {
            using var refreshPerf = PerformanceDiagnostics.Measure("QuizWorkspace.RefreshPublishing");
            RefreshQuizPublishingPage();
        }
    }

    private FrameworkElement BuildQuizWorkflowContinueButton(string nextPageKey, string label)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var next = new Button
        {
            Content = label,
            MinHeight = 36,
            Padding = new Thickness(16, 0, 16, 0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };
        next.Click += (_, _) => ContinueQuizWorkflow(nextPageKey);
        actions.Children.Add(next);
        return actions;
    }

    private void ContinueQuizWorkflow(string nextPageKey)
    {
        if (_quizDraftQuestions.Count == 0)
        {
            MessageBox.Show(
                this,
                "Build a quiz draft before continuing.",
                "Quiz Workflow",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectQuizWorkspacePage("builder");
            return;
        }

        if (string.Equals(nextPageKey, "export", StringComparison.OrdinalIgnoreCase) &&
            !TryValidateCurrentQuizPublishingMetadata())
        {
            MessageBox.Show(
                this,
                "Generate valid publishing metadata before continuing to Export.",
                "Quiz Workflow",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectQuizWorkspacePage(nextPageKey);
    }

    private static FrameworkElement BuildQuizWorkspacePage(
        string title,
        string subtitle,
        params FrameworkElement[] sections)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 16),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var section in sections)
            stack.Children.Add(section);

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private static void StyleQuizWorkspaceCard(Border card)
    {
        card.Background = Brushes.White;
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236));
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(8);
    }

    private static void DetachQuizWorkspaceElement(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
        }
    }
}
