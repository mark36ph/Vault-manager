using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizAiGenerationHookRegistered = RegisterQuizAiGenerationHook();
    private TextBox? _quizAiCountTextBox;
    private ComboBox? _quizAiCategoryComboBox;
    private ComboBox? _quizAiDifficultyComboBox;
    private TextBox? _quizAiTopicTextBox;
    private DataGrid? _quizAiResultsGrid;
    private TextBlock? _quizAiStatusText;
    private ProgressBar? _quizAiProgressBar;
    private TextBlock? _quizAiProgressText;
    private Button? _quizAiGenerateButton;
    private List<QuizAiReviewRow> _quizAiReviewRows = new();
    private bool _quizAiGenerationTabInitialized;
    private int _quizAiGenerationTabAttempts;

    private static bool RegisterQuizAiGenerationHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizAiGenerationWindow_Loaded));
        return true;
    }

    private static void QuizAiGenerationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(window.InitializeQuizAiGenerationTab));
    }

    private void InitializeQuizAiGenerationTab()
    {
        if (_quizAiGenerationTabInitialized)
            return;

        if (_quizBankTabs is null)
        {
            if (_quizAiGenerationTabAttempts++ < 40)
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(InitializeQuizAiGenerationTab));
            return;
        }

        EnsureQuizAiGenerationTab(_quizBankTabs);
        _quizAiGenerationTabInitialized = true;
    }

    private void EnsureQuizAiGenerationTab(TabControl tabs)
    {
        if (tabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "Generate with AI", StringComparison.OrdinalIgnoreCase)))
            return;

        var tab = new TabItem
        {
            Header = "Generate with AI",
            Content = BuildQuizAiGenerationPanel(),
        };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            tab.Style = sectionStyle;

        tabs.Items.Add(tab);
        StyleStandaloneQuestionBankTabs(tabs);
    }

    private FrameworkElement BuildQuizAiGenerationPanel()
    {
        var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "Generate quiz questions with your OpenAI API key, review them here, then add the ones you want to the question bank.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        root.Children.Add(intro);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _quizAiCountTextBox = new TextBox { Text = "10", MinHeight = 34 };
        AddQuizAiField(form, 0, "QUESTIONS", _quizAiCountTextBox);

        _quizAiCategoryComboBox = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            MinHeight = 34,
        };
        _quizAiCategoryComboBox.Items.Add("General Knowledge");
        foreach (var category in QuizQuestionTopicCategorizer.Categories)
            _quizAiCategoryComboBox.Items.Add(category);
        _quizAiCategoryComboBox.Background = new SolidColorBrush(Color.FromRgb(210, 220, 240));
        _quizAiCategoryComboBox.Foreground = new SolidColorBrush(Color.FromRgb(20, 32, 72));
        _quizAiCategoryComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 105, 180));
        _quizAiCategoryComboBox.SelectedIndex = 0;
        AddQuizAiField(form, 2, "CATEGORY", _quizAiCategoryComboBox);

        _quizAiDifficultyComboBox = new ComboBox { MinHeight = 34 };
        foreach (var difficulty in new[] { "mixed", "easy", "medium", "hard" })
            _quizAiDifficultyComboBox.Items.Add(difficulty);
        _quizAiDifficultyComboBox.Background = new SolidColorBrush(Color.FromRgb(210, 220, 240));
        _quizAiDifficultyComboBox.Foreground = new SolidColorBrush(Color.FromRgb(20, 32, 72));
        _quizAiDifficultyComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 105, 180));
        _quizAiDifficultyComboBox.SelectedIndex = 0;
        AddQuizAiField(form, 4, "DIFFICULTY", _quizAiDifficultyComboBox);

        _quizAiTopicTextBox = new TextBox
        {
            MinHeight = 34,
            ToolTip = "Optional narrower topic, for example Ancient Rome or Premier League",
        };
        AddQuizAiField(form, 6, "OPTIONAL TOPIC", _quizAiTopicTextBox);

        _quizAiGenerateButton = new Button
        {
            Content = "Generate questions",
            Height = 36,
            Padding = new Thickness(15, 0, 15, 0),
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        StyleStandaloneQuestionBankButton(_quizAiGenerateButton, Color.FromRgb(0, 204, 255));
        _quizAiGenerateButton.Click += GenerateQuizAiQuestions_Click;
        Grid.SetColumn(_quizAiGenerateButton, 8);
        form.Children.Add(_quizAiGenerateButton);
        Grid.SetRow(form, 1);
        root.Children.Add(form);

        _quizAiResultsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 220,
            ColumnHeaderHeight = 36,
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(8, 29, 75)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(13, 38, 93)),
            AlternationCount = 2,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(1),
        };

        var resultCellStyle = new Style(typeof(DataGridCell));
        resultCellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        resultCellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        resultCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
        resultCellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        resultCellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        var selectedCellTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true,
        };
        selectedCellTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selectedCellTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        resultCellStyle.Triggers.Add(selectedCellTrigger);
        _quizAiResultsGrid.CellStyle = resultCellStyle;

        var resultHeaderStyle = new Style(typeof(DataGridColumnHeader));
        resultHeaderStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        resultHeaderStyle.Setters.Add(new Setter(
            Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        resultHeaderStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        resultHeaderStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 9, 9, 9)));
        resultHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 204, 255))));
        resultHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderThicknessProperty,
            new Thickness(0, 0, 0, 1)));
        _quizAiResultsGrid.ColumnHeaderStyle = resultHeaderStyle;
        _quizAiResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(QuizAiReviewRow.Question)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        _quizAiResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Answers",
            Binding = new Binding(nameof(QuizAiReviewRow.Answers)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        _quizAiResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Correct",
            Binding = new Binding(nameof(QuizAiReviewRow.Correct)),
            Width = new DataGridLength(100),
        });
        _quizAiResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Category",
            Binding = new Binding(nameof(QuizAiReviewRow.Category)),
            Width = new DataGridLength(120),
        });
        _quizAiResultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Level",
            Binding = new Binding(nameof(QuizAiReviewRow.Difficulty)),
            Width = new DataGridLength(80),
        });
        Grid.SetRow(_quizAiResultsGrid, 2);
        root.Children.Add(_quizAiResultsGrid);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _quizAiStatusText = new TextBlock
        {
            Text = "Ready. OpenAI settings are read from Settings → AI.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
        };
        statusPanel.Children.Add(_quizAiStatusText);

        var progressRow = new Grid
        {
            Margin = new Thickness(0, 6, 18, 0),
            Visibility = Visibility.Collapsed,
        };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _quizAiProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 9,
            MinWidth = 220,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Workflow progress. OpenAI does not expose an exact server-side completion percentage for a single generation request.",
        };
        progressRow.Children.Add(_quizAiProgressBar);
        _quizAiProgressText = new TextBlock
        {
            Text = "0%",
            MinWidth = 44,
            Margin = new Thickness(10, 0, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_quizAiProgressText, 1);
        progressRow.Children.Add(_quizAiProgressText);
        statusPanel.Children.Add(progressRow);
        footer.Children.Add(statusPanel);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var addSelected = new Button
        {
            Content = "Add selected",
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
        };
        StyleStandaloneQuestionBankButton(addSelected, Color.FromRgb(0, 204, 255));
        addSelected.Click += (_, _) => AddGeneratedQuizQuestionsToBank(selectedOnly: true);
        actions.Children.Add(addSelected);

        var addAll = new Button
        {
            Content = "Add all to bank",
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = FontWeights.SemiBold,
        };
        StyleStandaloneQuestionBankButton(addAll, Color.FromRgb(70, 235, 115));
        addAll.Click += (_, _) => AddGeneratedQuizQuestionsToBank(selectedOnly: false);
        actions.Children.Add(addAll);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private async void GenerateQuizAiQuestions_Click(object sender, RoutedEventArgs e)
    {
        if (_quizAiCountTextBox is null ||
            _quizAiCategoryComboBox is null ||
            _quizAiDifficultyComboBox is null ||
            _quizAiTopicTextBox is null ||
            _quizAiResultsGrid is null ||
            _quizAiGenerateButton is null)
            return;

        _quizAiGenerateButton.IsEnabled = false;
        try
        {
            SetQuizAiProgress(QuizAiGenerationStage.Preparing, "Preparing AI generation");

            if (!int.TryParse(_quizAiCountTextBox.Text.Trim(), out var count))
                throw new ArgumentException("Question count must be a whole number between 1 and 50.");

            var request = QuizAiGenerationRequest.Create(
                count,
                _quizAiCategoryComboBox.Text,
                _quizAiDifficultyComboBox.SelectedItem?.ToString(),
                _quizAiTopicTextBox.Text);

            var settings = _data.LoadSettings();
            var credentials = NativeProviderCredentials.FromSettings(settings);
            var apiKey = credentials.Get("openai", required: false);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Add your OpenAI API key in Settings → AI first.");

            var model = string.IsNullOrWhiteSpace(settings.OpenAiModel)
                ? "gpt-5-mini"
                : settings.OpenAiModel.Trim();

            using var provider = new NativeOpenAITextProvider(
                apiKey,
                QuizAiQuestionGeneration.ProviderInstructions,
                model);

            SetQuizAiProgress(
                QuizAiGenerationStage.WaitingForOpenAi,
                $"Generating {request.Count} questions with {model}");
            var response = await provider.GenerateAsync(QuizAiQuestionGeneration.BuildPrompt(request));

            SetQuizAiProgress(QuizAiGenerationStage.ResponseReceived, "OpenAI response received");
            SetQuizAiProgress(QuizAiGenerationStage.Validating, "Validating generated questions");
            var questions = QuizAiQuestionGeneration.ParseResponse(response, request);

            SetQuizAiProgress(QuizAiGenerationStage.LoadingReview, "Loading questions for review");
            _quizAiReviewRows = questions.Select(question => new QuizAiReviewRow(question)).ToList();
            _quizAiResultsGrid.ItemsSource = _quizAiReviewRows;
            _quizAiResultsGrid.SelectAll();

            SetQuizAiProgress(
                QuizAiGenerationStage.Complete,
                $"Generated {_quizAiReviewRows.Count} questions. Review them, then add selected or add all");
        }
        catch (Exception error)
        {
            SetQuizAiStatus(error.Message);
            MessageBox.Show(this, error.Message, "Generate Quiz Questions", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _quizAiGenerateButton.IsEnabled = true;
        }
    }

    private void AddGeneratedQuizQuestionsToBank(bool selectedOnly)
    {
        if (_quizAiResultsGrid is null)
            return;

        try
        {
            var rows = selectedOnly
                ? _quizAiResultsGrid.SelectedItems.OfType<QuizAiReviewRow>().ToArray()
                : _quizAiReviewRows.ToArray();
            var payload = QuizAiQuestionGeneration.SerializeForImport(rows.Select(row => row.Item));
            var result = _data.ImportQuizQuestions(payload, "OpenAI generation");
            RefreshQuizBank();

            SetQuizAiStatus(result.Duplicates == 0
                ? $"Added {result.Inserted} questions to the bank."
                : $"Added {result.Inserted} questions; skipped {result.Duplicates} duplicates.");
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"AI questions added: {result.Inserted}";
        }
        catch (Exception error)
        {
            SetQuizAiStatus(error.Message);
            MessageBox.Show(this, error.Message, "Add AI Questions", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetQuizAiProgress(QuizAiGenerationStage stage, string message)
    {
        var percent = QuizAiGenerationProgress.Percent(stage);
        if (_quizAiProgressBar is not null)
        {
            _quizAiProgressBar.Value = percent;
            if (_quizAiProgressBar.Parent is FrameworkElement progressRow)
                progressRow.Visibility = Visibility.Visible;
        }
        if (_quizAiProgressText is not null)
            _quizAiProgressText.Text = $"{percent}%";
        SetQuizAiStatus($"{message}… {percent}%");
    }

    private void SetQuizAiStatus(string message)
    {
        if (_quizAiStatusText is not null)
            _quizAiStatusText.Text = message;
    }

    private static void AddQuizAiField(Grid grid, int column, string label, FrameworkElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(control);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private sealed record QuizAiReviewRow(QuizQuestionImportItem Item)
    {
        public string Question => Item.Question;
        public string Answers => $"A: {Item.OptionA}   B: {Item.OptionB}   C: {Item.OptionC}   D: {Item.OptionD}";
        public string Correct => Item.CorrectIndex is >= 0 and <= 3
            ? $"{(char)('A' + Item.CorrectIndex)}: {Item.Answers[Item.CorrectIndex]}"
            : "";
        public string Category => Item.Category;
        public string Difficulty => Item.Difficulty;
    }
}
