using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryManualArchiveUiRegistered;
    private Button? _quizHistoryManualArchiveButton;

    public void InitializeQuizHistoryManualArchiveUi()
    {
        if (_quizHistoryManualArchiveUiRegistered)
            return;

        _quizHistoryManualArchiveUiRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryManualArchiveButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryManualArchiveButton));
    }

    private void EnsureQuizHistoryManualArchiveButton()
    {
        if (_quizHistoryManualArchiveButton is not null ||
            _quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Grid root)
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

        var button = new Button
        {
            Content = "Archive selected",
            MinWidth = 118,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Safely archive the selected fully-uploaded quiz to the configured NAS/archive drive",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(70, 235, 115));
        button.Click += async (_, _) => await ArchiveSelectedQuizHistoryAsync(button);
        actions.Children.Add(button);
        _quizHistoryManualArchiveButton = button;
    }

    private async Task ArchiveSelectedQuizHistoryAsync(Button button)
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary selected)
        {
            MessageBox.Show(
                this,
                "Select one quiz in Quiz History first.",
                "Archive Quiz",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var history = _data.GetQuizHistory().FirstOrDefault(item => item.Id == selected.Id);
        if (history is null)
        {
            MessageBox.Show(this, "The selected quiz is no longer in Quiz History.", "Archive Quiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = _data.LoadSettings();
        if (!settings.ArchiveAfterUpload)
        {
            MessageBox.Show(
                this,
                "Enable 'Move a quiz project to the NAS after all of its required uploads are complete' in Settings → General first.",
                "Archive Quiz",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
        {
            MessageBox.Show(this, "Choose a NAS archive folder in Settings → General first.", "Archive Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var remaining = SocialUploadQueuePlanner.RemainingDestinations(history);
        if (remaining != SocialUploadDestination.None)
        {
            MessageBox.Show(
                this,
                $"This quiz cannot be archived yet because required uploads are still outstanding.\n\nRemaining: {remaining}",
                "Archive Quiz",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(history.ProjectFolder) || !Directory.Exists(history.ProjectFolder))
        {
            MessageBox.Show(
                this,
                $"The recorded local project folder was not found:\n\n{history.ProjectFolder}",
                "Archive Quiz",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var archiveRoot = Path.GetFullPath(settings.NasArchiveFolder.Trim());
            var source = Path.GetFullPath(history.ProjectFolder.Trim());
            if (source.StartsWith(archiveRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "This quiz is already stored in the configured archive.", "Archive Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, error.Message, "Archive Quiz", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Archive this fully-uploaded quiz now?\n\nSource:\n{history.ProjectFolder}\n\nArchive root:\n{settings.NasArchiveFolder}\n\nFactburst will copy every file, verify the copy, update Quiz History, and only then remove the local C: folder.",
            "Archive Quiz",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        button.IsEnabled = false;
        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = "Archiving selected quiz: copying and verifying...";

        try
        {
            var result = await Task.Run(() => _data.ArchiveQuizProject(history.Id));
            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = result.SourceDeleted
                    ? "Archive complete; local project removed"
                    : "Archive complete; local cleanup needs attention";

            var warning = string.IsNullOrWhiteSpace(result.Warning) ? "" : $"\n\nWarning:\n{result.Warning}";
            MessageBox.Show(
                this,
                $"Archive complete.\n\nStored at:\n{result.DestinationFolder}\n\nLocal C: copy removed: {(result.SourceDeleted ? "Yes" : "No")}{warning}",
                "Archive Quiz",
                MessageBoxButton.OK,
                result.SourceDeleted ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "Archive failed; local project was kept";
            MessageBox.Show(
                this,
                $"The quiz was not archived. The local project has been kept.\n\n{error.Message}",
                "Archive Quiz",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
