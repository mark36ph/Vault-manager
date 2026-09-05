using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // Disabled by default because the grouped archive workflow owns this safety decision.
    // Keep the diagnostic implementation available without making the compiler treat the
    // handler body as unreachable code.
    public static bool DuplicatePathArchiveGateEnabled => false;

    private static readonly bool QuizDuplicatePathArchiveHandlerRegistered = RegisterQuizDuplicatePathArchiveHandler();

    private static bool RegisterQuizDuplicatePathArchiveHandler()
    {
        if (!DuplicatePathArchiveGateEnabled)
            return true;

        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(QuizDuplicatePathArchiveButton_Click), handledEventsToo: true);
        return true;
    }

    private static async void QuizDuplicatePathArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !string.Equals(button.Content?.ToString(), "Archive completed C", StringComparison.Ordinal) || Window.GetWindow(button) is not MainShellWindow owner) return;
        e.Handled = true;
        await owner.ArchiveCompletedCWithDuplicateRepairAsync(button);
    }

    private async Task ArchiveCompletedCWithDuplicateRepairAsync(Button button)
    {
        if (!button.IsEnabled) return;
        button.IsEnabled = false;
        if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = "Checking C: project links for duplicate Quiz History paths...";
        QuizDuplicatePathRepairPreview duplicatePreview;
        try { duplicatePreview = await Task.Run(_data.PreviewDuplicateQuizHistoryProjectFolders); }
        catch (Exception error)
        {
            button.IsEnabled = true;
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = "Duplicate C: path check failed; no files changed";
            MessageBox.Show(this, "The duplicate-path safety check could not be completed. No files or Quiz History paths were changed.\n\n" + error.Message, "Archive completed C projects", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (duplicatePreview.DuplicateFolders == 0) { button.IsEnabled = true; await ArchiveCompletedCQuizProjectsAsync(button); return; }
        if (duplicatePreview.ConfidentRepairs == 0)
        {
            button.IsEnabled = true;
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = $"{duplicatePreview.DuplicateFolders} shared C: path group(s) need review before archiving";
            MessageBox.Show(this, BuildDuplicatePathRepairPreviewText(duplicatePreview) + "\n\nNo automatic database repair is safe yet, so nothing on C: or Z: was changed. The physical C: folders will remain in place until each shared path has one clear owner.", "Duplicate Quiz History paths", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var confirmation = MessageBox.Show(this, BuildDuplicatePathRepairPreviewText(duplicatePreview) + "\n\nRepair the confident one-to-one Quiz History paths now?\n\nThis step changes database paths only. It does NOT move, copy, rename, overwrite or delete any C: or Z: files. After the repair Factburst will re-scan the shared links before allowing the archive step to continue.", "Repair duplicate Quiz History paths", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            button.IsEnabled = true;
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = "Duplicate path repair cancelled; no files changed";
            return;
        }
        if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = $"Repairing {duplicatePreview.ConfidentRepairs} confident duplicate Quiz History path(s)...";
        QuizDuplicatePathRepairApplyResult repairResult;
        try { repairResult = await Task.Run(() => _data.ApplyDuplicateQuizHistoryProjectFolderRepairs(duplicatePreview.Suggestions)); }
        catch (Exception error)
        {
            button.IsEnabled = true;
            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = "Duplicate path repair stopped; no files were moved";
            MessageBox.Show(this, "The duplicate-path database repair stopped unexpectedly. No project folders were moved or deleted.\n\n" + error.Message, "Repair duplicate Quiz History paths", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshQuizHistory();
        QuizDuplicatePathRepairPreview refreshed;
        try { refreshed = await Task.Run(_data.PreviewDuplicateQuizHistoryProjectFolders); }
        catch (Exception error)
        {
            button.IsEnabled = true;
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = $"Duplicate repair updated {repairResult.Updated}; final safety recheck failed";
            MessageBox.Show(this, BuildDuplicatePathRepairApplyText(repairResult) + "\n\nThe final duplicate-path recheck could not complete, so archiving did not start. No project folders were moved or deleted.\n\n" + error.Message, "Repair duplicate Quiz History paths", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (refreshed.DuplicateFolders > 0)
        {
            button.IsEnabled = true;
            if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = $"Duplicate repair: {repairResult.Updated} updated; {refreshed.DuplicateFolders} shared C: group(s) remain";
            MessageBox.Show(this, BuildDuplicatePathRepairApplyText(repairResult) + "\n\nA re-scan still found shared C: project paths, so the archive step has NOT started.\n\n" + BuildDuplicatePathRepairPreviewText(refreshed), "Duplicate paths still need review", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_quizHistoryAnalyticsStatusText is not null) _quizHistoryAnalyticsStatusText.Text = $"Duplicate paths repaired: {repairResult.Updated} updated; opening archive preview...";
        button.IsEnabled = true;
        await ArchiveCompletedCQuizProjectsAsync(button);
    }

    private static string BuildDuplicatePathRepairPreviewText(QuizDuplicatePathRepairPreview preview)
    {
        var text = new StringBuilder();
        text.AppendLine("DUPLICATE QUIZ HISTORY PATH CHECK"); text.AppendLine(new string('=', 54));
        text.AppendLine($"Shared physical C: project folders: {preview.DuplicateFolders}"); text.AppendLine($"Quiz History rows involved:       {preview.DuplicateRows}"); text.AppendLine($"Safe one-to-one path repairs:     {preview.ConfidentRepairs}");
        if (preview.Suggestions.Count > 0)
        {
            text.AppendLine(); text.AppendLine("SAFE DATABASE-ONLY REPAIRS:");
            foreach (var suggestion in preview.Suggestions.Take(12)) { text.AppendLine($"• #{suggestion.HistoryId} {suggestion.Label}"); text.AppendLine($"  From: {suggestion.CurrentFolder}"); text.AppendLine($"  To:   {suggestion.ProposedFolder}"); text.AppendLine($"  {QuizArchiveDeepMatcher.ConfidenceDisplay(suggestion.Confidence)} score {suggestion.Score}: {suggestion.Evidence}"); }
            if (preview.Suggestions.Count > 12) text.AppendLine($"• ...and {preview.Suggestions.Count - 12} more safe repair(s)");
        }
        if (preview.Conflicts.Count > 0)
        {
            text.AppendLine(); text.AppendLine("NOT CHANGED AUTOMATICALLY:");
            foreach (var conflict in preview.Conflicts.Take(12)) { var ids = conflict.HistoryIds.Count == 0 ? "" : " [" + string.Join(", ", conflict.HistoryIds.Select(id => $"#{id}")) + "]"; text.AppendLine($"• {Path.GetFileName(conflict.SourceFolder)}{ids} — {conflict.Reason}"); }
            if (preview.Conflicts.Count > 12) text.AppendLine($"• ...and {preview.Conflicts.Count - 12} more unresolved item(s)");
        }
        return text.ToString().TrimEnd();
    }

    private static string BuildDuplicatePathRepairApplyText(QuizDuplicatePathRepairApplyResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("DUPLICATE PATH REPAIR RESULT"); text.AppendLine(new string('=', 54));
        text.AppendLine($"Quiz History paths updated: {result.Updated}"); text.AppendLine($"Skipped at final safety check: {result.Skipped}"); text.AppendLine("Project folders moved/deleted: 0");
        if (result.Details.Count > 0) { text.AppendLine(); foreach (var detail in result.Details.Take(12)) text.AppendLine("• " + detail); if (result.Details.Count > 12) text.AppendLine($"• ...and {result.Details.Count - 12} more detail(s)"); }
        return text.ToString().TrimEnd();
    }
}
