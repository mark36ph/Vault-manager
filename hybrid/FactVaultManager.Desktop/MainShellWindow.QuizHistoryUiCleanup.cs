using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryUiCleanupRegistered;
    private Button? _quizHistoryMoreButton;

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

        var analyticsRefresh = header?.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Refresh", StringComparison.Ordinal));
        if (analyticsRefresh is not null)
        {
            analyticsRefresh.Content = "Refresh analytics";
            analyticsRefresh.MinWidth = 128;
            analyticsRefresh.ToolTip = "Refresh YouTube analytics for Quiz History";
        }

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
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "Archive completed C", StringComparison.Ordinal) ||
                string.Equals(button.Content?.ToString(), "Archive C → Z", StringComparison.Ordinal));
        var delete = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Delete", StringComparison.Ordinal));

        if (_quizHistoryMoreButton is not null && actions.Children.Contains(_quizHistoryMoreButton))
        {
            if (archive is not null)
                archive.Visibility = Visibility.Collapsed;
            if (delete is not null)
                delete.Visibility = Visibility.Collapsed;
            return;
        }

        if (archive is not null)
        {
            archive.Content = "Archive C → Z";
            archive.Visibility = Visibility.Collapsed;
        }
        if (delete is not null)
            delete.Visibility = Visibility.Collapsed;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.Bottom,
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 78)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(1),
        };

        var archiveItem = new MenuItem
        {
            Header = "Archive completed C → Z",
            ToolTip = "Archive completed physical C: quiz projects to Z: after upload and verification checks",
            IsEnabled = archive is not null,
        };
        archiveItem.Click += async (_, _) =>
        {
            if (archive is not null)
                await ArchiveGroupedCompletedCQuizProjectsAsync(archive);
        };
        menu.Items.Add(archiveItem);

        var deleteItem = new MenuItem
        {
            Header = "Delete selected quiz…",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 125, 135)),
            ToolTip = "Delete the selected Quiz History entry",
        };
        deleteItem.Click += (_, _) => DeleteSelectedQuizHistory();
        menu.Items.Add(deleteItem);

        var more = new Button
        {
            Content = "More ▾",
            MinWidth = 82,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Less-used Quiz History actions",
            ContextMenu = menu,
        };
        StyleQuizHistoryButton(more, Color.FromRgb(160, 175, 215));
        more.Click += (_, _) =>
        {
            if (more.ContextMenu is null)
                return;
            more.ContextMenu.PlacementTarget = more;
            more.ContextMenu.IsOpen = true;
        };

        var deleteIndex = delete is null ? actions.Children.Count : actions.Children.IndexOf(delete);
        actions.Children.Insert(Math.Max(0, deleteIndex), more);
        _quizHistoryMoreButton = more;
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
