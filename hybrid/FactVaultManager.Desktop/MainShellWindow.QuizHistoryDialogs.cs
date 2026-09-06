using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ShowQuizHistoryQuestionsDialog(QuizHistorySummary history, IReadOnlyList<QuizHistoryQuestion> questions)
    {
        var dialog = new Window
        {
            Title = $"Questions — {history.SeriesName} {history.EpisodeLabel}",
            Owner = this,
            Width = 1080,
            Height = 700,
            MinWidth = 780,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        var summary = new StackPanel();
        summary.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(history.SeriesName)
                ? "Unnumbered legacy export"
                : $"{history.SeriesName} {history.EpisodeLabel}",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"{history.CreatedDisplay}  •  {history.QuestionCount} questions  •  {history.Format}  •  {history.QuestionSeconds} seconds per question",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"Categories: {history.Categories}",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(summary, 0);
        root.Children.Add(summary);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            AlternationCount = 2,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            MinRowHeight = 42,
            Margin = new Thickness(0, 14, 0, 12),
            ItemsSource = questions,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
        };
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        grid.CellStyle = cellStyle;
        var questionTextStyle = new Style(typeof(TextBlock));
        questionTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        questionTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(QuizHistoryQuestion.Position)), Width = new DataGridLength(52) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Bank No.", Binding = new Binding(nameof(QuizHistoryQuestion.QuestionId)), Width = new DataGridLength(82) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding(nameof(QuizHistoryQuestion.Category)), Width = new DataGridLength(150) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new Binding(nameof(QuizHistoryQuestion.Difficulty)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Question", Binding = new Binding(nameof(QuizHistoryQuestion.Question)), ElementStyle = questionTextStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var close = new Button { Content = "Close", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);
        dialog.ShowDialog();
    }
}
