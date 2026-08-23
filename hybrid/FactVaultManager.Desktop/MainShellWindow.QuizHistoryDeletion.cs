using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizHistoryDeletionHookRegistered = RegisterQuizHistoryDeletionHook();

    private static bool RegisterQuizHistoryDeletionHook()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizHistoryGrid_Loaded));
        return true;
    }

    private static void QuizHistoryGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || Window.GetWindow(grid) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(window._quizHistoryGrid, grid))
            return;

        window.EnsureQuizHistoryDeleteButton();
    }

    private void EnsureQuizHistoryDeleteButton()
    {
        if (_quizHistoryGrid?.Parent is not Grid pageRoot)
            return;

        var actions = pageRoot.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 2 && panel.Orientation == Orientation.Horizontal);
        if (actions is null || actions.Children.OfType<Button>().Any(button => string.Equals(button.Content?.ToString(), "Delete quiz", StringComparison.Ordinal)))
            return;

        var delete = new Button
        {
            Content = "Delete quiz",
            MinWidth = 100,
            Margin = new Thickness(8, 0, 0, 0),
        };
        delete.Click += (_, _) => DeleteSelectedQuizHistory();
        actions.Children.Add(delete);
    }

    private void DeleteSelectedQuizHistory()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(this, "Select a quiz first.", "Delete Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var folderText = string.IsNullOrWhiteSpace(history.ProjectFolder)
            ? "No export folder is recorded for this quiz."
            : $"Export folder:\n{history.ProjectFolder}";
        var result = MessageBox.Show(
            this,
            $"Delete {history.SeriesName} {history.EpisodeLabel}?\n\nThis permanently removes the Quiz History entry, deletes its export folder, and removes one usage count from each question used in this quiz.\n\n{folderText}\n\nThis cannot be undone.",
            "Delete Quiz",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            if (!_data.DeleteQuizHistory(history.Id, deleteFolder: true))
            {
                MessageBox.Show(this, "The selected quiz no longer exists in Quiz History.", "Delete Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshQuizHistory();
                return;
            }

            RefreshQuizHistory();
            RefreshQuizBank();
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = "Quiz removed. Its project folder is being deleted in the background.";
        }
        catch (QuizHistoryFolderCleanupException error)
        {
            RefreshQuizHistory();
            RefreshQuizBank();
            MessageBox.Show(this, error.Message, "Delete Quiz", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Delete Quiz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
