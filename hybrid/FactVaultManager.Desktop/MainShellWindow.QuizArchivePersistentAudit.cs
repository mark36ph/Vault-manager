using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static bool _quizArchivePersistentAuditHandlerRegistered;
    private static readonly object QuizArchivePersistentAuditHandlerLock = new();

    public void InitializeQuizArchivePersistentAudit()
    {
        lock (QuizArchivePersistentAuditHandlerLock)
        {
            if (_quizArchivePersistentAuditHandlerRegistered)
                return;

            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.ClickEvent,
                new RoutedEventHandler(QuizArchivePersistentAuditButton_Click),
                handledEventsToo: true);
            _quizArchivePersistentAuditHandlerRegistered = true;
        }
    }

    private static async void QuizArchivePersistentAuditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not Window { Title: "Quiz Archive Audit", Owner: MainShellWindow owner } dialog)
        {
            return;
        }

        var content = button.Content?.ToString() ?? "";
        if (string.Equals(content, "Relink selected", StringComparison.Ordinal))
        {
            e.Handled = true;
            await owner.RelinkSelectedInsideArchiveAuditAsync(dialog, button);
            return;
        }

        if (content.StartsWith("Relink confident Z copies", StringComparison.Ordinal))
        {
            e.Handled = true;
            await owner.RelinkConfidentInsideArchiveAuditAsync(dialog, button);
            return;
        }

        if (string.Equals(content, "Copy report", StringComparison.Ordinal) &&
            dialog.Tag is QuizArchiveMatchPreview currentPreview)
        {
            e.Handled = true;
            try
            {
                Clipboard.SetText(BuildQuizArchiveAuditReport(currentPreview));
            }
            catch
            {
                // Copy is optional; leave the audit open if the clipboard is unavailable.
            }
        }
    }

    private async Task RelinkSelectedInsideArchiveAuditAsync(Window dialog, Button relinkSelectedButton)
    {
        if (!TryGetArchiveAuditControls(dialog, out var matchesGrid, out _, out _, out var relinkConfidentButton) ||
            matchesGrid.SelectedItem is not QuizArchiveFolderAudit selected ||
            !selected.HasSuggestion)
        {
            return;
        }

        var confidence = QuizArchiveDeepMatcher.ConfidenceDisplay(selected.Confidence);
        var warning = selected.IsConfidentRelink
            ? "This is eligible for automatic relinking."
            : "This is NOT eligible for automatic relinking. You are manually approving the suggested match.";
        var confirmation = MessageBox.Show(
            dialog,
            $"Relink this Quiz History record to the selected Z: folder?\n\n" +
            $"Confidence: {confidence}\n" +
            $"Quiz History: #{selected.HistoryId} {selected.HistoryLabel}\n" +
            $"Current path: {selected.CurrentFolderDisplay}\n" +
            $"Z: folder: {selected.ArchiveFolder}\n\n" +
            $"Evidence: {selected.EvidenceDisplay}\n\n" +
            warning + "\n\n" +
            "Only the database path will change. No files will be moved or deleted.",
            "Relink Selected Archive Folder",
            MessageBoxButton.YesNo,
            selected.IsConfidentRelink ? MessageBoxImage.Question : MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        var selectedIndex = Math.Max(0, matchesGrid.SelectedIndex);
        var request = new QuizArchiveRelinkRequest(
            selected.HistoryId!.Value,
            selected.HistoryLabel,
            selected.CurrentFolder,
            selected.ArchiveFolder,
            selected.Confidence);

        relinkSelectedButton.IsEnabled = false;
        if (relinkConfidentButton is not null)
            relinkConfidentButton.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => _data.ApplyQuizArchiveRelinks([request], allowExistingPaths: true));
            if (result.Updated != 1)
            {
                MessageBox.Show(
                    dialog,
                    "The relink was skipped because the record, path, or archive ownership changed since the audit.",
                    "Relink Selected Archive Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    result.Updated == 1
                        ? $"Archive relinked History #{selected.HistoryId} to Z:; audit refreshed"
                        : "Archive relink skipped; audit refreshed";

            var refreshed = await Task.Run(_data.PreviewQuizArchiveMatches);
            RefreshArchiveAuditWindow(dialog, refreshed, selectedIndex);
        }
        catch (Exception error)
        {
            MessageBox.Show(dialog, error.Message, "Relink Selected Archive Folder", MessageBoxButton.OK, MessageBoxImage.Error);
            relinkSelectedButton.IsEnabled = selected.HasSuggestion;
            if (relinkConfidentButton is not null)
                relinkConfidentButton.IsEnabled = true;
        }
    }

    private async Task RelinkConfidentInsideArchiveAuditAsync(Window dialog, Button relinkConfidentButton)
    {
        if (!TryGetArchiveAuditControls(dialog, out _, out _, out _, out _))
            return;

        // Always rescan before a batch relink so the button never acts on stale matches after
        // one or more individual relinks were completed in this same audit window.
        var preview = await Task.Run(_data.PreviewQuizArchiveMatches);
        if (preview.ConfidentRelinks.Count == 0)
        {
            RefreshArchiveAuditWindow(dialog, preview, 0);
            return;
        }

        var currentPathCount = preview.ConfidentRelinks.Count(item =>
            !string.IsNullOrWhiteSpace(item.ExpectedCurrentFolder) && Directory.Exists(item.ExpectedCurrentFolder));
        var missingPathCount = preview.ConfidentRelinks.Count - currentPathCount;
        var confirmation = MessageBox.Show(
            dialog,
            $"Relink {preview.ConfidentRelinks.Count} unique Exact/High archive match{(preview.ConfidentRelinks.Count == 1 ? "" : "es")} to Z:?\n\n" +
            $"Existing C:/other paths that will be replaced in Quiz History: {currentPathCount}\n" +
            $"Missing paths that will be recovered: {missingPathCount}\n\n" +
            "Only the database path will change. Any existing C: folder will remain on disk unchanged.\n" +
            "No files will be copied, moved, renamed, overwritten, or deleted.",
            "Relink Confident Z Copies",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        relinkConfidentButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => _data.ApplyQuizArchiveRelinks(
                preview.ConfidentRelinks,
                allowExistingPaths: true));
            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    $"Archive relink: {result.Updated} updated, {result.Skipped} skipped; audit refreshed";

            var refreshed = await Task.Run(_data.PreviewQuizArchiveMatches);
            RefreshArchiveAuditWindow(dialog, refreshed, 0);

            if (result.Skipped > 0)
            {
                MessageBox.Show(
                    dialog,
                    $"Relinked {result.Updated} Quiz History record(s) to Z:.\n\n" +
                    $"Skipped because a path, record, or ownership check changed: {result.Skipped}\n\n" +
                    "The audit has refreshed and remains open. Existing C: folders were left untouched.",
                    "Relink Confident Z Copies",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(dialog, error.Message, "Relink Confident Z Copies", MessageBoxButton.OK, MessageBoxImage.Error);
            relinkConfidentButton.IsEnabled = true;
        }
    }

    private static void RefreshArchiveAuditWindow(Window dialog, QuizArchiveMatchPreview preview, int preferredIndex)
    {
        if (!TryGetArchiveAuditControls(dialog, out var matchesGrid, out var matchesTab, out var reportBox, out var relinkConfidentButton))
            return;

        dialog.Tag = preview;
        matchesGrid.ItemsSource = preview.FolderAudits;
        matchesTab.Header = $"Archive folders ({preview.FolderAudits.Count})";
        reportBox.Text = BuildQuizArchiveAuditReport(preview);

        if (relinkConfidentButton is not null)
        {
            relinkConfidentButton.Content = $"Relink confident Z copies ({preview.ConfidentRelinks.Count})";
            relinkConfidentButton.IsEnabled = preview.ConfidentRelinks.Count > 0;
        }

        if (preview.FolderAudits.Count == 0)
        {
            matchesGrid.SelectedItem = null;
            return;
        }

        var nextIndex = Math.Clamp(preferredIndex, 0, preview.FolderAudits.Count - 1);
        matchesGrid.SelectedIndex = nextIndex;
        matchesGrid.ScrollIntoView(matchesGrid.Items[nextIndex]);
    }

    private static bool TryGetArchiveAuditControls(
        Window dialog,
        out DataGrid matchesGrid,
        out TabItem matchesTab,
        out TextBox reportBox,
        out Button? relinkConfidentButton)
    {
        matchesGrid = null!;
        matchesTab = null!;
        reportBox = null!;
        relinkConfidentButton = null;

        if (dialog.Content is not Grid root)
            return false;

        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs is null || tabs.Items.Count < 2 ||
            tabs.Items[0] is not TabItem firstTab || firstTab.Content is not DataGrid grid ||
            tabs.Items[1] is not TabItem secondTab || secondTab.Content is not TextBox report)
        {
            return false;
        }

        var footer = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any());
        relinkConfidentButton = footer?.Children
            .OfType<Button>()
            .FirstOrDefault(candidate =>
                (candidate.Content?.ToString() ?? "").StartsWith("Relink confident Z copies", StringComparison.Ordinal));

        matchesGrid = grid;
        matchesTab = firstTab;
        reportBox = report;
        return true;
    }
}
