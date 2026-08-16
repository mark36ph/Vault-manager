using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ShowEditQuizQuestionDialog(QuizQuestion question)
    {
        var previousDisplayOrder = _quizBankGrid?.Items
            .OfType<QuizQuestion>()
            .Select(item => item.Id)
            .ToArray() ?? [];

        var questionBox = EditQuizTextBox(question.Question, multiline: true);
        var answerBoxes = new[]
        {
            EditQuizTextBox(question.OptionA),
            EditQuizTextBox(question.OptionB),
            EditQuizTextBox(question.OptionC),
            EditQuizTextBox(question.OptionD),
        };
        var correctAnswer = new ComboBox { MinHeight = 34 };
        foreach (var letter in new[] { "A", "B", "C", "D" })
            correctAnswer.Items.Add(letter);
        correctAnswer.SelectedIndex = Math.Clamp(question.CorrectIndex, 0, 3);

        var category = new ComboBox
        {
            MinHeight = 34,
            IsEditable = true,
            IsTextSearchEnabled = true,
            Text = question.Category,
        };
        foreach (var item in QuizQuestionTopicCategorizer.Categories
                     .Concat(_data.GetQuizCategories())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            category.Items.Add(item);
        }

        var difficulty = new ComboBox { MinHeight = 34 };
        foreach (var value in new[] { "easy", "medium", "hard" })
            difficulty.Items.Add(value);
        difficulty.SelectedItem = new[] { "easy", "medium", "hard" }
            .FirstOrDefault(value => string.Equals(value, question.Difficulty, StringComparison.OrdinalIgnoreCase)) ?? "medium";

        var explanation = EditQuizTextBox(question.Explanation, multiline: true);
        var enabled = new CheckBox
        {
            Content = "Enabled for future random quiz selection",
            IsChecked = question.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var form = new Grid { Margin = new Thickness(22) };
        for (var row = 0; row < 9; row++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var meta = new TextBlock
        {
            Text = $"Question #{question.Id} • used {question.TimesUsed:N0} time{(question.TimesUsed == 1 ? "" : "s")} • source: {question.Source}",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetColumnSpan(meta, 3);
        form.Children.Add(meta);

        AddEditQuizField(form, 1, 0, 3, "QUESTION", questionBox);
        AddEditQuizField(form, 2, 0, 1, "ANSWER A", answerBoxes[0]);
        AddEditQuizField(form, 2, 2, 1, "ANSWER B", answerBoxes[1]);
        AddEditQuizField(form, 3, 0, 1, "ANSWER C", answerBoxes[2]);
        AddEditQuizField(form, 3, 2, 1, "ANSWER D", answerBoxes[3]);
        AddEditQuizField(form, 4, 0, 1, "CORRECT ANSWER", correctAnswer);
        AddEditQuizField(form, 4, 2, 1, "DIFFICULTY", difficulty);
        AddEditQuizField(form, 5, 0, 3, "CATEGORY", category);
        AddEditQuizField(form, 6, 0, 3, "EXPLANATION", explanation);

        Grid.SetRow(enabled, 7);
        Grid.SetColumnSpan(enabled, 3);
        form.Children.Add(enabled);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Height = 36,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var save = new Button
        {
            Content = "Save changes",
            Height = 36,
            MinWidth = 120,
            FontWeight = FontWeights.SemiBold,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 8);
        Grid.SetColumnSpan(buttons, 3);
        form.Children.Add(buttons);

        var dialog = new Window
        {
            Owner = this,
            Title = $"Edit Question #{question.Id}",
            Width = 820,
            Height = 720,
            MinWidth = 650,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = form,
            },
        };

        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                var request = new QuizQuestionEditRequest(
                    questionBox.Text,
                    answerBoxes[0].Text,
                    answerBoxes[1].Text,
                    answerBoxes[2].Text,
                    answerBoxes[3].Text,
                    correctAnswer.SelectedIndex,
                    explanation.Text,
                    category.Text,
                    difficulty.SelectedItem?.ToString() ?? "medium",
                    enabled.IsChecked == true);

                var updated = _data.UpdateQuizQuestion(question.Id, request);
                dialog.DialogResult = true;
                RefreshQuizBank();
                RestoreEditedQuizQuestionDisplayOrder(previousDisplayOrder);
                RefreshQuizCategorySection();
                SyncEditedQuizQuestionWithDraft(updated);
                SelectEditedQuizQuestion(updated);
                if (_quizPageStatusText is not null)
                    _quizPageStatusText.Text = $"Question #{updated.Id} updated";
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Edit Quiz Question", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        questionBox.Focus();
        questionBox.SelectAll();
        dialog.ShowDialog();
    }

    private void RestoreEditedQuizQuestionDisplayOrder(IReadOnlyList<int> previousDisplayOrder)
    {
        if (_quizBankGrid?.ItemsSource is not IEnumerable<QuizQuestion> currentQuestions)
            return;

        _quizBankGrid.ItemsSource = QuizQuestionDisplayOrder.Preserve(currentQuestions, previousDisplayOrder);
    }

    private void SyncEditedQuizQuestionWithDraft(QuizQuestion updated)
    {
        var index = _quizDraftQuestions.FindIndex(item => item.Id == updated.Id);
        if (index < 0)
            return;

        if (updated.IsEnabled)
            _quizDraftQuestions[index] = updated;
        else
            _quizDraftQuestions.RemoveAt(index);

        if (_quizDraftGrid is not null)
            _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
        if (_quizDraftStatusText is not null && !updated.IsEnabled)
            _quizDraftStatusText.Text = "An edited question was disabled and removed from the current draft. Pick random questions again to refill the draft.";
    }

    private void SelectEditedQuizQuestion(QuizQuestion updated)
    {
        if (_quizBankGrid is null)
            return;

        var visible = _quizBankGrid.Items
            .OfType<QuizQuestion>()
            .FirstOrDefault(item => item.Id == updated.Id);
        if (visible is not null)
        {
            _quizBankGrid.SelectedItem = visible;
            _quizBankGrid.ScrollIntoView(visible);
            UpdateQuizQuestionDetails(visible);
        }
        else
        {
            UpdateQuizQuestionDetails(updated);
        }
    }

    private static TextBox EditQuizTextBox(string text, bool multiline = false) => new()
    {
        Text = text,
        MinHeight = multiline ? 76 : 34,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
    };

    private static void AddEditQuizField(Grid form, int row, int column, int columnSpan, string label, FrameworkElement control)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(control);
        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, column);
        Grid.SetColumnSpan(stack, columnSpan);
        form.Children.Add(stack);
    }
}
