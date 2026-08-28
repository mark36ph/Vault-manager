using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private Button? _quizAutopilotPrimaryButton;

    private FrameworkElement BuildQuizAutopilotPrimaryPanel()
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14),
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = layout;

        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = "Autopilot — recommended",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Generate the batch once and let the app carry it through the release-preparation steps automatically.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        });
        copy.Children.Add(AutopilotStep("1", "Generate and render fresh quizzes using your Builder rotation rules."));
        copy.Children.Add(AutopilotStep("2", "Schedule each full quiz to the next free YouTube day at 09:00."));
        copy.Children.Add(AutopilotStep("3", "Create promo Shorts, tracking links and website releases, then schedule YouTube/Facebook promos."));
        copy.Children.Add(AutopilotStep("4", "Leave only platform-limited tasks such as Instagram and Related video for manual confirmation."));
        layout.Children.Add(copy);

        var actions = new StackPanel
        {
            GridColumn = 2,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 190,
        };

        _quizAutopilotPrimaryButton = new Button
        {
            Content = "Generate + Autopilot",
            Height = 42,
            MinWidth = 190,
            Padding = new Thickness(18, 0, 18, 0),
            Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Generate fresh quizzes, schedule the full videos, create promo Shorts, tracking and website releases, then prepare promo scheduling.",
        };
        _quizAutopilotPrimaryButton.Click += GenerateAndScheduleQuizBatch_Click;
        _quizAutopilotPrimaryButton.Click += QuizAutopilotBatchButton_Click;
        actions.Children.Add(_quizAutopilotPrimaryButton);
        actions.Children.Add(new TextBlock
        {
            Text = "Uses the current Export settings below.",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0),
        });
        Grid.SetColumn(actions, 2);
        layout.Children.Add(actions);

        card.Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(HideLegacyQuizAutomationButtons));

        return card;
    }

    private FrameworkElement BuildQuizExportSettingsExpander(Border exportCard)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Change these when you want a different voice, logo, effects, music or video format. Batch Render and Render Final Video are manual tools only — they do not schedule a release.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(2, 0, 2, 10),
        });
        content.Children.Add(exportCard);

        var expander = new Expander
        {
            Header = "Settings & manual render options",
            Content = content,
            IsExpanded = false,
            FontWeight = FontWeights.SemiBold,
        };
        expander.Expanded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(HideLegacyQuizAutomationButtons));
        return expander;
    }

    private static FrameworkElement AutopilotStep(string number, string text)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2),
        };
        row.Children.Add(new TextBlock
        {
            Text = number + ".",
            Width = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
        });
        return row;
    }

    private void HideLegacyQuizAutomationButtons()
    {
        if (_quizFormatComboBox?.Parent is not Grid controls)
            return;

        foreach (var button in QuizExportButtons(controls))
        {
            if (ReferenceEquals(button, _quizAutopilotPrimaryButton))
                continue;

            var content = button.Content?.ToString() ?? "";
            if (string.Equals(button.Tag?.ToString(), QuizBatchAutomationButtonTag, StringComparison.Ordinal) ||
                string.Equals(content, "Generate + Schedule...", StringComparison.Ordinal) ||
                string.Equals(content, "Generate + Autopilot...", StringComparison.Ordinal))
            {
                button.Visibility = Visibility.Collapsed;
                button.IsEnabled = false;
            }
        }
    }

    private static IEnumerable<Button> QuizExportButtons(Grid controls)
    {
        foreach (var child in controls.Children)
        {
            if (child is Button direct)
                yield return direct;

            if (child is Panel panel)
            {
                foreach (var nested in panel.Children.OfType<Button>())
                    yield return nested;
            }
        }
    }
}
