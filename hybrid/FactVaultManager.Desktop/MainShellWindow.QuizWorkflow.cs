using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizWorkflowInitialized;
    private bool _quizRefreshing;
    private int _quizTabIndex = -1;
    private TextBox? _quizQuestionCountTextBox;
    private TextBox? _quizSecondsPerQuestionTextBox;
    private TextBox? _quizSearchTextBox;
    private TextBox? _quizImportTextBox;
    private ComboBox? _quizCategoryComboBox;
    private ComboBox? _quizDifficultyComboBox;
    private DataGrid? _quizBankGrid;
    private DataGrid? _quizDraftGrid;
    private TextBlock? _quizBankStatusText;
    private TextBlock? _quizDraftStatusText;
    private TextBlock? _quizPageStatusText;
    private List<QuizQuestion> _quizDraftQuestions = new();
    private int _quizSecondsPerQuestion = 8;

    private void InitializeQuizWorkflow()
    {
        if (_quizWorkflowInitialized || MainTabs is null)
            return;

        _quizWorkflowInitialized = true;
        var tab = new TabItem { Content = BuildQuizPage() };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
            tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _quizTabIndex = MainTabs.Items.Count - 1;
        AddQuizNavigationButton(_quizTabIndex);
        RefreshQuizBank();
    }

    private FrameworkElement BuildQuizPage()
    {
        var root = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Quizzes",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Build a reusable question bank, pick random questions, and prepare quiz videos for Resolve.",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 3, 0, 0),
        });
        header.Children.Add(heading);
        _quizPageStatusText = new TextBlock
        {
            Text = "Question bank ready",
            Foreground = QuizMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_quizPageStatusText, 1);
        header.Children.Add(_quizPageStatusText);
        root.Children.Add(header);

        var settingsCard = QuizCard(new Thickness(14));
        settingsCard.Margin = new Thickness(0, 16, 0, 12);
        Grid.SetRow(settingsCard, 1);
        root.Children.Add(settingsCard);

        var settings = new Grid();
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        settingsCard.Child = settings;

        _quizQuestionCountTextBox = QuizTextField(settings, 0, "QUESTIONS", "10", "Number of random questions to use in a quiz");
        _quizSecondsPerQuestionTextBox = QuizTextField(settings, 2, "SECONDS / QUESTION", "8", "How long viewers get to answer each question");

        _quizCategoryComboBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MinHeight = 34 };
        var categoryStack = QuizLabeledControl("CATEGORY", _quizCategoryComboBox);
        Grid.SetColumn(categoryStack, 4);
        settings.Children.Add(categoryStack);
        _quizCategoryComboBox.SelectionChanged += (_, _) =>
        {
            if (!_quizRefreshing)
            {
                SyncQuizCategorySeriesName();
                SuggestNextQuizEpisode();
                RefreshQuizBank();
            }
        };

        _quizDifficultyComboBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MinHeight = 34 };
        _quizDifficultyComboBox.Items.Add("All difficulties");
        _quizDifficultyComboBox.Items.Add("easy");
        _quizDifficultyComboBox.Items.Add("medium");
        _quizDifficultyComboBox.Items.Add("hard");
        _quizDifficultyComboBox.SelectedIndex = 0;
        var difficultyStack = QuizLabeledControl("DIFFICULTY", _quizDifficultyComboBox);
        Grid.SetColumn(difficultyStack, 6);
        settings.Children.Add(difficultyStack);
        _quizDifficultyComboBox.SelectionChanged += (_, _) =>
        {
            if (!_quizRefreshing)
                RefreshQuizBank();
        };

        var pickRandom = new Button
        {
            Content = "Pick random questions",
            Height = 36,
            Padding = new Thickness(15, 0, 15, 0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 18, 0, 0),
        };
        pickRandom.Click += PickRandomQuizQuestions_Click;
        Grid.SetColumn(pickRandom, 8);
        settings.Children.Add(pickRandom);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);

        var bankCard = QuizCard(new Thickness(14));
        workspace.Children.Add(bankCard);
        var bank = new Grid();
        bank.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bank.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bankCard.Child = bank;

        var bankHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        bankHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bankHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bankHeading = new StackPanel();
        bankHeading.Children.Add(new TextBlock { Text = "Question Bank", FontSize = 17, FontWeight = FontWeights.SemiBold });
        _quizBankStatusText = new TextBlock { Text = "0 questions", Foreground = QuizMutedBrush(), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
        bankHeading.Children.Add(_quizBankStatusText);
        bankHeader.Children.Add(bankHeading);

        var bankActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var toggleSelected = new Button
        {
            Content = "Enable / disable selected",
            ToolTip = "Disabled questions stay in the bank but are excluded from random quiz selection.",
        };
        toggleSelected.Click += ToggleSelectedQuizQuestion_Click;
        bankActions.Children.Add(toggleSelected);
        var deleteSelected = new Button { Content = "Delete selected", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        deleteSelected.Click += DeleteSelectedQuizQuestion_Click;
        bankActions.Children.Add(deleteSelected);
        Grid.SetColumn(bankActions, 1);
        bankHeader.Children.Add(bankActions);
        bank.Children.Add(bankHeader);

        var bankTabs = new TabControl { Background = Brushes.White, BorderThickness = new Thickness(0) };
        Grid.SetRow(bankTabs, 1);
        bank.Children.Add(bankTabs);

        var browseTab = new TabItem { Header = "Browse" };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            browseTab.Style = sectionStyle;
        browseTab.Content = BuildQuizBankBrowse();
        bankTabs.Items.Add(browseTab);

        var importTab = new TabItem { Header = "Import from ChatGPT" };
        if (FindResource("SectionTabStyle") is Style importStyle)
            importTab.Style = importStyle;
        importTab.Content = BuildQuizImportPanel();
        bankTabs.Items.Add(importTab);

        var draftCard = QuizCard(new Thickness(14));
        Grid.SetColumn(draftCard, 2);
        workspace.Children.Add(draftCard);
        var draft = new Grid();
        draft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        draft.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        draftCard.Child = draft;

        var draftHeading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        draftHeading.Children.Add(new TextBlock { Text = "Quiz Draft", FontSize = 17, FontWeight = FontWeights.SemiBold });
        _quizDraftStatusText = new TextBlock
        {
            Text = "Pick random questions to create a draft.",
            Foreground = QuizMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        draftHeading.Children.Add(_quizDraftStatusText);
        draft.Children.Add(draftHeading);

        _quizDraftGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Margin = new Thickness(0),
        };
        _quizDraftGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(QuizDraftDisplayRow.Number)),
            Width = new DataGridLength(42),
        });
        _quizDraftGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(QuizDraftDisplayRow.Question)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizDraftGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Answer",
            Binding = new Binding(nameof(QuizDraftDisplayRow.Answer)),
            Width = new DataGridLength(140),
        });
        Grid.SetRow(_quizDraftGrid, 1);
        draft.Children.Add(_quizDraftGrid);

        return root;
    }

    private FrameworkElement BuildQuizBankBrowse()
    {
        var root = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _quizSearchTextBox = new TextBox { ToolTip = "Search question text or category" };
        _quizSearchTextBox.TextChanged += (_, _) =>
        {
            if (!_quizRefreshing)
                RefreshQuizBank();
        };
        search.Children.Add(_quizSearchTextBox);
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8, 0, 0, 0) };
        refresh.Click += (_, _) => RefreshQuizBank();
        Grid.SetColumn(refresh, 1);
        search.Children.Add(refresh);
        root.Children.Add(search);

        _quizBankGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Category",
            Binding = new Binding(nameof(QuizQuestion.Category)),
            SortMemberPath = nameof(QuizQuestion.Category),
            Width = new DataGridLength(125),
        });
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(QuizQuestion.Question)),
            SortMemberPath = nameof(QuizQuestion.Question),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Level",
            Binding = new Binding(nameof(QuizQuestion.Difficulty)),
            SortMemberPath = nameof(QuizQuestion.Difficulty),
            Width = new DataGridLength(75),
        });
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Correct",
            Binding = new Binding(nameof(QuizQuestion.CorrectAnswer)),
            SortMemberPath = nameof(QuizQuestion.CorrectAnswer),
            Width = new DataGridLength(130),
        });
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Times used",
            Binding = new Binding(nameof(QuizQuestion.TimesUsed)),
            SortMemberPath = nameof(QuizQuestion.TimesUsed),
            Width = new DataGridLength(82),
        });
        _quizBankGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(QuizQuestion.Availability)),
            SortMemberPath = nameof(QuizQuestion.IsEnabled),
            Width = new DataGridLength(82),
        });
        Grid.SetRow(_quizBankGrid, 1);
        root.Children.Add(_quizBankGrid);
        return root;
    }

    private FrameworkElement BuildQuizImportPanel()
    {
        var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var instructions = new TextBlock
        {
            Text = "Ask ChatGPT for a large JSON question bank, paste it below, then import it. Duplicate questions are skipped automatically.",
            Foreground = QuizMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(instructions);

        _quizImportTextBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };
        Grid.SetRow(_quizImportTextBox, 1);
        root.Children.Add(_quizImportTextBox);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var import = new Button
        {
            Content = "Import pasted JSON",
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };
        import.Click += ImportQuizJson_Click;
        actions.Children.Add(import);
        var load = new Button { Content = "Load JSON file" };
        load.Click += LoadQuizJsonFile_Click;
        actions.Children.Add(load);
        var prompt = new Button { Content = "Copy ChatGPT prompt", Margin = new Thickness(0) };
        prompt.Click += CopyQuizChatGptPrompt_Click;
        actions.Children.Add(prompt);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return root;
    }

    private void AddQuizNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;
        var projectsButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "1", StringComparison.Ordinal));
        if (projectsButton?.Parent is not StackPanel navigation)
            return;

        var quizButton = new Button
        {
            Content = "?   Quizzes",
            Tag = tabIndex.ToString(),
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            quizButton.Style = navStyle;
        quizButton.Click += Navigate_Click;
        var projectIndex = navigation.Children.IndexOf(projectsButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, projectIndex + 1), quizButton);
    }

    private void RefreshQuizBank()
    {
        if (!_quizWorkflowInitialized || _quizBankGrid is null || _quizCategoryComboBox is null || _quizDifficultyComboBox is null)
            return;

        try
        {
            _quizRefreshing = true;
            var selectedCategory = SelectedQuizCategory();
            var categories = QuizQuestionTopicCategorizer.Categories
                .Concat(_data.GetQuizCategories())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _quizCategoryComboBox.Items.Clear();
            _quizCategoryComboBox.Items.Add("All categories");
            foreach (var category in categories)
                _quizCategoryComboBox.Items.Add(category);
            _quizCategoryComboBox.SelectedItem = categories.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase)
                ? categories.First(category => string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                : "All categories";

            var categoryFilter = SelectedQuizCategory();
            var difficulty = SelectedQuizDifficulty();
            var search = _quizSearchTextBox?.Text ?? "";
            var questions = _data.GetQuizQuestions(search, categoryFilter, difficulty, limit: 10_000);
            _quizBankGrid.ItemsSource = questions;
            var total = _data.GetQuizQuestionCount(categoryFilter, difficulty);
            var enabled = _data.GetQuizQuestionCount(categoryFilter, difficulty, enabledOnly: true);
            var disabled = total - enabled;
            if (_quizBankStatusText is not null)
            {
                _quizBankStatusText.Text = search.Trim().Length == 0
                    ? $"{total:N0} matching • {enabled:N0} enabled • {disabled:N0} disabled • click column headers to sort"
                    : $"Showing {questions.Count:N0} search results • {enabled:N0} enabled • {disabled:N0} disabled";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Question bank: {_data.GetQuizQuestionCount():N0}";
        }
        catch (Exception error)
        {
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Quiz database: {error.Message}";
        }
        finally
        {
            _quizRefreshing = false;
        }
    }

    private void ImportQuizJson_Click(object sender, RoutedEventArgs e)
    {
        if (_quizImportTextBox is null)
            return;
        try
        {
            var result = _data.ImportQuizQuestions(_quizImportTextBox.Text, "ChatGPT import");
            _quizImportTextBox.Clear();
            RefreshQuizBank();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Imported {result.Inserted:N0} • skipped {result.Duplicates:N0} duplicates";
            MessageBox.Show(
                this,
                $"Parsed: {result.Parsed:N0}\nImported: {result.Inserted:N0}\nDuplicates skipped: {result.Duplicates:N0}",
                "Quiz Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadQuizJsonFile_Click(object sender, RoutedEventArgs e)
    {
        if (_quizImportTextBox is null)
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Import quiz questions",
            Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > QuizQuestionImportParser.MaximumImportCharacters * 4L)
                throw new InvalidDataException("Quiz import file is too large. Import smaller batches.");
            _quizImportTextBox.Text = File.ReadAllText(dialog.FileName);
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Loaded {Path.GetFileName(dialog.FileName)} — review it, then click Import pasted JSON";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Load Quiz JSON", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyQuizChatGptPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var category = SelectedQuizCategory();
            if (string.IsNullOrWhiteSpace(category))
                category = "General Knowledge";
            Clipboard.SetText(QuizQuestionImportParser.ChatGptPrompt(100, category));
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "ChatGPT import prompt copied to clipboard";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Copy Quiz Prompt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PickRandomQuizQuestions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_quizQuestionCountTextBox is null || _quizSecondsPerQuestionTextBox is null || _quizDraftGrid is null)
                return;
            if (!int.TryParse(_quizQuestionCountTextBox.Text.Trim(), out var count) || count is < 1 or > 100)
                throw new ArgumentException("Question count must be a whole number from 1 to 100.");
            if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
                throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");

            _quizDraftQuestions = _data.GetRandomQuizQuestions(count, SelectedQuizCategory(), SelectedQuizDifficulty()).ToList();
            _quizSecondsPerQuestion = seconds;
            _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);

            if (_quizDraftStatusText is not null)
            {
                var thinkingSeconds = count * seconds;
                _quizDraftStatusText.Text = $"{count} random enabled questions • {seconds} seconds per question • {thinkingSeconds / 60}:{thinkingSeconds % 60:00} total answer time. Pick again for a different random set.";
            }
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Random quiz draft created";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Quiz Draft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleSelectedQuizQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBankGrid?.SelectedItem is not QuizQuestion question)
            return;
        try
        {
            var enabled = !question.IsEnabled;
            _data.SetQuizQuestionEnabled(question.Id, enabled);

            if (!enabled && _quizDraftQuestions.Any(item => item.Id == question.Id))
            {
                _quizDraftQuestions = _quizDraftQuestions.Where(item => item.Id != question.Id).ToList();
                if (_quizDraftGrid is not null)
                    _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
                if (_quizDraftStatusText is not null)
                    _quizDraftStatusText.Text = "A disabled question was removed from the current draft. Pick random questions again to refill the draft.";
            }

            RefreshQuizBank();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = enabled ? "Question enabled for future quizzes" : "Question disabled from future quiz selection";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Question Availability", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedQuizQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBankGrid?.SelectedItem is not QuizQuestion question)
            return;
        var answer = MessageBox.Show(
            this,
            $"Delete this question from the reusable bank?\n\n{question.Question}",
            "Delete Quiz Question",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        try
        {
            _data.DeleteQuizQuestion(question.Id);
            _quizDraftQuestions = _quizDraftQuestions.Where(item => item.Id != question.Id).ToList();
            if (_quizDraftGrid is not null)
                _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
            RefreshQuizBank();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Quiz Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static IReadOnlyList<QuizDraftDisplayRow> QuizDraftRows(IEnumerable<QuizQuestion> questions) =>
        questions
            .Select((question, index) => new QuizDraftDisplayRow(
                index + 1,
                question.Question,
                $"{question.CorrectLetter}. {question.CorrectAnswer}"))
            .ToList();

    private string SelectedQuizCategory()
    {
        var value = _quizCategoryComboBox?.SelectedItem?.ToString()?.Trim() ?? "";
        return value.StartsWith("All ", StringComparison.OrdinalIgnoreCase) ? "" : value;
    }

    private string SelectedQuizDifficulty()
    {
        var value = _quizDifficultyComboBox?.SelectedItem?.ToString()?.Trim() ?? "";
        return value.StartsWith("All ", StringComparison.OrdinalIgnoreCase) ? "" : value;
    }

    private static TextBox QuizTextField(Grid parent, int column, string label, string value, string tooltip)
    {
        var textBox = new TextBox { Text = value, ToolTip = tooltip, Margin = new Thickness(0, 4, 0, 0) };
        var stack = QuizLabeledControl(label, textBox);
        Grid.SetColumn(stack, column);
        parent.Children.Add(stack);
        return textBox;
    }

    private static StackPanel QuizLabeledControl(string label, FrameworkElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(control);
        return stack;
    }

    private static Border QuizCard(Thickness padding) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
    };

    private static Brush QuizMutedBrush() => new SolidColorBrush(Color.FromRgb(102, 112, 133));
}

public sealed record QuizDraftDisplayRow(int Number, string Question, string Answer);
