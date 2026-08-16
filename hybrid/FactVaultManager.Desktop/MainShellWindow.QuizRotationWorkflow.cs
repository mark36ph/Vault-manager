using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizRotationWorkflowInitialized;
    private CheckBox? _quizPreferLeastUsedCheckBox;
    private CheckBox? _quizAvoidRecentCheckBox;
    private TextBox? _quizRecentQuizCountTextBox;

    private void InitializeQuizRotationWorkflow()
    {
        if (_quizRotationWorkflowInitialized || Content is not DependencyObject root)
            return;

        var pickButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Pick random questions", StringComparison.Ordinal));
        if (pickButton?.Parent is not Grid settings)
            return;

        _quizRotationWorkflowInitialized = true;
        pickButton.Click -= PickRandomQuizQuestions_Click;
        pickButton.Click += PickSmartQuizQuestions_Click;
        pickButton.Content = "Build quiz draft";
        pickButton.ToolTip = "Build a draft using the rotation options below.";

        if (settings.RowDefinitions.Count == 0)
        {
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else if (settings.RowDefinitions.Count == 1)
        {
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var rotation = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        rotation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(rotation, 1);
        Grid.SetColumnSpan(rotation, Math.Max(1, settings.ColumnDefinitions.Count));
        settings.Children.Add(rotation);

        _quizPreferLeastUsedCheckBox = new CheckBox
        {
            Content = "Prefer least-used questions",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Questions with lower Times used are chosen first. Equal-usage questions are randomized.",
        };
        rotation.Children.Add(_quizPreferLeastUsedCheckBox);

        _quizAvoidRecentCheckBox = new CheckBox
        {
            Content = "Avoid questions from last",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Avoid questions used in recent successfully exported quizzes when enough alternatives exist.",
        };
        Grid.SetColumn(_quizAvoidRecentCheckBox, 2);
        rotation.Children.Add(_quizAvoidRecentCheckBox);

        _quizRecentQuizCountTextBox = new TextBox
        {
            Text = "5",
            MinWidth = 42,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Number of most recent exported quizzes to avoid. Choose 1 to 50.",
        };
        Grid.SetColumn(_quizRecentQuizCountTextBox, 4);
        rotation.Children.Add(_quizRecentQuizCountTextBox);

        var suffix = new TextBlock
        {
            Text = "quizzes",
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(suffix, 5);
        rotation.Children.Add(suffix);
    }

    private void PickSmartQuizQuestions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_quizQuestionCountTextBox is null || _quizSecondsPerQuestionTextBox is null || _quizDraftGrid is null)
                return;
            if (!int.TryParse(_quizQuestionCountTextBox.Text.Trim(), out var count) || count is < 1 or > 100)
                throw new ArgumentException("Question count must be a whole number from 1 to 100.");
            if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
                throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");

            var preferLeastUsed = _quizPreferLeastUsedCheckBox?.IsChecked == true;
            var avoidRecent = _quizAvoidRecentCheckBox?.IsChecked == true;
            var recentQuizCount = 0;
            if (avoidRecent)
            {
                if (_quizRecentQuizCountTextBox is null ||
                    !int.TryParse(_quizRecentQuizCountTextBox.Text.Trim(), out recentQuizCount) ||
                    recentQuizCount is < 1 or > 50)
                {
                    throw new ArgumentException("Recent quiz avoidance must be a whole number from 1 to 50.");
                }
            }

            var matching = _data.GetQuizQuestions(
                category: SelectedQuizCategory(),
                difficulty: SelectedQuizDifficulty(),
                limit: 10_000,
                enabledOnly: true);
            var recentIds = avoidRecent
                ? _data.GetRecentQuizQuestionIds(recentQuizCount)
                : new HashSet<int>();

            _quizDraftQuestions = QuizRotationSelector.Select(
                    matching,
                    count,
                    preferLeastUsed,
                    recentIds)
                .ToList();
            _quizSecondsPerQuestion = seconds;
            RefreshQuizDraftEditorGrid(_quizDraftQuestions.FirstOrDefault()?.Id);

            var recentFallbacks = QuizRotationSelector.CountRecentFallbacks(_quizDraftQuestions, recentIds);
            var selectionMode = preferLeastUsed ? "least-used first" : "random";
            var recentStatus = avoidRecent
                ? recentFallbacks == 0
                    ? $"avoided last {recentQuizCount} exported quizzes"
                    : $"avoided last {recentQuizCount} quizzes where possible; reused {recentFallbacks} recent question{(recentFallbacks == 1 ? "" : "s")} because the fresh pool was too small"
                : "recent-use avoidance off";

            if (_quizDraftStatusText is not null)
            {
                var thinkingSeconds = count * seconds;
                _quizDraftStatusText.Text =
                    $"{count} enabled questions • {selectionMode} • {recentStatus} • {seconds} sec/question • {thinkingSeconds / 60}:{thinkingSeconds % 60:00} answer time.";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Quiz draft built with rotation rules";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Quiz Draft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
