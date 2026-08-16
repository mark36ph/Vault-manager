using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizDraftEditorInitialized;
    private TextBlock? _quizDraftSelectionText;
    private CheckBox? _quizShuffleAnswersCheckBox;

    private void InitializeQuizDraftEditor()
    {
        if (_quizDraftEditorInitialized || _quizDraftGrid?.Parent is not Grid draft)
            return;

        _quizDraftEditorInitialized = true;
        _quizDraftGrid.SelectionMode = DataGridSelectionMode.Single;
        _quizDraftGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _quizDraftGrid.AlternationCount = 2;
        _quizDraftGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        _quizDraftGrid.SelectionChanged += (_, _) => UpdateQuizDraftSelectionDetails();

        var actionRow = draft.RowDefinitions.Count;
        draft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(panel, actionRow);
        draft.Children.Add(panel);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Child = layout;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(DraftButton("Add from bank", AddQuestionToDraft_Click));
        buttons.Children.Add(DraftButton("Replace", ReplaceDraftQuestion_Click));
        buttons.Children.Add(DraftButton("Remove", RemoveDraftQuestion_Click));
        buttons.Children.Add(DraftButton("Move up", (_, _) => MoveSelectedDraftQuestion(-1)));
        buttons.Children.Add(DraftButton("Move down", (_, _) => MoveSelectedDraftQuestion(1), last: true));
        layout.Children.Add(buttons);

        var info = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(info, 1);
        layout.Children.Add(info);

        _quizDraftSelectionText = new TextBlock
        {
            Text = "Select a draft question to see its category, difficulty, bank number, and usage.",
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        info.Children.Add(_quizDraftSelectionText);

        _quizShuffleAnswersCheckBox = new CheckBox
        {
            Content = "Shuffle A/B/C/D on export",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            ToolTip = "Shuffles answer positions only in the exported quiz. The stored question bank is not changed.",
        };
        Grid.SetColumn(_quizShuffleAnswersCheckBox, 1);
        info.Children.Add(_quizShuffleAnswersCheckBox);

        UpdateQuizDraftSelectionDetails();
    }

    private static Button DraftButton(string text, RoutedEventHandler handler, bool last = false)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            Padding = new Thickness(11, 0, 11, 0),
            Margin = last ? new Thickness(0) : new Thickness(0, 0, 7, 0),
        };
        button.Click += handler;
        return button;
    }

    private QuizQuestion? SelectedDraftQuestion()
    {
        if (_quizDraftGrid?.SelectedItem is not QuizDraftDisplayRow row)
            return null;
        var index = row.Number - 1;
        return index >= 0 && index < _quizDraftQuestions.Count
            ? _quizDraftQuestions[index]
            : null;
    }

    private void UpdateQuizDraftSelectionDetails()
    {
        if (_quizDraftSelectionText is null)
            return;

        var question = SelectedDraftQuestion();
        if (question is null)
        {
            _quizDraftSelectionText.Text = _quizDraftQuestions.Count == 0
                ? "The quiz draft is empty. Pick random questions or add one from the bank."
                : "Select a draft question to see its category, difficulty, bank number, and usage.";
            return;
        }

        var position = _quizDraftQuestions.FindIndex(item => item.Id == question.Id) + 1;
        _quizDraftSelectionText.Text =
            $"Selected: draft #{position} • bank #{question.Id} • {question.Category} • {question.Difficulty} • used {question.TimesUsed:N0} time{(question.TimesUsed == 1 ? "" : "s")}";
    }

    private void MoveSelectedDraftQuestion(int offset)
    {
        var selected = SelectedDraftQuestion();
        if (selected is null)
        {
            ShowDraftSelectionRequired();
            return;
        }

        try
        {
            _quizDraftQuestions = QuizDraftOperations.Move(_quizDraftQuestions, selected.Id, offset).ToList();
            RefreshQuizDraftEditorGrid(selected.Id, "Quiz draft order updated.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Reorder Quiz Draft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveDraftQuestion_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDraftQuestion();
        if (selected is null)
        {
            ShowDraftSelectionRequired();
            return;
        }

        try
        {
            var oldIndex = _quizDraftQuestions.FindIndex(question => question.Id == selected.Id);
            _quizDraftQuestions = QuizDraftOperations.Remove(_quizDraftQuestions, selected.Id).ToList();
            var nextId = _quizDraftQuestions.Count == 0
                ? (int?)null
                : _quizDraftQuestions[Math.Min(oldIndex, _quizDraftQuestions.Count - 1)].Id;
            RefreshQuizDraftEditorGrid(nextId, $"Removed bank question #{selected.Id} from this quiz draft.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Remove Draft Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReplaceDraftQuestion_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDraftQuestion();
        if (selected is null)
        {
            ShowDraftSelectionRequired();
            return;
        }

        try
        {
            var usedIds = _quizDraftQuestions.Select(question => question.Id).ToHashSet();
            var candidates = _data.GetQuizQuestions(
                    category: SelectedQuizCategory(),
                    difficulty: SelectedQuizDifficulty(),
                    limit: 10_000,
                    enabledOnly: true)
                .Where(question => !usedIds.Contains(question.Id))
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("No unused enabled questions match the current Category and Difficulty filters. Change the filters or use Add from bank.");

            var replacement = candidates[Random.Shared.Next(candidates.Count)];
            _quizDraftQuestions = QuizDraftOperations.Replace(_quizDraftQuestions, selected.Id, replacement).ToList();
            RefreshQuizDraftEditorGrid(replacement.Id, $"Replaced bank question #{selected.Id} with #{replacement.Id}.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Replace Draft Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddQuestionToDraft_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_quizDraftQuestions.Count >= QuizDraftOperations.MaximumQuestions)
                throw new InvalidOperationException($"A quiz draft can contain at most {QuizDraftOperations.MaximumQuestions} questions.");

            var question = ShowQuizQuestionPicker();
            if (question is null)
                return;

            _quizDraftQuestions = QuizDraftOperations.Add(_quizDraftQuestions, question).ToList();
            RefreshQuizDraftEditorGrid(question.Id, $"Added bank question #{question.Id} to this quiz draft.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Add Quiz Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private QuizQuestion? ShowQuizQuestionPicker()
    {
        var existingIds = _quizDraftQuestions.Select(question => question.Id).ToHashSet();
        var candidates = _data.GetQuizQuestions(
                category: SelectedQuizCategory(),
                difficulty: SelectedQuizDifficulty(),
                limit: 10_000,
                enabledOnly: true)
            .Where(question => !existingIds.Contains(question.Id))
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("There are no unused enabled questions matching the current Category and Difficulty filters.");

        var dialog = new Window
        {
            Title = "Add Question from Bank",
            Owner = this,
            Width = 960,
            Height = 640,
            MinWidth = 720,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        root.Children.Add(new TextBlock
        {
            Text = "Choose an enabled question to add to the current quiz. Current Quiz Builder category/difficulty filters are applied.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var search = new TextBox
        {
            ToolTip = "Search question, category, difficulty, or answer",
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(search, 1);
        root.Children.Add(search);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            AlternationCount = 2,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "No.",
            Binding = new Binding(nameof(QuizQuestion.Id)),
            SortMemberPath = nameof(QuizQuestion.Id),
            Width = new DataGridLength(62),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Category",
            Binding = new Binding(nameof(QuizQuestion.Category)),
            SortMemberPath = nameof(QuizQuestion.Category),
            Width = new DataGridLength(130),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(QuizQuestion.Question)),
            SortMemberPath = nameof(QuizQuestion.Question),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Level",
            Binding = new Binding(nameof(QuizQuestion.Difficulty)),
            SortMemberPath = nameof(QuizQuestion.Difficulty),
            Width = new DataGridLength(85),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Times used",
            Binding = new Binding(nameof(QuizQuestion.TimesUsed)),
            SortMemberPath = nameof(QuizQuestion.TimesUsed),
            Width = new DataGridLength(90),
        });
        Grid.SetRow(grid, 2);
        root.Children.Add(grid);

        void ApplySearch()
        {
            var term = search.Text.Trim();
            grid.ItemsSource = term.Length == 0
                ? candidates
                : candidates.Where(question =>
                    question.Question.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    question.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    question.Difficulty.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    question.Answers.Any(answer => answer.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
        }

        QuizQuestion? chosen = null;
        void Accept()
        {
            if (grid.SelectedItem is not QuizQuestion selected)
                return;
            chosen = selected;
            dialog.DialogResult = true;
        }

        search.TextChanged += (_, _) => ApplySearch();
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                Accept();
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        actions.Children.Add(cancel);
        var add = new Button
        {
            Content = "Add selected",
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };
        add.Click += (_, _) => Accept();
        actions.Children.Add(add);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        ApplySearch();
        if (candidates.Count > 0)
            grid.SelectedIndex = 0;
        search.Focus();

        return dialog.ShowDialog() == true ? chosen : null;
    }

    private void RefreshQuizDraftEditorGrid(int? selectedQuestionId = null, string? status = null)
    {
        if (_quizDraftGrid is null)
            return;

        _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
        if (selectedQuestionId is int id)
        {
            var index = _quizDraftQuestions.FindIndex(question => question.Id == id);
            if (index >= 0)
            {
                _quizDraftGrid.SelectedIndex = index;
                _quizDraftGrid.ScrollIntoView(_quizDraftGrid.SelectedItem);
            }
        }

        if (!string.IsNullOrWhiteSpace(status) && _quizDraftStatusText is not null)
            _quizDraftStatusText.Text = $"{_quizDraftQuestions.Count} questions • {status}";
        UpdateQuizDraftSelectionDetails();
    }

    private void RefreshQuizDraftUsageCounts()
    {
        if (_quizDraftQuestions.Count == 0)
            return;

        var latest = _data.GetQuizQuestions(limit: 10_000)
            .ToDictionary(question => question.Id);
        var selectedId = SelectedDraftQuestion()?.Id;
        _quizDraftQuestions = _quizDraftQuestions
            .Select(question => latest.TryGetValue(question.Id, out var current) ? current : question)
            .ToList();
        RefreshQuizDraftEditorGrid(selectedId);
    }

    private void ShowDraftSelectionRequired() =>
        MessageBox.Show(
            this,
            "Select a question in the Quiz Draft first.",
            "Quiz Draft",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
