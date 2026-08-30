using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryArchiveActionsVisibilityFixRegistered;

    public void InitializeQuizHistoryArchiveActionsVisibilityFix()
    {
        if (_quizHistoryArchiveActionsVisibilityFixRegistered)
            return;

        _quizHistoryArchiveActionsVisibilityFixRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryArchiveActionsVisible));
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryArchiveActionsVisible));
    }

    private void EnsureQuizHistoryArchiveActionsVisible()
    {
        if (_quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Border { Child: Grid root })
        {
            return;
        }

        var footer = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 3);
        var actions = footer?.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 1);
        if (actions is null)
            return;

        var deleteIndex = actions.Children
            .OfType<Button>()
            .Select((button, index) => new { button, index })
            .FirstOrDefault(item => string.Equals(item.button.Content?.ToString(), "Delete", StringComparison.Ordinal))
            ?.index ?? actions.Children.Count;

        if (_quizHistoryArchiveMatcherButton is null)
        {
            var matchArchive = new Button
            {
                Content = "Match archive",
                MinWidth = 110,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Match existing folders in the configured quiz archive back to Quiz History without moving or deleting files",
            };
            StyleQuizHistoryButton(matchArchive, Color.FromRgb(255, 202, 45));
            matchArchive.Click += async (_, _) => await MatchQuizHistoryArchiveAsync(matchArchive);
            actions.Children.Insert(deleteIndex, matchArchive);
            _quizHistoryArchiveMatcherButton = matchArchive;
            deleteIndex++;
        }

        if (_quizHistoryManualArchiveButton is null)
        {
            var archiveSelected = new Button
            {
                Content = "Archive selected",
                MinWidth = 118,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Safely archive the selected fully-uploaded quiz to the configured archive drive",
            };
            StyleQuizHistoryButton(archiveSelected, Color.FromRgb(70, 235, 115));
            archiveSelected.Click += async (_, _) => await ArchiveSelectedQuizHistoryAsync(archiveSelected);
            actions.Children.Insert(deleteIndex, archiveSelected);
            _quizHistoryManualArchiveButton = archiveSelected;
        }
    }
}
