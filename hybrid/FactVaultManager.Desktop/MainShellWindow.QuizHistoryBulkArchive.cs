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
            ToolTip = "Reconcile previously archived links, then safely move every fully-uploaded C: quiz project to Z: with live progress",
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
            _quizHistoryAnalyticsStatusText.Text = "Reconciling previous archive links and scanning C: projects...";

        QuizCompletedCArchivePreview preview;
        QuizArchiveJournalReconciliationResult preScanReconciliation;
        try
        {
            // Repair only journal-proven fallbacks first. This is DB-only: the History ID, previous
            // C: path, Z: destination and database path must all match the successful archive journal.
            preScanReconciliation = await Task.Run(_data.ReconcileJournaledQuizArchivePaths);
            preview = await Task.Run(_data.PreviewCompletedCQuizProjects);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: archive scan failed";
            MessageBox.Show(
                this,
                "The C: archive scan could not be completed. No project files were changed.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            button.IsEnabled = true;
            return;
        }

        if (preScanReconciliation.Repaired > 0)
            RefreshQuizHistory();

        if (preview.ReadyProjects == 0)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = preScanReconciliation.Repaired > 0
                    ? $"Recovered {preScanReconciliation.Repaired} archived Quiz History link(s); no C: projects are ready"
                    : "No completed C: projects are ready to archive";
            MessageBox.Show(
                this,
                BuildCompletedCArchivePreviewText(preview, includeReadyItems: false, preScanReconciliation.Repaired) +
                "\n\nNothing else is ready to move. Safety-skipped rows remain untouched.",
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            button.IsEnabled = true;
            return;
        }

        var confirmationText = BuildCompletedCArchivePreviewText(preview, includeReadyItems: true, preScanReconciliation.Repaired) +
            "\n\nFor every READY item Factburst will:\n" +
            "• recheck its current Quiz History path immediately before acting\n" +
            "• restore a prior Z: database link only when the archive journal proves the same History ID + exact C: path + exact Z: path\n" +
            "• reuse an existing Z: project only when the folder name and complete relative file set match and at least 80% of file sizes still match\n" +
            "• otherwise copy into Z:\\FactVaultManager\\Quizzes using a collision-safe folder name\n" +
            "• confirm the Quiz History Z: path by reading it back before deleting anything from C:\n" +
            "• delete a C: project only after its own archive checks succeed\n\n" +
            "Existing Z: folders are never overwritten or deleted.\n\nProceed with all READY items?";

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
                _quizHistoryAnalyticsStatusText.Text = "C: archive cancelled; no project files changed";
            button.IsEnabled = true;
            return;
        }

        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = $"Processing {preview.ReadyProjects} completed C: archive item(s)...";

        var progressWindow = CreateCompletedCArchiveProgressWindow(preview.ReadyProjects);
        var progress = new Progress<QuizCompletedCArchiveProgress>(update =>
            UpdateCompletedCArchiveProgressWindow(progressWindow, update));
        progressWindow.Dialog.Show();

        try
        {
            var result = await Task.Run(() => _data.ArchiveCompletedCQuizProjects(preview, progress));

            // Refreshing other Quiz History views should never be allowed to leave a journal-proven
            // archive row pointing back to its old C: location. Reconcile once more after the UI refresh.
            RefreshQuizHistory();
            var afterRefresh = await Task.Run(_data.ReconcileJournaledQuizArchivePaths);
            if (afterRefresh.Repaired > 0)
            {
                result = result with { JournalLinksReconciled = result.JournalLinksReconciled + afterRefresh.Repaired };
                RefreshQuizHistory();
            }

            if (_quizHistoryAnalyticsStatusText is not null)
            {
                _quizHistoryAnalyticsStatusText.Text = result.Failed == 0 && result.CleanupWarnings == 0
                    ? $"C: archive complete: {result.SourceFoldersRemoved} local project(s) removed; {result.RestoredArchivedLinks + result.JournalLinksReconciled} link(s) reconciled"
                    : $"C: archive finished: {result.Succeeded} succeeded, {result.Failed} failed, {result.CleanupWarnings} cleanup warning(s)";
            }

            progressWindow.Dialog.Close();
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
            progressWindow.Dialog.Close();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "C: archive stopped; see error";
            MessageBox.Show(
                this,
                "The bulk archive operation stopped unexpectedly. A C: project is deleted only after its own Z: archive and Quiz History path have been verified.\n\n" + error.Message,
                "Archive completed C projects",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private sealed record CompletedCArchiveProgressWindow(
        Window Dialog,
        ProgressBar ProgressBar,
        TextBlock Counter,
        TextBlock Quiz,
        TextBlock Stage,
        TextBlock Source,
        TextBlock Destination);

    private CompletedCArchiveProgressWindow CreateCompletedCArchiveProgressWindow(int total)
    {
        var dialog = new Window
        {
            Title = "Archiving completed C projects",
            Owner = this,
            Width = 760,
            Height = 390,
            MinWidth = 680,
            MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
        };

        var root = new Grid { Margin = new Thickness(28) };
        for (var index = 0; index < 7; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Moving completed quiz projects to Z:",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        root.Children.Add(heading);

        var counter = new TextBlock
        {
            Text = $"0 of {total}",
            Margin = new Thickness(0, 10, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetRow(counter, 1);
        root.Children.Add(counter);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, total),
            Value = 0,
            Height = 16,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(bar, 2);
        root.Children.Add(bar);

        var quiz = new TextBlock
        {
            Text = "Preparing...",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(quiz, 3);
        root.Children.Add(quiz);

        var stage = new TextBlock
        {
            Text = "Starting archive checks",
            Margin = new Thickness(0, 6, 0, 14),
            Foreground = new SolidColorBrush(Color.FromRgb(70, 235, 115)),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(stage, 4);
        root.Children.Add(stage);

        var source = new TextBlock
        {
            Text = "C: —",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(source, 5);
        root.Children.Add(source);

        var destination = new TextBlock
        {
            Text = "Z: —",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(destination, 6);
        root.Children.Add(destination);

        dialog.Content = root;
        return new CompletedCArchiveProgressWindow(dialog, bar, counter, quiz, stage, source, destination);
    }

    private static void UpdateCompletedCArchiveProgressWindow(
        CompletedCArchiveProgressWindow view,
        QuizCompletedCArchiveProgress update)
    {
        view.Counter.Text = $"{update.Current} of {update.Total}";
        view.ProgressBar.Maximum = Math.Max(1, update.Total);
        view.ProgressBar.Value = update.ItemCompleted
            ? update.Current
            : Math.Max(0, update.Current - 1 + 0.2);
        view.Quiz.Text = $"History #{update.HistoryId} • {update.Label}";
        view.Stage.Text = update.Stage;
        view.Source.Text = "C: " + (string.IsNullOrWhiteSpace(update.SourceFolder) ? "—" : update.SourceFolder);
        view.Destination.Text = "Z: " + (string.IsNullOrWhiteSpace(update.DestinationFolder) ? "—" : update.DestinationFolder);
    }

    private static string BuildCompletedCArchivePreviewText(
        QuizCompletedCArchivePreview preview,
        bool includeReadyItems,
        int preScanReconciled = 0)
    {
        var text = new StringBuilder();
        text.AppendLine("C: QUIZ ARCHIVE PREVIEW");
        text.AppendLine(new string('=', 48));
        if (preScanReconciled > 0)
            text.AppendLine($"Recovered prior archived Z: links:{preScanReconciled,3}");
        text.AppendLine($"Existing C: quiz projects:       {preview.ExistingCProjects}");
        text.AppendLine($"Ready to process:                {preview.ReadyProjects}");
        text.AppendLine($"  Restore journaled Z: links:    {preview.RestoreArchivedLinks}");
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
                var action = item.Action switch
                {
                    QuizCompletedCArchiveAction.RestoreJournaledArchiveLink => "restore verified Z database link (no file move)",
                    QuizCompletedCArchiveAction.ReuseVerifiedArchiveCopy => "reuse verified Z copy",
                    _ => "copy + verify",
                };
                text.AppendLine($"• #{item.HistoryId} {item.Label} — {action}");
            }
            if (preview.ReadyItems.Count > 12)
                text.AppendLine($"• ...and {preview.ReadyItems.Count - 12} more");
        }

        if (preview.SkippedItems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("LEFT ON C: FOR NOW:");
            foreach (var item in preview.SkippedItems.Take(10))
                text.AppendLine($"• #{item.HistoryId} {item.Label} — {item.Reason}");
            if (preview.SkippedItems.Count > 10)
                text.AppendLine($"• ...and {preview.SkippedItems.Count - 10} more");
        }

        return text.ToString().TrimEnd();
    }

    private static string BuildCompletedCArchiveResultText(QuizCompletedCArchiveApplyResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("C: QUIZ ARCHIVE RESULT");
        text.AppendLine(new string('=', 48));
        text.AppendLine($"Confirmed items processed: {result.ReadyAtStart}");
        text.AppendLine($"Succeeded:                 {result.Succeeded}");
        text.AppendLine($"  Restored Z: links:       {result.RestoredArchivedLinks}");
        text.AppendLine($"  Reused Z: copies:        {result.ReusedExistingCopies}");
        text.AppendLine($"  Copied new to Z:         {result.CopiedNewProjects}");
        text.AppendLine($"C: folders removed:        {result.SourceFoldersRemoved}");
        text.AppendLine($"Cleanup warnings:          {result.CleanupWarnings}");
        text.AppendLine($"Failed/kept on C:          {result.Failed}");
        if (result.JournalLinksReconciled > 0)
            text.AppendLine($"Post-run Z: links repaired:{result.JournalLinksReconciled,3}");

        var attention = result.Results
            .Where(item => !item.Succeeded ||
                           (item.Action != QuizCompletedCArchiveAction.RestoreJournaledArchiveLink && !item.SourceDeleted))
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
