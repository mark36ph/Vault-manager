using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizQuestionViewerInitialized;
    private bool _quizImportHadText;
    private TabControl? _quizBankTabs;
    private TextBlock? _quizQuestionDetailHeading;
    private TextBlock? _quizQuestionDetailMeta;
    private TextBlock? _quizQuestionDetailText;
    private TextBlock[]? _quizQuestionAnswerTexts;
    private TextBlock? _quizQuestionExplanationText;

    private void InitializeQuizQuestionViewer()
    {
        if (_quizQuestionViewerInitialized)
            return;

        if (_quizBankGrid is null)
        {
            Dispatcher.BeginInvoke(new Action(InitializeQuizQuestionViewer));
            return;
        }

        _quizQuestionViewerInitialized = true;
        _quizBankGrid.SelectionChanged += QuizBankGrid_QuestionViewerSelectionChanged;
        _quizBankGrid.MouseDoubleClick += QuizBankGrid_QuestionViewerMouseDoubleClick;

        _quizBankTabs ??= FindQuizVisualAncestor<TabControl>(_quizBankGrid);
        if (_quizBankTabs?.Items.Count > 0 && _quizBankTabs.Items[0] is TabItem questionsTab)
            questionsTab.Header = "Questions";

        if (_quizImportTextBox is not null)
        {
            _quizImportHadText = !string.IsNullOrWhiteSpace(_quizImportTextBox.Text);
            _quizImportTextBox.TextChanged += QuizImportTextBox_QuestionViewerTextChanged;
        }

        if (_quizBankGrid.Parent is Grid browseRoot)
            AddQuizQuestionDetailsPanel(browseRoot);

        if (_quizBankGrid.Items.Count > 0)
            _quizBankGrid.SelectedIndex = 0;
        else
            UpdateQuizQuestionDetails(null);
    }

    private void AddQuizQuestionDetailsPanel(Grid browseRoot)
    {
        if (browseRoot.RowDefinitions.Count < 2)
            return;

        browseRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        browseRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });

        var splitter = new GridSplitter
        {
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(235, 238, 242)),
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        Grid.SetRow(splitter, 2);
        browseRoot.Children.Add(splitter);

        var detailsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 5, 0, 0),
        };
        Grid.SetRow(detailsBorder, 3);
        browseRoot.Children.Add(detailsBorder);

        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        detailsBorder.Child = details;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _quizQuestionDetailHeading = new TextBlock
        {
            Text = "Selected question",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(_quizQuestionDetailHeading);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var editQuestion = new Button
        {
            Content = "Edit",
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = FontWeights.SemiBold,
        };
        editQuestion.Click += (_, _) =>
        {
            if (_quizBankGrid?.SelectedItem is QuizQuestion question)
                ShowEditQuizQuestionDialog(question);
        };
        actions.Children.Add(editQuestion);

        var openFullView = new Button
        {
            Content = "Open",
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
        };
        openFullView.Click += (_, _) =>
        {
            if (_quizBankGrid?.SelectedItem is QuizQuestion question)
                ShowQuizQuestionDialog(question);
        };
        actions.Children.Add(openFullView);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        details.Children.Add(header);

        _quizQuestionDetailMeta = new TextBlock
        {
            Text = "Select a question above to see all answers and its explanation.",
            Foreground = QuizMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_quizQuestionDetailMeta, 1);
        details.Children.Add(_quizQuestionDetailMeta);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 2);
        details.Children.Add(scroll);

        var body = new StackPanel();
        scroll.Content = body;
        _quizQuestionDetailText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        body.Children.Add(_quizQuestionDetailText);

        _quizQuestionAnswerTexts = Enumerable.Range(0, 4)
            .Select(_ => new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
            })
            .ToArray();
        foreach (var answerText in _quizQuestionAnswerTexts)
            body.Children.Add(answerText);

        body.Children.Add(new TextBlock
        {
            Text = "EXPLANATION",
            Foreground = QuizMutedBrush(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 9, 0, 2),
        });
        _quizQuestionExplanationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(_quizQuestionExplanationText);
    }

    private void QuizBankGrid_QuestionViewerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateQuizQuestionDetails(_quizBankGrid?.SelectedItem as QuizQuestion);
    }

    private void QuizBankGrid_QuestionViewerMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_quizBankGrid?.SelectedItem is QuizQuestion question)
            ShowEditQuizQuestionDialog(question);
    }

    private void QuizImportTextBox_QuestionViewerTextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(_quizImportTextBox?.Text);
        if (_quizImportHadText && !hasText && _quizBankTabs is not null && _quizBankGrid is not null && _quizBankGrid.Items.Count > 0)
        {
            _quizBankTabs.SelectedIndex = 0;
            _quizBankGrid.SelectedIndex = 0;
            _quizBankGrid.ScrollIntoView(_quizBankGrid.SelectedItem);
        }
        _quizImportHadText = hasText;
    }

    private void UpdateQuizQuestionDetails(QuizQuestion? question)
    {
        if (_quizQuestionDetailHeading is null ||
            _quizQuestionDetailMeta is null ||
            _quizQuestionDetailText is null ||
            _quizQuestionAnswerTexts is null ||
            _quizQuestionExplanationText is null)
            return;

        if (question is null)
        {
            _quizQuestionDetailHeading.Text = "Selected question";
            _quizQuestionDetailMeta.Text = "Select a question above to see all answers and its explanation.";
            _quizQuestionDetailText.Text = "";
            foreach (var answerText in _quizQuestionAnswerTexts)
                answerText.Text = "";
            _quizQuestionExplanationText.Text = "";
            return;
        }

        _quizQuestionDetailHeading.Text = $"Question #{question.Id}";
        _quizQuestionDetailMeta.Text =
            $"{question.Category} • {question.Difficulty} • {question.Availability} • used {question.TimesUsed:N0} time{(question.TimesUsed == 1 ? "" : "s")}";
        _quizQuestionDetailText.Text = question.Question;

        var answers = question.Answers;
        for (var index = 0; index < _quizQuestionAnswerTexts.Length; index++)
        {
            var correct = index == question.CorrectIndex;
            _quizQuestionAnswerTexts[index].Text = $"{(correct ? "✓ " : "")}{(char)('A' + index)}. {answers[index]}";
            _quizQuestionAnswerTexts[index].FontWeight = correct ? FontWeights.SemiBold : FontWeights.Normal;
            _quizQuestionAnswerTexts[index].Foreground = correct
                ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
                : new SolidColorBrush(Color.FromRgb(31, 41, 55));
        }

        _quizQuestionExplanationText.Text = string.IsNullOrWhiteSpace(question.Explanation)
            ? "No explanation stored."
            : question.Explanation;
    }

    private void ShowQuizQuestionDialog(QuizQuestion question)
    {
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock
        {
            Text = question.Question,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{question.Category} • {question.Difficulty} • {question.Availability} • used {question.TimesUsed:N0} time{(question.TimesUsed == 1 ? "" : "s")}",
            Foreground = QuizMutedBrush(),
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap,
        });

        for (var index = 0; index < question.Answers.Count; index++)
        {
            var correct = index == question.CorrectIndex;
            content.Children.Add(new TextBlock
            {
                Text = $"{(correct ? "✓ " : "")}{(char)('A' + index)}. {question.Answers[index]}",
                FontSize = 14,
                FontWeight = correct ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = "Explanation",
            Foreground = QuizMutedBrush(),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 4),
        });
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(question.Explanation) ? "No explanation stored." : question.Explanation,
            TextWrapping = TextWrapping.Wrap,
        });

        var window = new Window
        {
            Owner = this,
            Title = "Quiz Question",
            Width = 760,
            Height = 560,
            MinWidth = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content,
            },
        };
        window.ShowDialog();
    }

    private static T? FindQuizVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
