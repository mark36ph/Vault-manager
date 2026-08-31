using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryGroupedBulkArchiveUiRegistered;
    private Button? _quizHistoryGroupedBulkArchiveButton;

    public void InitializeQuizHistoryGroupedBulkArchiveUi()
    {
        if (_quizHistoryGroupedBulkArchiveUiRegistered)
            return;

        _quizHistoryGroupedBulkArchiveUiRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryGroupedBulkArchiveButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryGroupedBulkArchiveButton));
    }

    private void EnsureQuizHistoryGroupedBulkArchiveButton()
    {
        if (_quizHistoryGroupedBulkArchiveButton is not null ||
            _quizHistoryBulkArchiveButton is null ||
            _quizHistoryBulkArchiveButton.Parent is not StackPanel actions)
        {
            return;
        }

        var oldButton = _quizHistoryBulkArchiveButton;
        var index = actions.Children.IndexOf(oldButton);
        if (index < 0)
            return;

        actions.Children.RemoveAt(index);
        var button = new Button
        {
            Content = "Archive completed C",
            MinWidth = 142,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Archive each completed physical C: project folder once, updating every linked Quiz History row together only after Z: verification",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(64, 190, 255));
        button.Click += async (_, _) => await ArchiveGroupedCompletedCQuizProjectsAsync(button);
        actions.Children.Insert(index, button);
        _quizHistoryGroupedBulkArchiveButton = button;
    }

    private async Task ArchiveGroupedCompletedCQuizProjectsAsync(Button button)
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
            _quizHistoryAnalyticsStatusText.Text = "Reconciling prior links and grouping physical C: project folders...";

        QuizGroupedCArchivePreview preview;
        QuizArchiveJournalReconciliationResult preScanReconciliation;
        try
        {
            preScanReconciliation = await Task.Run(_data.ReconcileJournaledQuizArchivePaths);
            preview = await Task.Run(_data.PreviewGroupedCompletedCQuizProjects);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: grouped archive scan failed";
            MessageBox.Show(
                this,
                "The grouped C: archive scan could not be completed. No project files were changed.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            button.IsEnabled = true;
            return;
        }

        if (preScanReconciliation.Repaired > 0)
            RefreshQuizHistory();

        if (preview.ReadyPhysicalFolders == 0)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = preScanReconciliation.Repaired > 0
                    ? $"Recovered {preScanReconciliation.Repaired} archived link(s); no physical C: folders are ready"
                    : "No completed physical C: project folders are ready to archive";
            MessageBox.Show(
                this,
                BuildGroupedCArchivePreviewText(preview, includeReadyItems: false, preScanReconciliation.Repaired) +
                "\n\nNothing else is ready to move. Any blocked physical folder remains completely untouched on C:.",
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            button.IsEnabled = true;
            return;
        }

        var confirmationText = BuildGroupedCArchivePreviewText(preview, includeReadyItems: true, preScanReconciliation.Repaired) +
            "\n\nFor every READY physical folder Factburst will:\n" +
            "• treat one C: folder as one physical project, even when several Quiz History rows point to it\n" +
            "• recheck that the exact same set of History rows still points to that C: folder\n" +
            "• keep the whole folder on C: if ANY linked History row still has required uploads outstanding\n" +
            "• reuse a same-name Z: copy only when its complete relative file set has a strong verified copy identity\n" +
            "• otherwise copy once to a collision-safe Z:\\FactVaultManager\\Quizzes folder and verify it\n" +
            "• update ALL linked History rows in one SQLite transaction and verify every row before deleting anything\n" +
            "• if the database group update cannot be verified, roll the group back to C: and keep the C: folder\n" +
            "• delete the physical C: folder only after the Z: copy and every linked database path are verified\n\n" +
            "Existing Z: folders are never overwritten or deleted. Shared History rows are preserved; Factburst does not guess which row owns the physical folder.\n\nProceed with all READY physical folders?";

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
                _quizHistoryAnalyticsStatusText.Text = "C: grouped archive cancelled; no project files changed";
            button.IsEnabled = true;
            return;
        }

        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = $"Processing {preview.ReadyPhysicalFolders} physical C: folder(s) covering {preview.ReadyHistoryRows} History row(s)...";

        var progressWindow = CreateCompletedCArchiveProgressWindow(preview.ReadyPhysicalFolders);
        var progress = new Progress<QuizGroupedCArchiveProgress>(update =>
        {
            var compatible = new QuizCompletedCArchiveProgress(
                update.Current,
                update.Total,
                update.HistoryId,
                update.HistoryRowCount > 1 ? $"{update.Label} • {update.HistoryRowCount} History rows" : update.Label,
                update.Stage,
                update.SourceFolder,
                update.DestinationFolder,
                update.ItemCompleted);
            UpdateCompletedCArchiveProgressWindow(progressWindow, compatible);
        });
        progressWindow.Dialog.Show();

        try
        {
            var result = await Task.Run(() => _data.ArchiveGroupedCompletedCQuizProjects(preview, progress));
            RefreshQuizHistory();

            if (_quizHistoryAnalyticsStatusText is not null)
            {
                _quizHistoryAnalyticsStatusText.Text = result.FailedPhysicalFolders == 0 && result.CleanupWarnings == 0
                    ? $"C: archive complete: {result.SourceFoldersRemoved} physical folder(s) removed; {result.SucceededHistoryRows} History row(s) now point to Z:"
                    : $"C: archive finished: {result.SucceededPhysicalFolders} physical folder(s) succeeded, {result.FailedPhysicalFolders} failed, {result.CleanupWarnings} cleanup warning(s)";
            }

            progressWindow.Dialog.Close();
            MessageBox.Show(
                this,
                BuildGroupedCArchiveResultText(result),
                "Archive completed C projects",
                MessageBoxButton.OK,
                result.FailedPhysicalFolders == 0 && result.CleanupWarnings == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            progressWindow.Dialog.Close();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: grouped archive stopped; see error";
            MessageBox.Show(
                this,
                "The grouped archive operation stopped unexpectedly. A physical C: folder is deleted only after its own Z: copy and every linked Quiz History path have been verified.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string BuildGroupedCArchivePreviewText(
        QuizGroupedCArchivePreview preview,
        bool includeReadyItems,
        int preScanReconciled = 0)
    {
        var text = new StringBuilder();
        text.AppendLine("PHYSICAL C: QUIZ ARCHIVE PREVIEW");
        text.AppendLine(new string('=', 52));
        if (preScanReconciled > 0)
            text.AppendLine($"Recovered prior archived Z: links: {preScanReconciled}");
        text.AppendLine($"Physical C: project folders:       {preview.ExistingPhysicalFolders}");
        text.AppendLine($"Quiz History rows on those folders:{preview.ExistingHistoryRows,4}");
        text.AppendLine($"Ready physical folders:            {preview.ReadyPhysicalFolders}");
        text.AppendLine($"Ready Quiz History rows:           {preview.ReadyHistoryRows}");
        text.AppendLine($"  Reuse verified Z: copies:        {preview.ReuseVerifiedCopies}");
        text.AppendLine($"  Copy new verified Z: projects:   {preview.CopyNewProjects}");
        text.AppendLine($"Left on C: - uploads outstanding:  {preview.BlockedByUploads}");
        text.AppendLine($"Left on C: - safety checks:        {preview.SafetySkipped}");

        if (includeReadyItems && preview.ReadyItems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("READY PHYSICAL FOLDERS:");
            foreach (var item in preview.ReadyItems.Take(12))
            {
                var action = item.Action == QuizGroupedCArchiveAction.ReuseVerifiedArchiveCopy
                    ? "reuse verified Z copy"
                    : "copy once + verify";
                text.AppendLine($"• {item.Label} — {action}; {item.HistoryRowCount} History row(s)");
            }
            if (preview.ReadyItems.Count > 12)
                text.AppendLine($"• ...and {preview.ReadyItems.Count - 12} more physical folders");
        }

        if (preview.SkippedItems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("LEFT ON C: FOR NOW:");
            foreach (var item in preview.SkippedItems.Take(10))
                text.AppendLine($"• {item.Label} — {item.Reason}");
            if (preview.SkippedItems.Count > 10)
                text.AppendLine($"• ...and {preview.SkippedItems.Count - 10} more physical folders");
        }

        return text.ToString().TrimEnd();
    }

    private static string BuildGroupedCArchiveResultText(QuizGroupedCArchiveApplyResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("PHYSICAL C: QUIZ ARCHIVE RESULT");
        text.AppendLine(new string('=', 52));
        text.AppendLine($"Confirmed physical folders: {result.ReadyPhysicalFoldersAtStart}");
        text.AppendLine($"Confirmed History rows:      {result.ReadyHistoryRowsAtStart}");
        text.AppendLine($"Succeeded physical folders:  {result.SucceededPhysicalFolders}");
        text.AppendLine($"History rows moved to Z:      {result.SucceededHistoryRows}");
        text.AppendLine($"  Reused Z: copies:           {result.ReusedExistingCopies}");
        text.AppendLine($"  Copied new to Z:            {result.CopiedNewProjects}");
        text.AppendLine($"C: folders removed:           {result.SourceFoldersRemoved}");
        text.AppendLine($"Cleanup warnings:             {result.CleanupWarnings}");
        text.AppendLine($"Failed/kept on C:             {result.FailedPhysicalFolders}");

        var attention = result.Results
            .Where(item => !item.Succeeded || !item.SourceDeleted)
            .Take(10)
            .ToList();
        if (attention.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("NEEDS ATTENTION:");
            foreach (var item in attention)
                text.AppendLine($"• {item.Label}: {item.Message}");
        }

        text.AppendLine();
        text.AppendLine("Existing Z: folders were not overwritten or deleted. Shared Quiz History rows were preserved as one registered physical project group.");
        return text.ToString().TrimEnd();
    }
}
