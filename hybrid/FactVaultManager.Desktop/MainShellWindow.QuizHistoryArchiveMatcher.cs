using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryArchiveMatcherUiRegistered;
    private Button? _quizHistoryArchiveMatcherButton;

    public void InitializeQuizHistoryArchiveMatcherUi()
    {
        if (_quizHistoryArchiveMatcherUiRegistered)
            return;

        _quizHistoryArchiveMatcherUiRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryArchiveMatcherButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(EnsureQuizHistoryArchiveMatcherButton));
    }

    private void EnsureQuizHistoryArchiveMatcherButton()
    {
        if (_quizHistoryArchiveMatcherButton is not null ||
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

        var button = new Button
        {
            Content = "Match archive",
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Audit and match existing folders in the configured Z: quiz archive back to Quiz History without moving or deleting files",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(255, 202, 45));
        button.Click += async (_, _) => await MatchQuizHistoryArchiveAsync(button);

        var deleteButton = actions.Children
            .OfType<Button>()
            .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), "Delete", StringComparison.Ordinal));
        var deleteIndex = deleteButton is null ? actions.Children.Count : actions.Children.IndexOf(deleteButton);
        actions.Children.Insert(Math.Max(0, deleteIndex), button);
        _quizHistoryArchiveMatcherButton = button;
    }

    private async Task MatchQuizHistoryArchiveAsync(Button button)
    {
        button.IsEnabled = false;
        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = "Auditing Z: archive against Quiz History...";

        try
        {
            var preview = await Task.Run(_data.PreviewQuizArchiveMatches);
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    $"Archive audit: {preview.UnlinkedArchiveFolders.Count} unlinked folders, " +
                    $"{preview.ExistingPathArchiveMatches.Count} local records with a Z: copy, " +
                    $"{preview.ReadyToMatch} ready";

            var applyReadyMatches = ShowQuizArchiveAudit(preview);
            if (!applyReadyMatches || preview.ReadyToMatch == 0)
                return;

            var confirmation = MessageBox.Show(
                this,
                $"Update {preview.ReadyToMatch} Quiz History record(s) whose current project folder is missing?\n\n" +
                "Only the database path will be changed to the existing Z: archive folder.\n" +
                "Records that still have an existing C:/other folder are audit-only and will NOT be changed.\n\n" +
                "No files will be copied, moved, renamed, overwritten, or deleted.",
                "Apply Archive Matches",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "Updating recovered Quiz History archive paths...";

            var result = await Task.Run(() => _data.ApplyQuizArchiveMatches(preview.Matches));
            RefreshQuizHistory();
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    $"Archive paths: {result.Updated} updated, {result.Skipped} skipped";

            MessageBox.Show(
                this,
                $"Matched {result.Updated} Quiz History record(s) to existing archive folders.\n\n" +
                $"Skipped because the record or folder changed during the scan: {result.Skipped}\n\n" +
                "No files were moved or deleted.",
                "Match Quiz Archive",
                MessageBoxButton.OK,
                result.Skipped == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "Archive audit failed; no files were changed";
            MessageBox.Show(
                this,
                error.Message,
                "Match Quiz Archive",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private bool ShowQuizArchiveAudit(QuizArchiveMatchPreview preview)
    {
        var report = BuildQuizArchiveAuditReport(preview);
        var applyReadyMatches = false;
        var dialog = new Window
        {
            Owner = this,
            Title = "Quiz Archive Audit",
            Width = 940,
            Height = 720,
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock
        {
            Text = "Quiz Archive Audit",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Read-only audit. Existing C:/other paths are never replaced automatically and no files are moved or deleted.",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var reportBox = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(12),
            Background = Brushes.White,
        };
        Grid.SetRow(reportBox, 1);
        root.Children.Add(reportBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var copyButton = new Button
        {
            Content = "Copy report",
            MinWidth = 105,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(report);
            }
            catch
            {
                // Copy is optional; the audit remains visible if the clipboard is unavailable.
            }
        };
        buttons.Children.Add(copyButton);

        if (preview.ReadyToMatch > 0)
        {
            var applyButton = new Button
            {
                Content = $"Apply {preview.ReadyToMatch} ready match{(preview.ReadyToMatch == 1 ? "" : "es")}",
                MinWidth = 150,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
            };
            applyButton.Click += (_, _) =>
            {
                applyReadyMatches = true;
                dialog.Close();
            };
            buttons.Children.Add(applyButton);
        }

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 90,
            Padding = new Thickness(14, 7, 14, 7),
            IsDefault = true,
            IsCancel = true,
        };
        closeButton.Click += (_, _) => dialog.Close();
        buttons.Children.Add(closeButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.ShowDialog();
        return applyReadyMatches;
    }

    private static string BuildQuizArchiveAuditReport(QuizArchiveMatchPreview preview)
    {
        var report = new StringBuilder();
        report.AppendLine("SUMMARY");
        report.AppendLine(new string('=', 72));
        report.AppendLine($"Archive folders found:                         {preview.ArchiveFolders}");
        report.AppendLine($"Quiz History entries:                         {preview.HistoryEntries}");
        report.AppendLine($"Already linked to Z:                          {preview.AlreadyLinked}");
        report.AppendLine($"Existing C:/other paths:                      {preview.LocalPathExists}");
        report.AppendLine($"Unlinked Z: archive folders:                  {preview.UnlinkedArchiveFolders.Count}");
        report.AppendLine($"Existing-path records with confident Z: copy: {preview.ExistingPathArchiveMatches.Count}");
        report.AppendLine($"Missing-path records ready to relink:         {preview.ReadyToMatch}");
        report.AppendLine($"Ambiguous missing-path records:               {preview.Ambiguous}");
        report.AppendLine($"Unmatched missing-path records:               {preview.Unmatched}");
        report.AppendLine();

        AppendAuditSection(
            report,
            $"UNLINKED Z: ARCHIVE FOLDERS ({preview.UnlinkedArchiveFolders.Count})",
            preview.UnlinkedArchiveFolders.Select(folder =>
                $"• {Path.GetFileName(folder)}\n  {folder}"));

        AppendAuditSection(
            report,
            $"EXISTING C:/OTHER PATH + CONFIDENT Z: COPY ({preview.ExistingPathArchiveMatches.Count})",
            preview.ExistingPathArchiveMatches.Select(match =>
                $"• History #{match.HistoryId}: {match.Label}\n  Current: {match.CurrentFolder}\n  Z copy:  {match.ArchiveFolder}"));

        AppendAuditSection(
            report,
            $"MISSING-PATH RECORDS READY TO RELINK ({preview.Matches.Count})",
            preview.Matches.Select(match =>
                $"• History #{match.HistoryId}: {match.Label}\n  Z match: {match.ArchiveFolder}"));

        AppendAuditSection(
            report,
            $"AMBIGUOUS MISSING-PATH RECORDS ({preview.AmbiguousEntries.Count})",
            preview.AmbiguousEntries.Select(entry =>
                $"• History #{entry.HistoryId}: {entry.Label}\n" +
                $"  Stored path: {AuditCurrentPath(entry.CurrentFolder)}\n" +
                (entry.CandidateFolders.Count == 0
                    ? "  Candidates: none"
                    : "  Candidates:\n" + string.Join("\n", entry.CandidateFolders.Select(folder => $"    - {folder}")))));

        AppendAuditSection(
            report,
            $"UNMATCHED QUIZ HISTORY RECORDS ({preview.UnmatchedEntries.Count})",
            preview.UnmatchedEntries.Select(entry =>
                $"• History #{entry.HistoryId}: {entry.Label}\n  Stored path: {AuditCurrentPath(entry.CurrentFolder)}"));

        report.AppendLine("SAFETY");
        report.AppendLine(new string('=', 72));
        report.AppendLine("This audit does not copy, move, rename, overwrite, or delete files.");
        report.AppendLine("Records whose current C:/other folder still exists are report-only.");
        report.AppendLine("Only missing-path 'ready' records can be explicitly relinked.");
        return report.ToString();
    }

    private static void AppendAuditSection(StringBuilder report, string title, IEnumerable<string> items)
    {
        report.AppendLine(title);
        report.AppendLine(new string('-', 72));
        var values = items.ToList();
        if (values.Count == 0)
        {
            report.AppendLine("(none)");
        }
        else
        {
            foreach (var value in values)
            {
                report.AppendLine(value);
                report.AppendLine();
            }
        }
        report.AppendLine();
    }

    private static string AuditCurrentPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "(none stored)" : path;
}
