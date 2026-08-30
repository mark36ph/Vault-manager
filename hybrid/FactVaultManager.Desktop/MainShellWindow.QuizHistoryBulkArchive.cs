using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryBulkArchiveUiRegistered;
    private Button? _quizHistoryBulkArchiveButton;

    public void InitializeQuizHistoryBulkArchiveUi()
    {
        if (_quizHistoryBulkArchiveUiRegistered)
            return;

        _quizHistoryBulkArchiveUiRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryBulkArchiveButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryBulkArchiveButton));
    }

    private void EnsureQuizHistoryBulkArchiveButton()
    {
        if (_quizHistoryBulkArchiveButton is not null ||
            _quizHistoryTabIndex < 0 ||
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

        var archiveSelectedIndex = actions.Children
            .OfType<Button>()
            .Select((button, index) => new { button, index })
            .FirstOrDefault(item => string.Equals(item.button.Content?.ToString(), "Archive selected", StringComparison.Ordinal))
            ?.index;
        var deleteIndex = actions.Children
            .OfType<Button>()
            .Select((button, index) => new { button, index })
            .FirstOrDefault(item => string.Equals(item.button.Content?.ToString(), "Delete", StringComparison.Ordinal))
            ?.index ?? actions.Children.Count;
        var insertIndex = archiveSelectedIndex ?? deleteIndex;

        var button = new Button
        {
            Content = "Archive completed C",
            MinWidth = 142,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Safely move every fully-uploaded C: quiz project to Z:, reusing only completely verified identical archive copies",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(64, 190, 255));
        button.Click += async (_, _) => await ArchiveCompletedCQuizProjectsAsync(button);
        actions.Children.Insert(insertIndex, button);
        _quizHistoryBulkArchiveButton = button;
    }

    private async Task ArchiveCompletedCQuizProjectsAsync(Button button)
    {
        var settings = _data.LoadSettings();
        if (!settings.ArchiveAfterUpload)
        {
            MessageBox.Show(
                this,
                "Enable 'Move a quiz project to the NAS after all of its required uploads are complete' in Settings → General first.",
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.NasArchiveFolder))
        {
            MessageBox.Show(
                this,
                "Choose the Z:/archive folder in Settings → General first.",
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        button.IsEnabled = false;
        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = "Scanning completed C: quiz projects and verifying Z: copies...";

        QuizCompletedCArchivePreview preview;
        try
        {
            preview = await Task.Run(_data.PreviewCompletedCQuizProjects);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: archive scan failed";
            MessageBox.Show(
                this,
                "The C: archive scan could not be completed. No files were changed.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            button.IsEnabled = true;
            return;
        }

        if (preview.ReadyProjects == 0)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "No completed C: projects are ready to archive";
            MessageBox.Show(
                this,
                BuildCompletedCArchivePreviewText(preview, includeReadyItems: false) +
                "\n\nNothing is ready to move. Projects with outstanding required uploads remain on C:.",
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            button.IsEnabled = true;
            return;
        }

        var confirmationText = BuildCompletedCArchivePreviewText(preview, includeReadyItems: true) +
            "\n\nFor every READY project Factburst will:\n" +
            "• recheck that all required uploads are complete\n" +
            "• reuse an existing Z: folder only when the complete relative file list and every file size are identical\n" +
            "• otherwise copy into Z:\\FactVaultManager\\Quizzes using a collision-safe folder name\n" +
            "• verify the Z: copy before changing Quiz History\n" +
            "• update Quiz History before deleting anything from C:\n" +
            "• delete the C: project only after the previous steps succeed\n\n" +
            "Existing Z: folders are never overwritten or deleted.\n\nProceed with all READY projects?";

        var confirmation = MessageBox.Show(
            this,
            confirmationText,
            "Archive completed C projects",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: archive cancelled; no files changed";
            button.IsEnabled = true;
            return;
        }

        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = $"Archiving {preview.ReadyProjects} completed C: quiz project(s)...";

        try
        {
            var result = await Task.Run(_data.ArchiveCompletedCQuizProjects);
            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null)
            {
                _quizHistoryAnalyticsStatusText.Text = result.Failed == 0 && result.CleanupWarnings == 0
                    ? $"C: archive complete: {result.SourceFoldersRemoved} local project(s) removed"
                    : $"C: archive finished: {result.Succeeded} succeeded, {result.Failed} failed, {result.CleanupWarnings} cleanup warning(s)";
            }

            MessageBox.Show(
                this,
                BuildCompletedCArchiveResultText(result),
                "Archive completed C projects",
                MessageBoxButton.OK,
                result.Failed == 0 && result.CleanupWarnings == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: archive stopped; see error";
            MessageBox.Show(
                this,
                "The bulk archive operation stopped unexpectedly. Any project is deleted from C: only after its own Z: copy and Quiz History update have succeeded.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string BuildCompletedCArchivePreviewText(
        QuizCompletedCArchivePreview preview,
        bool includeReadyItems)
    {
        var text = new StringBuilder();
        text.AppendLine("C: QUIZ ARCHIVE PREVIEW");
        text.AppendLine(new string('=', 48));
        text.AppendLine($"Existing C: quiz projects:       {preview.ExistingCProjects}");
        text.AppendLine($"Ready to archive:                {preview.ReadyProjects}");
        text.AppendLine($"  Reuse verified Z: copies:      {preview.ReuseVerifiedCopies}");
        text.AppendLine($"  Copy new verified Z: projects: {preview.CopyNewProjects}");
        text.AppendLine($"Left on C: - uploads outstanding:{preview.BlockedByUploads,3}");
        text.AppendLine($"Left on C: - safety checks:      {preview.SafetySkipped}");

        if (includeReadyItems && preview.ReadyItems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("READY:");
            foreach (var item in preview.ReadyItems.Take(12))
            {
                var action = item.Action == QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy
                    ? "reuse verified Z copy"
                    : "copy + verify";
                text.AppendLine($"• #{item.HistoryId} {item.Label} — {action}");
            }
            if (preview.ReadyItems.Count > 12)
                text.AppendLine($"• ...and {preview.ReadyItems.Count - 12} more");
        }

        if (preview.SkippedItems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("LEFT ON C: FOR NOW:");
            foreach (var item in preview.SkippedItems.Take(8))
                text.AppendLine($"• #{item.HistoryId} {item.Label} — {item.Reason}");
            if (preview.SkippedItems.Count > 8)
                text.AppendLine($"• ...and {preview.SkippedItems.Count - 8} more");
        }

        return text.ToString().TrimEnd();
    }

    private static string BuildCompletedCArchiveResultText(QuizCompletedCArchiveApplyResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("C: QUIZ ARCHIVE RESULT");
        text.AppendLine(new string('=', 48));
        text.AppendLine($"Ready at final recheck: {result.ReadyAtStart}");
        text.AppendLine($"Succeeded:              {result.Succeeded}");
        text.AppendLine($"  Reused Z: copies:     {result.ReusedExistingCopies}");
        text.AppendLine($"  Copied new to Z:      {result.CopiedNewProjects}");
        text.AppendLine($"C: folders removed:     {result.SourceFoldersRemoved}");
        text.AppendLine($"Cleanup warnings:       {result.CleanupWarnings}");
        text.AppendLine($"Failed/kept on C:       {result.Failed}");

        var attention = result.Results
            .Where(item => !item.Succeeded || !item.SourceDeleted)
            .Take(10)
            .ToList();
        if (attention.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("NEEDS ATTENTION:");
            foreach (var item in attention)
                text.AppendLine($"• #{item.HistoryId} {item.Label}: {item.Message}");
        }

        text.AppendLine();
        text.AppendLine("Existing Z: folders were not overwritten or deleted.");
        return text.ToString().TrimEnd();
    }
}
