using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ConfigureQuizBulkActions(StackPanel bankActions)
    {
        if (_quizBankGrid is null)
            return;

        _quizBankGrid.SelectionMode = DataGridSelectionMode.Extended;
        _quizBankGrid.SelectionUnit = DataGridSelectionUnit.FullRow;

        if (bankActions.Children.OfType<Button>().Any(button =>
                string.Equals(button.Tag?.ToString(), "QuizBulkAction", StringComparison.Ordinal)))
            return;

        var legacyButtons = bankActions.Children
            .OfType<Button>()
            .Where(button =>
            {
                var text = button.Content?.ToString() ?? "";
                return string.Equals(text, "Enable / disable selected", StringComparison.Ordinal) ||
                       string.Equals(text, "Delete selected", StringComparison.Ordinal) ||
                       string.Equals(text, "Enable / disable", StringComparison.Ordinal) ||
                       string.Equals(text, "Delete", StringComparison.Ordinal);
            })
            .ToArray();
        foreach (var button in legacyButtons)
            bankActions.Children.Remove(button);

        var selectAll = new Button
        {
            Content = "Select all",
            Tag = "QuizBulkAction",
            ToolTip = "Select every question currently visible in the list.",
            Margin = new Thickness(0, 0, 8, 0),
        };
        selectAll.Click += (_, _) => _quizBankGrid.SelectAll();
        bankActions.Children.Add(selectAll);

        var enable = new Button
        {
            Content = "Enable",
            Tag = "QuizBulkAction",
            ToolTip = "Enable all selected questions for future random quiz selection.",
            Margin = new Thickness(0, 0, 8, 0),
        };
        enable.Click += (_, _) => SetSelectedQuizQuestionsEnabled(true);
        bankActions.Children.Add(enable);

        var disable = new Button
        {
            Content = "Disable",
            Tag = "QuizBulkAction",
            ToolTip = "Keep selected questions in the bank but exclude them from future random quiz selection.",
            Margin = new Thickness(0, 0, 8, 0),
        };
        disable.Click += (_, _) => SetSelectedQuizQuestionsEnabled(false);
        bankActions.Children.Add(disable);

        var findIcons = new Button
        {
            Content = "Find icon images",
            Tag = "QuizBulkAction",
            ToolTip = "Download matching Simple Icons images for selected Icons questions that do not have an image.",
            Margin = new Thickness(0, 0, 8, 0),
        };
        findIcons.Click += FindSelectedQuizQuestionIcons_Click;
        bankActions.Children.Add(findIcons);

        var category = new Button
        {
            Content = "Change category",
            Tag = "QuizBulkAction",
            ToolTip = "Move all selected questions into one category.",
            Margin = new Thickness(0, 0, 8, 0),
        };
        category.Click += SetSelectedQuizQuestionsCategory_Click;
        bankActions.Children.Add(category);

        var delete = new Button
        {
            Content = "Delete",
            Tag = "QuizBulkAction",
            ToolTip = "Permanently delete all selected questions from the question bank.",
        };
        delete.Click += DeleteSelectedQuizQuestionsBulk_Click;
        bankActions.Children.Add(delete);
    }

    private async void FindSelectedQuizQuestionIcons_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQuizQuestionsBulk();
        var questions = selected
            .Where(question => QuizTypeCatalog.FromCategory(question.Category) == QuizTypeCatalog.Logo)
            .Where(question => !question.HasImage)
            .ToList();
        if (questions.Count == 0)
        {
            MessageBox.Show(
                this,
                "Select one or more Icons questions that do not yet have an image.",
                "Find Icon Images",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var colourMode = ChooseSimpleIconColourMode(questions.Count);
        if (colourMode is null)
            return;

        var button = sender as Button;
        if (button is not null)
            button.IsEnabled = false;
        var displayOrder = CurrentQuizQuestionDisplayOrder();
        var selectedIds = selected.Select(question => question.Id).ToArray();
        var downloaded = 0;
        var missing = new List<string>();
        var failed = new List<string>();

        try
        {
            foreach (var question in questions)
            {
                if (_quizPageStatusText is not null)
                    _quizPageStatusText.Text = $"Finding icon {downloaded + missing.Count + failed.Count + 1:N0}/{questions.Count:N0}: {question.CorrectAnswer}";
                try
                {
                    var result = await SimpleIconsService.DownloadPngAsync(question.CorrectAnswer, colourMode.Value);
                    if (!result.Found)
                    {
                        missing.Add(question.CorrectAnswer);
                        continue;
                    }

                    _data.UpdateQuizQuestion(question.Id, new QuizQuestionEditRequest(
                        question.Question,
                        question.OptionA,
                        question.OptionB,
                        question.OptionC,
                        question.OptionD,
                        question.CorrectIndex,
                        question.Explanation,
                        question.Category,
                        question.Difficulty,
                        question.IsEnabled,
                        result.ImagePath));
                    downloaded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{question.CorrectAnswer}: {error.Message}");
                }
            }

            RefreshQuizBank();
            RestoreEditedQuizQuestionDisplayOrder(displayOrder);
            RefreshQuizCategorySection();
            RestoreQuizBulkSelection(selectedIds);

            var summary = $"Attached {downloaded:N0} icon image{(downloaded == 1 ? "" : "s")}.";
            if (missing.Count > 0)
                summary += $"\n\nNot available from Simple Icons ({missing.Count:N0}):\n" + string.Join("\n", missing.Distinct(StringComparer.OrdinalIgnoreCase).Take(15));
            if (failed.Count > 0)
                summary += $"\n\nCould not download ({failed.Count:N0}):\n" + string.Join("\n", failed.Take(8));
            if (missing.Count > 0 || failed.Count > 0)
                summary += "\n\nUse Edit → Browse to attach those images manually.";

            MessageBox.Show(
                this,
                summary,
                "Find Icon Images",
                MessageBoxButton.OK,
                missing.Count == 0 && failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Attached {downloaded:N0} Simple Icons image{(downloaded == 1 ? "" : "s")}";
        }
        finally
        {
            if (button is not null)
                button.IsEnabled = true;
        }
    }

    private SimpleIconColourMode? ChooseSimpleIconColourMode(int questionCount)
    {
        var choice = new ComboBox { MinHeight = 34, MinWidth = 240, SelectedIndex = 0 };
        choice.Items.Add("Brand colours");
        choice.Items.Add("Black");

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Choose the colour style for {questionCount:N0} icon image{(questionCount == 1 ? "" : "s")}:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(choice);
        panel.Children.Add(new TextBlock
        {
            Text = "Brand colours are recommended. Questions without an exact Simple Icons match will be left for manual selection.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var download = new Button { Content = "Find images", MinWidth = 110, Height = 34, FontWeight = FontWeights.SemiBold };
        actions.Children.Add(cancel);
        actions.Children.Add(download);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Find Icon Images",
            Width = 460,
            Height = 245,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };
        cancel.Click += (_, _) => dialog.Close();
        download.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true)
            return null;
        return choice.SelectedIndex == 1 ? SimpleIconColourMode.Black : SimpleIconColourMode.Brand;
    }

    private IReadOnlyList<QuizQuestion> SelectedQuizQuestionsBulk()
    {
        if (_quizBankGrid is null)
            return [];

        return _quizBankGrid.SelectedItems
            .OfType<QuizQuestion>()
            .GroupBy(question => question.Id)
            .Select(group => group.First())
            .ToList();
    }

    private int[] CurrentQuizQuestionDisplayOrder() =>
        _quizBankGrid?.Items
            .OfType<QuizQuestion>()
            .Select(question => question.Id)
            .ToArray() ?? [];

    private void SetSelectedQuizQuestionsEnabled(bool enabled)
    {
        var selected = SelectedQuizQuestionsBulk();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select one or more questions first.", "Question Bank", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var displayOrder = CurrentQuizQuestionDisplayOrder();
            var selectedIds = selected.Select(question => question.Id).ToArray();
            foreach (var id in selectedIds)
                _data.SetQuizQuestionEnabled(id, enabled);

            if (!enabled)
                RemoveQuizQuestionsFromDraft(selectedIds, "Disabled selected questions were removed from the current draft.");

            RefreshQuizBank();
            RestoreEditedQuizQuestionDisplayOrder(displayOrder);
            RefreshQuizCategorySection();
            RestoreQuizBulkSelection(selectedIds);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"{selected.Count:N0} question{(selected.Count == 1 ? "" : "s")} {(enabled ? "enabled" : "disabled")}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Question Bank", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetSelectedQuizQuestionsCategory_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQuizQuestionsBulk();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select one or more questions first.", "Question Bank", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var categoryBox = new ComboBox
        {
            MinHeight = 34,
            MinWidth = 280,
            IsEditable = true,
            IsTextSearchEnabled = true,
        };
        foreach (var category in QuizQuestionTopicCategorizer.Categories
                     .Concat(_data.GetQuizCategories())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            categoryBox.Items.Add(category);
        }
        categoryBox.SelectedItem = selected
            .Select(question => question.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1
                ? selected[0].Category
                : null;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Move {selected.Count:N0} selected question{(selected.Count == 1 ? "" : "s")} to:",
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(categoryBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Move questions", MinWidth = 120, Height = 34, FontWeight = FontWeights.SemiBold };
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Set Question Category",
            Width = 420,
            Height = 190,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        cancel.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) =>
        {
            var category = categoryBox.Text.Trim();
            if (category.Length == 0)
            {
                MessageBox.Show(dialog, "Choose or enter a category first.", "Set Question Category", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var category = categoryBox.Text.Trim();
            var displayOrder = CurrentQuizQuestionDisplayOrder();
            var selectedIds = selected.Select(question => question.Id).ToArray();
            foreach (var id in selectedIds)
                _data.SetQuizQuestionCategory(id, category);

            RefreshQuizBank();
            RestoreEditedQuizQuestionDisplayOrder(displayOrder);
            RefreshQuizCategorySection();
            RestoreQuizBulkSelection(selectedIds);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Moved {selected.Count:N0} question{(selected.Count == 1 ? "" : "s")} to {category}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Set Question Category", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedQuizQuestionsBulk_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQuizQuestionsBulk();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select one or more questions first.", "Question Bank", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preview = string.Join("\n", selected.Take(3).Select(question => $"• #{question.Id} {question.Question}"));
        if (selected.Count > 3)
            preview += $"\n• …and {selected.Count - 3:N0} more";

        var answer = MessageBox.Show(
            this,
            $"Permanently delete {selected.Count:N0} selected question{(selected.Count == 1 ? "" : "s")} from the reusable bank?\n\n{preview}",
            "Delete Quiz Questions",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var displayOrder = CurrentQuizQuestionDisplayOrder();
            var selectedIds = selected.Select(question => question.Id).ToArray();
            foreach (var id in selectedIds)
                _data.DeleteQuizQuestion(id);

            RemoveQuizQuestionsFromDraft(selectedIds, "Deleted questions were removed from the current draft.");
            RefreshQuizBank();
            RestoreEditedQuizQuestionDisplayOrder(displayOrder);
            RefreshQuizCategorySection();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Deleted {selected.Count:N0} question{(selected.Count == 1 ? "" : "s")}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Quiz Questions", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreQuizBulkSelection(IEnumerable<int> questionIds)
    {
        if (_quizBankGrid is null)
            return;

        var wanted = questionIds.ToHashSet();
        var visible = _quizBankGrid.Items
            .OfType<QuizQuestion>()
            .Where(question => wanted.Contains(question.Id))
            .ToList();
        _quizBankGrid.SelectedItems.Clear();
        foreach (var question in visible)
            _quizBankGrid.SelectedItems.Add(question);
        if (visible.Count > 0)
        {
            _quizBankGrid.ScrollIntoView(visible[0]);
            UpdateQuizQuestionDetails(visible[0]);
        }
    }

    private void RemoveQuizQuestionsFromDraft(IEnumerable<int> questionIds, string statusMessage)
    {
        var ids = questionIds.ToHashSet();
        if (!ids.Overlaps(_quizDraftQuestions.Select(question => question.Id)))
            return;

        _quizDraftQuestions = _quizDraftQuestions.Where(question => !ids.Contains(question.Id)).ToList();
        if (_quizDraftGrid is not null)
            _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
        if (_quizDraftStatusText is not null)
            _quizDraftStatusText.Text = statusMessage + " Pick random questions again to refill the draft.";
    }
}
