using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private TextBox? _manualQuizQuestionTextBox;
    private readonly TextBox[] _manualQuizAnswerTextBoxes = new TextBox[4];
    private ComboBox? _manualQuizCorrectAnswerComboBox;
    private TextBox? _manualQuizExplanationTextBox;
    private ComboBox? _manualQuizCategoryComboBox;
    private ComboBox? _manualQuizDifficultyComboBox;
    private bool _quizCategoryImportHooked;
    private bool _quizCategoryImportHadText;

    private void ConfigureStandaloneQuestionBank(Border bankCard)
    {
        bankCard.Padding = new Thickness(10);

        if (_quizBankGrid is not null)
        {
            _quizBankGrid.RowHeight = 30;
            _quizBankGrid.ColumnHeaderHeight = 34;
            _quizBankGrid.RowBackground = Brushes.White;
            _quizBankGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 248, 252));
            _quizBankGrid.AlternationCount = 2;
            _quizBankGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _quizBankGrid.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 234, 240));

            if (!_quizBankGrid.Columns.Any(column =>
                    string.Equals(column.SortMemberPath, nameof(QuizQuestion.Id), StringComparison.Ordinal)))
            {
                _quizBankGrid.Columns.Insert(0, new DataGridTextColumn
                {
                    Header = "No.",
                    Binding = new Binding(nameof(QuizQuestion.Id)),
                    SortMemberPath = nameof(QuizQuestion.Id),
                    Width = new DataGridLength(58),
                });
            }
        }

        if (_quizImportTextBox is not null && !_quizCategoryImportHooked)
        {
            _quizCategoryImportHooked = true;
            _quizCategoryImportHadText = !string.IsNullOrWhiteSpace(_quizImportTextBox.Text);
            _quizImportTextBox.TextChanged += QuizImportTextBox_AutoCategorizeTextChanged;
        }

        if (bankCard.Child is not Grid bank)
            return;

        var bankHeader = bank.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        var bankActions = bankHeader?.Children
            .OfType<StackPanel>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 1);
        if (bankActions is not null && !bankActions.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "Auto-categorize", StringComparison.Ordinal)))
        {
            var categorize = new Button
            {
                Content = "Auto-categorize",
                ToolTip = "Move questions currently filed as General Knowledge into topic categories.",
                Margin = new Thickness(0, 0, 8, 0),
            };
            categorize.Click += AutoCategorizeQuizQuestions_Click;
            bankActions.Children.Insert(0, categorize);
        }

        var tabs = bank.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs is null)
            return;
        _quizBankTabs ??= tabs;

        if (tabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "Add manually", StringComparison.OrdinalIgnoreCase)))
            return;

        var manualTab = new TabItem
        {
            Header = "Add manually",
            Content = BuildQuizManualQuestionPanel(),
        };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            manualTab.Style = sectionStyle;
        tabs.Items.Add(manualTab);
    }

    private FrameworkElement BuildQuizManualQuestionPanel()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var form = new Grid { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 8; i++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scroll.Content = form;

        _manualQuizQuestionTextBox = ManualQuizTextBox(multiline: true);
        AddManualQuizField(form, 0, 0, 3, "QUESTION", _manualQuizQuestionTextBox);

        for (var index = 0; index < 4; index++)
        {
            _manualQuizAnswerTextBoxes[index] = ManualQuizTextBox();
            var row = 1 + index / 2;
            var column = (index % 2) * 2;
            AddManualQuizField(form, row, column, 1, $"ANSWER {(char)('A' + index)}", _manualQuizAnswerTextBoxes[index]);
        }

        _manualQuizCorrectAnswerComboBox = new ComboBox { MinHeight = 34 };
        foreach (var value in new[] { "A", "B", "C", "D" })
            _manualQuizCorrectAnswerComboBox.Items.Add(value);
        _manualQuizCorrectAnswerComboBox.SelectedIndex = 0;
        AddManualQuizField(form, 3, 0, 1, "CORRECT ANSWER", _manualQuizCorrectAnswerComboBox);

        _manualQuizDifficultyComboBox = new ComboBox { MinHeight = 34 };
        foreach (var value in new[] { "easy", "medium", "hard" })
            _manualQuizDifficultyComboBox.Items.Add(value);
        _manualQuizDifficultyComboBox.SelectedIndex = 1;
        AddManualQuizField(form, 3, 2, 1, "DIFFICULTY", _manualQuizDifficultyComboBox);

        _manualQuizCategoryComboBox = new ComboBox
        {
            MinHeight = 34,
            IsEditable = true,
            IsTextSearchEnabled = true,
        };
        foreach (var category in QuizQuestionTopicCategorizer.Categories)
            _manualQuizCategoryComboBox.Items.Add(category);
        _manualQuizCategoryComboBox.SelectedItem = "Miscellaneous";
        AddManualQuizField(form, 4, 0, 3, "CATEGORY", _manualQuizCategoryComboBox);

        _manualQuizExplanationTextBox = ManualQuizTextBox(multiline: true);
        AddManualQuizField(form, 5, 0, 3, "EXPLANATION", _manualQuizExplanationTextBox);

        var help = new TextBlock
        {
            Text = "Choose a topic category or leave it blank to categorize automatically. All four answers must be different. New questions start enabled with Times used = 0.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        };
        Grid.SetRow(help, 6);
        Grid.SetColumnSpan(help, 3);
        form.Children.Add(help);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button
        {
            Content = "Add question",
            Height = 36,
            Padding = new Thickness(15, 0, 15, 0),
            FontWeight = FontWeights.SemiBold,
        };
        add.Click += AddManualQuizQuestion_Click;
        actions.Children.Add(add);

        var clear = new Button
        {
            Content = "Clear form",
            Height = 36,
            Padding = new Thickness(15, 0, 15, 0),
            Margin = new Thickness(8, 0, 0, 0),
        };
        clear.Click += (_, _) => ClearManualQuizQuestionForm();
        actions.Children.Add(clear);
        Grid.SetRow(actions, 7);
        Grid.SetColumnSpan(actions, 3);
        form.Children.Add(actions);

        return scroll;
    }

    private void AddManualQuizQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (_manualQuizQuestionTextBox is null ||
            _manualQuizCorrectAnswerComboBox is null ||
            _manualQuizExplanationTextBox is null ||
            _manualQuizCategoryComboBox is null ||
            _manualQuizDifficultyComboBox is null)
            return;

        try
        {
            var questionText = _manualQuizQuestionTextBox.Text.Trim();
            var answers = _manualQuizAnswerTextBoxes.Select(box => box.Text.Trim()).ToArray();
            var explanation = _manualQuizExplanationTextBox.Text.Trim();
            var category = _manualQuizCategoryComboBox.Text.Trim();
            if (category.Length == 0 || string.Equals(category, "General Knowledge", StringComparison.OrdinalIgnoreCase))
                category = QuizQuestionTopicCategorizer.Categorize(questionText, answers, explanation);

            var payload = JsonSerializer.Serialize(new
            {
                questions = new[]
                {
                    new
                    {
                        question = questionText,
                        answers,
                        correct_answer = _manualQuizCorrectAnswerComboBox.SelectedItem?.ToString() ?? "A",
                        explanation,
                        category,
                        difficulty = _manualQuizDifficultyComboBox.SelectedItem?.ToString() ?? "medium",
                    },
                },
            });

            var result = _data.ImportQuizQuestions(payload, "Manual entry");
            RefreshQuizBank();

            if (result.Inserted == 0)
            {
                MessageBox.Show(
                    this,
                    "That question is already in the question bank.",
                    "Add Quiz Question",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var addedQuestion = questionText;
            ClearManualQuizQuestionForm();
            if (_quizBankTabs is not null)
                _quizBankTabs.SelectedIndex = 0;
            if (_quizBankGrid is not null)
            {
                var match = _quizBankGrid.Items
                    .OfType<QuizQuestion>()
                    .FirstOrDefault(question => string.Equals(question.Question, addedQuestion, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    _quizBankGrid.SelectedItem = match;
                    _quizBankGrid.ScrollIntoView(match);
                }
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Manual quiz question added to {category}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Add Quiz Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AutoCategorizeQuizQuestions_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Move every question currently categorized as General Knowledge into a more specific topic category?\n\nExisting specific categories will not be changed.",
            "Auto-categorize Questions",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var result = _data.AutoCategorizeGeneralKnowledgeQuestions();
            RefreshQuizBank();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = result.Updated == 0
                    ? "No General Knowledge questions needed categorizing"
                    : $"Categorized {result.Updated:N0} questions";
            MessageBox.Show(
                this,
                result.Updated == 0
                    ? "There are no questions currently filed as General Knowledge."
                    : $"Categorized {result.Updated:N0} of {result.Found:N0} General Knowledge questions.",
                "Auto-categorize Questions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Auto-categorize Questions", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QuizImportTextBox_AutoCategorizeTextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(_quizImportTextBox?.Text);
        if (_quizCategoryImportHadText && !hasText)
        {
            try
            {
                _data.AutoCategorizeGeneralKnowledgeQuestions();
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"Could not auto-categorize imported quiz questions: {error.Message}");
            }
        }
        _quizCategoryImportHadText = hasText;
    }

    private void ClearManualQuizQuestionForm()
    {
        if (_manualQuizQuestionTextBox is not null)
            _manualQuizQuestionTextBox.Clear();
        foreach (var answer in _manualQuizAnswerTextBoxes)
            answer?.Clear();
        if (_manualQuizCorrectAnswerComboBox is not null)
            _manualQuizCorrectAnswerComboBox.SelectedIndex = 0;
        if (_manualQuizExplanationTextBox is not null)
            _manualQuizExplanationTextBox.Clear();
        if (_manualQuizCategoryComboBox is not null)
            _manualQuizCategoryComboBox.SelectedItem = "Miscellaneous";
        if (_manualQuizDifficultyComboBox is not null)
            _manualQuizDifficultyComboBox.SelectedIndex = 1;
        _manualQuizQuestionTextBox?.Focus();
    }

    private static TextBox ManualQuizTextBox(bool multiline = false) => new()
    {
        MinHeight = multiline ? 68 : 34,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
    };

    private static void AddManualQuizField(Grid form, int row, int column, int columnSpan, string label, FrameworkElement control)
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
