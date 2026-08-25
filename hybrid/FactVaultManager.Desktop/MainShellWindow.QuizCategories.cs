using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private DataGrid? _quizCategoriesGrid;
    private ComboBox? _quizAssignCategoryComboBox;
    private TextBlock? _quizCategoriesStatusText;
    private TabItem? _quizCategoriesTab;

    private void EnsureQuizCategoriesTab(TabControl tabs)
    {
        if (_quizCategoriesTab is not null || tabs.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "Categories", StringComparison.OrdinalIgnoreCase)))
            return;

        _quizCategoriesTab = new TabItem
        {
            Header = "Categories",
            Content = BuildQuizCategoriesPanel(),
        };
        if (FindResource("SectionTabStyle") is Style sectionStyle)
            _quizCategoriesTab.Style = sectionStyle;

        tabs.Items.Insert(Math.Min(1, tabs.Items.Count), _quizCategoriesTab);
        tabs.SelectionChanged += (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.Source, tabs) && ReferenceEquals(tabs.SelectedItem, _quizCategoriesTab))
                RefreshQuizCategorySection();
        };
        RefreshQuizCategorySection();
    }

    private FrameworkElement BuildQuizCategoriesPanel()
    {
        var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Categories",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        _quizCategoriesStatusText = new TextBlock
        {
            Text = "Category totals",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        heading.Children.Add(_quizCategoriesStatusText);
        header.Children.Add(heading);

        var refresh = new Button
        {
            Content = "Refresh categories",
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleStandaloneQuestionBankButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += (_, _) => RefreshQuizCategorySection();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        _quizCategoriesGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 34,
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

        var categoryCellStyle = new Style(typeof(DataGridCell));
        categoryCellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        categoryCellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        categoryCellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
        categoryCellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        categoryCellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        var selectedCellTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true,
        };
        selectedCellTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selectedCellTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        categoryCellStyle.Triggers.Add(selectedCellTrigger);
        _quizCategoriesGrid.CellStyle = categoryCellStyle;

        var categoryHeaderStyle = new Style(typeof(DataGridColumnHeader));
        categoryHeaderStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        categoryHeaderStyle.Setters.Add(new Setter(
            Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        categoryHeaderStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        categoryHeaderStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 9, 9, 9)));
        categoryHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 204, 255))));
        categoryHeaderStyle.Setters.Add(new Setter(
            DataGridColumnHeader.BorderThicknessProperty,
            new Thickness(0, 0, 0, 1)));
        _quizCategoriesGrid.ColumnHeaderStyle = categoryHeaderStyle;
        _quizCategoriesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Category",
            Binding = new Binding(nameof(QuizQuestionCategorySummary.Category)),
            SortMemberPath = nameof(QuizQuestionCategorySummary.Category),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _quizCategoriesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Questions",
            Binding = new Binding(nameof(QuizQuestionCategorySummary.QuestionCount)),
            SortMemberPath = nameof(QuizQuestionCategorySummary.QuestionCount),
            Width = new DataGridLength(90),
        });
        _quizCategoriesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Enabled",
            Binding = new Binding(nameof(QuizQuestionCategorySummary.EnabledCount)),
            SortMemberPath = nameof(QuizQuestionCategorySummary.EnabledCount),
            Width = new DataGridLength(80),
        });
        _quizCategoriesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Disabled",
            Binding = new Binding(nameof(QuizQuestionCategorySummary.DisabledCount)),
            SortMemberPath = nameof(QuizQuestionCategorySummary.DisabledCount),
            Width = new DataGridLength(80),
        });
        _quizCategoriesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Times used",
            Binding = new Binding(nameof(QuizQuestionCategorySummary.TimesUsed)),
            SortMemberPath = nameof(QuizQuestionCategorySummary.TimesUsed),
            Width = new DataGridLength(90),
        });
        _quizCategoriesGrid.MouseDoubleClick += QuizCategoriesGrid_MouseDoubleClick;
        Grid.SetRow(_quizCategoriesGrid, 1);
        root.Children.Add(_quizCategoriesGrid);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var autoCategorize = new Button
        {
            Content = "Update General Knowledge",
            ToolTip = "Automatically move all questions still filed as General Knowledge into topic categories.",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            FontWeight = FontWeights.SemiBold,
        };
        StyleStandaloneQuestionBankButton(autoCategorize, Color.FromRgb(204, 70, 255));
        autoCategorize.Click += (sender, eventArgs) =>
        {
            AutoCategorizeQuizQuestions_Click(sender, eventArgs);
            RefreshQuizCategorySection();
        };
        actions.Children.Add(autoCategorize);

        var view = new Button
        {
            Content = "View selected category",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
        };
        StyleStandaloneQuestionBankButton(view, Color.FromRgb(0, 204, 255));
        view.Click += (_, _) => ViewSelectedQuizCategory();
        Grid.SetColumn(view, 2);
        actions.Children.Add(view);

        _quizAssignCategoryComboBox = new ComboBox
        {
            MinHeight = 34,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var category in QuizQuestionTopicCategorizer.Categories)
            _quizAssignCategoryComboBox.Items.Add(category);
        _quizAssignCategoryComboBox.Background = new SolidColorBrush(Color.FromRgb(20, 32, 72));
        _quizAssignCategoryComboBox.Foreground = new SolidColorBrush(Color.FromRgb(225, 235, 255));
        _quizAssignCategoryComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 105, 180));
        _quizAssignCategoryComboBox.BorderThickness = new Thickness(1);
        _quizAssignCategoryComboBox.SelectedItem = "General Knowledge";
        Grid.SetColumn(_quizAssignCategoryComboBox, 4);
        actions.Children.Add(_quizAssignCategoryComboBox);

        var assign = new Button
        {
            Content = "Move selected question",
            ToolTip = "Move the question currently selected on the Questions tab into this category.",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
        };
        StyleStandaloneQuestionBankButton(assign, Color.FromRgb(70, 235, 115));
        assign.Click += AssignSelectedQuizQuestionCategory_Click;
        Grid.SetColumn(assign, 6);
        actions.Children.Add(assign);

        var hint = new TextBlock
        {
            Text = "Tip: select a question on Questions first if you want to move it manually.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(hint, 7);
        actions.Children.Add(hint);

        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private void RefreshQuizCategorySection()
    {
        if (_quizCategoriesGrid is null)
            return;

        try
        {
            var selected = (_quizCategoriesGrid.SelectedItem as QuizQuestionCategorySummary)?.Category;
            var summaries = _data.GetQuizCategorySummaries();
            _quizCategoriesGrid.ItemsSource = summaries;

            var total = summaries.Sum(item => item.QuestionCount);
            var populated = summaries.Count(item => item.QuestionCount > 0);
            var easy = _data.GetQuizQuestionCount(difficulty: "easy");
            var medium = _data.GetQuizQuestionCount(difficulty: "medium");
            var hard = _data.GetQuizQuestionCount(difficulty: "hard");
            var insane = _data.GetQuizQuestionCount(difficulty: "insane");
            var generalKnowledge = summaries.FirstOrDefault(item =>
                string.Equals(item.Category, "General Knowledge", StringComparison.OrdinalIgnoreCase));
            if (_quizCategoriesStatusText is not null)
            {
                var categorySummary = generalKnowledge is { QuestionCount: > 0 }
                    ? $"{total:N0} questions across {populated:N0} populated categories • {generalKnowledge.QuestionCount:N0} General Knowledge"
                    : $"{total:N0} questions across {populated:N0} populated categories";
                _quizCategoriesStatusText.Text =
                    $"{categorySummary}\nEasy {easy:N0} • Medium {medium:N0} • Hard {hard:N0} • Insane {insane:N0}";
            }

            var selectedItem = summaries.FirstOrDefault(item =>
                string.Equals(item.Category, selected, StringComparison.OrdinalIgnoreCase));
            selectedItem ??= generalKnowledge is { QuestionCount: > 0 }
                ? generalKnowledge
                : summaries.FirstOrDefault(item => item.QuestionCount > 0) ?? summaries.FirstOrDefault();
            if (selectedItem is not null)
                _quizCategoriesGrid.SelectedItem = selectedItem;
        }
        catch (Exception error)
        {
            if (_quizCategoriesStatusText is not null)
                _quizCategoriesStatusText.Text = $"Could not load categories: {error.Message}";
        }
    }

    private void QuizCategoriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ViewSelectedQuizCategory();

    private void ViewSelectedQuizCategory()
    {
        if (_quizCategoriesGrid?.SelectedItem is not QuizQuestionCategorySummary summary || summary.QuestionCount == 0)
            return;

        RefreshQuizBank();
        if (_quizCategoryComboBox is not null)
        {
            var matching = _quizCategoryComboBox.Items
                .Cast<object>()
                .FirstOrDefault(item => string.Equals(item?.ToString(), summary.Category, StringComparison.OrdinalIgnoreCase));
            if (matching is not null)
                _quizCategoryComboBox.SelectedItem = matching;
        }
        if (_quizBankTabs is not null)
            _quizBankTabs.SelectedIndex = 0;
        RefreshQuizBank();
    }

    private void AssignSelectedQuizQuestionCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBankGrid?.SelectedItem is not QuizQuestion question)
        {
            MessageBox.Show(
                this,
                "Select a question on the Questions tab first, then return to Categories and choose where to move it.",
                "Move Quiz Question",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var category = _quizAssignCategoryComboBox?.SelectedItem?.ToString()?.Trim() ?? "";
        if (category.Length == 0)
            return;

        try
        {
            _data.SetQuizQuestionCategory(question.Id, category);
            RefreshQuizBank();
            RefreshQuizCategorySection();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Question #{question.Id} moved to {category}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Move Quiz Question", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
