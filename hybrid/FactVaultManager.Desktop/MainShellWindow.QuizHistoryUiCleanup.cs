using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryUiCleanupRegistered;

    public void InitializeQuizHistoryUiCleanup()
    {
        if (_quizHistoryUiCleanupRegistered)
            return;

        _quizHistoryUiCleanupRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyQuizHistoryUiCleanup));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyQuizHistoryUiCleanup));
    }

    private void ApplyQuizHistoryUiCleanup()
    {
        if (_quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Border { Child: Grid root })
        {
            return;
        }

        var header = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 0);
        RemoveQuizHistoryButtonsByContent(header, "Update paths");

        var footer = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 3);
        var actions = footer?.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 1);
        if (actions is null)
            return;

        RemoveQuizHistoryButtonsByContent(actions, "Match archive", "Archive selected");

        var archive = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(
                button.Content?.ToString(),
                "Archive completed C",
                StringComparison.Ordinal));
        if (archive is not null)
        {
            archive.Content = "Archive C → Z";
            archive.MinWidth = 108;
            archive.ToolTip = "Archive completed physical C: quiz projects to Z: after upload and verification checks";
            StyleQuizHistoryButton(archive, Color.FromRgb(64, 190, 255));
        }
    }

    private static void RemoveQuizHistoryButtonsByContent(Panel? panel, params string[] contents)
    {
        if (panel is null || contents.Length == 0)
            return;

        var unwanted = contents.ToHashSet(StringComparer.Ordinal);
        var buttons = panel.Children
            .OfType<Button>()
            .Where(button => unwanted.Contains(button.Content?.ToString() ?? ""))
            .ToList();
        foreach (var button in buttons)
            panel.Children.Remove(button);
    }
}
