using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizBatchModesHookTag = "QuizBatchModesHooked";
    private static readonly bool QuizBatchCategoryModeRegistered = RegisterQuizBatchCategoryMode();

    private sealed record QuizBatchRenderPlan(
        IReadOnlyList<string?> Categories,
        bool OnePerCategory)
    {
        public int Count => Categories.Count;
    }

    private static bool RegisterQuizBatchCategoryMode()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizBatchModeButton_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizBatchModeButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Batch Render...", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window ||
            string.Equals(button.Tag?.ToString(), QuizBatchModesHookTag, StringComparison.Ordinal))
        {
            return;
        }

        button.Click -= window.BatchRenderQuizVideos_Click;
        button.Click += window.BatchRenderQuizVideosWithModes_Click;
        button.Tag = QuizBatchModesHookTag;
        button.ToolTip = "Render several fresh quizzes from the current category, or automatically render one quiz from every category with enabled questions.";
    }

    private async void BatchRenderQuizVideosWithModes_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBatchRenderRunning)
            return;

        var plan = AskQuizBatchRenderPlan();
        if (plan is null || plan.Count == 0)
            return;

        var batchButton = sender as Button;
        var progressWindow = CreateQuizBatchProgressWindow(
            plan.Count,
            out var progressText,
            out var progressBar,
            out var detailText);
        var completed = new List<QuizBatchRenderItemResult>();
        var failed = new List<(int Number, string Error)>();
        var originalCategory = _quizCategoryComboBox?.SelectedItem;
        var originalTitle = _quizTitleTextBox?.Text ?? "";

        _quizBatchRenderRunning = true;
        if (batchButton is not null)
            batchButton.IsEnabled = false;

        progressWindow.Show();
        try
        {
            for (var index = 0; index < plan.Count; index++)
            {
                var number = index + 1;
                var category = plan.Categories[index];
                var categoryPrefix = string.IsNullOrWhiteSpace(category) ? "" : category + " • ";
                progressText.Text = $"{categoryPrefix}Quiz {number:N0} of {plan.Count:N0}";
                progressBar.Value = index * 100.0 / plan.Count;
                detailText.Text = string.IsNullOrWhiteSpace(category)
                    ? "Selecting fresh questions with rotation rules..."
                    : $"Switching to {category} and selecting fresh questions with rotation rules...";

                if (!string.IsNullOrWhiteSpace(category))
                {
                    SelectQuizBatchCategory(category);
                    if (_quizTitleTextBox is not null)
                        _quizTitleTextBox.Text = category;
                }

                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var result = await RenderOneBatchQuizAsync(
                        number,
                        plan.Count,
                        stage =>
                        {
                            detailText.Text = string.IsNullOrWhiteSpace(category)
                                ? stage
                                : $"{category}: {stage}";
                            if (_quizPageStatusText is not null)
                            {
                                _quizPageStatusText.Text = string.IsNullOrWhiteSpace(category)
                                    ? $"Batch {number}/{plan.Count}: {stage}"
                                    : $"Batch {number}/{plan.Count} • {category}: {stage}";
                            }
                        });
                    completed.Add(result);
                    progressBar.Value = number * 100.0 / plan.Count;
                }
                catch (Exception error)
                {
                    var errorText = string.IsNullOrWhiteSpace(category)
                        ? error.Message
                        : $"{category}: {error.Message}";
                    failed.Add((number, errorText));
                    detailText.Text = string.IsNullOrWhiteSpace(category)
                        ? $"Quiz {number:N0} failed; continuing with the next item..."
                        : $"{category} failed; continuing with the next category...";
                    progressBar.Value = number * 100.0 / plan.Count;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            if (plan.OnePerCategory)
            {
                if (_quizCategoryComboBox is not null)
                    _quizCategoryComboBox.SelectedItem = originalCategory;
                if (_quizTitleTextBox is not null)
                    _quizTitleTextBox.Text = originalTitle;
            }

            _quizBatchRenderRunning = false;
            if (batchButton is not null)
                batchButton.IsEnabled = true;
            progressWindow.Close();
        }

        RefreshQuizBank();
        RefreshQuizDraftUsageCounts();
        RefreshQuizHistory();
        RefreshQuizPreview();
        RefreshQuizPublishingSeries();

        if (_quizPageStatusText is not null)
        {
            var prefix = plan.OnePerCategory ? "Category batch complete" : "Batch complete";
            _quizPageStatusText.Text = failed.Count == 0
                ? $"{prefix}: {completed.Count:N0} final video{(completed.Count == 1 ? "" : "s")} rendered"
                : $"{prefix}: {completed.Count:N0} rendered, {failed.Count:N0} failed";
        }

        ShowQuizBatchSummary(plan.Count, completed, failed);
    }

    private QuizBatchRenderPlan? AskQuizBatchRenderPlan()
    {
        var currentCategory = SelectedQuizCategory();
        var currentCategoryDisplay = string.IsNullOrWhiteSpace(currentCategory)
            ? "All categories"
            : currentCategory;
        var categories = _data.GetQuizCategorySummaries()
            .Where(summary => summary.EnabledCount > 0)
            .Select(summary => summary.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var repeatCurrent = new RadioButton
        {
            Content = $"Repeat current category ({currentCategoryDisplay})",
            IsChecked = true,
            GroupName = "QuizBatchMode",
            FontWeight = FontWeights.SemiBold,
        };
        var onePerCategory = new RadioButton
        {
            Content = $"One quiz from each category ({categories.Length:N0} categories)",
            GroupName = "QuizBatchMode",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var countBox = new TextBox
        {
            Text = "5",
            MinWidth = 90,
            Width = 90,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(22, 8, 0, 0),
        };
        var countHint = new TextBlock
        {
            Text = "Number of quizzes: 2–20",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(22, 5, 0, 0),
        };
        var categoryHint = new TextBlock
        {
            Text = "Uses every category that currently has enabled questions. The category and project title switch automatically for each quiz. Your current question count, difficulty, rotation rules and Export settings are reused.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 7, 0, 0),
        };

        void UpdateModeState()
        {
            var repeating = repeatCurrent.IsChecked == true;
            countBox.IsEnabled = repeating;
            countHint.IsEnabled = repeating;
        }

        repeatCurrent.Checked += (_, _) => UpdateModeState();
        onePerCategory.Checked += (_, _) => UpdateModeState();

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose how this batch should be built",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });
        panel.Children.Add(repeatCurrent);
        panel.Children.Add(countBox);
        panel.Children.Add(countHint);
        panel.Children.Add(onePerCategory);
        panel.Children.Add(categoryHint);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var start = new Button { Content = "Start batch", MinWidth = 110, Height = 34, FontWeight = FontWeights.SemiBold };
        actions.Children.Add(cancel);
        actions.Children.Add(start);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Batch Render Final Videos",
            Width = 570,
            Height = 390,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) =>
        {
            if (onePerCategory.IsChecked == true)
            {
                if (QuizMarathonPlanner.IsMarathonPreset(_quizModeComboBox?.SelectedItem as QuizBuilderModePreset))
                {
                    MessageBox.Show(
                        dialog,
                        "One-per-category batch rendering uses the regular quiz modes. Switch VIDEO TYPE out of Marathon Mode first.",
                        "Batch Render",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                if (categories.Length == 0)
                {
                    MessageBox.Show(
                        dialog,
                        "There are no categories with enabled questions to render.",
                        "Batch Render",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                dialog.Tag = new QuizBatchRenderPlan(
                    categories.Select(category => (string?)category).ToArray(),
                    OnePerCategory: true);
                dialog.DialogResult = true;
                return;
            }

            if (!int.TryParse(countBox.Text.Trim(), out var count) || count is < 2 or > 20)
            {
                MessageBox.Show(dialog, "Enter a batch size from 2 to 20.", "Batch Render", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dialog.Tag = new QuizBatchRenderPlan(
                Enumerable.Repeat<string?>(null, count).ToArray(),
                OnePerCategory: false);
            dialog.DialogResult = true;
        };

        UpdateModeState();
        if (dialog.ShowDialog() != true || dialog.Tag is not QuizBatchRenderPlan result)
            return null;
        return result;
    }

    private void SelectQuizBatchCategory(string category)
    {
        if (_quizCategoryComboBox is null)
            throw new InvalidOperationException("Quiz category control is not ready.");

        var item = _quizCategoryComboBox.Items
            .Cast<object>()
            .FirstOrDefault(value => string.Equals(
                Convert.ToString(value)?.Trim(),
                category,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
            throw new InvalidOperationException($"Quiz category '{category}' is no longer available.");

        _quizCategoryComboBox.SelectedItem = item;
    }
}
