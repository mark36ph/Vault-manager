using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
            ToolTip = "Deep-audit existing folders in the configured Z: quiz archive and safely relink Quiz History paths",
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
            _quizHistoryAnalyticsStatusText.Text = "Deep-auditing Z: archive against Quiz History...";

        try
        {
            var preview = await Task.Run(_data.PreviewQuizArchiveMatches);
            var exact = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.Exact);
            var high = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.High);
            var possible = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.Possible);
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    $"Archive audit: {exact} exact, {high} high, {possible} possible, " +
                    $"{preview.ConfidentRelinks.Count} safe relink{(preview.ConfidentRelinks.Count == 1 ? "" : "s")}";

            ShowQuizArchiveAudit(preview);
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

    private void ShowQuizArchiveAudit(QuizArchiveMatchPreview preview)
    {
        var report = BuildQuizArchiveAuditReport(preview);
        var dialog = new Window
        {
            Owner = this,
            Title = "Quiz Archive Audit",
            Width = 1180,
            Height = 760,
            MinWidth = 860,
            MinHeight = 560,
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
            Text = "Exact/High one-to-one matches can be relinked safely. Relinking changes only the Quiz History database path; existing C: folders are left in place and no archive files are moved, renamed, overwritten, or deleted.",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var tabs = new TabControl();
        var matchesGrid = BuildQuizArchiveAuditGrid();
        matchesGrid.ItemsSource = preview.FolderAudits;
        var matchesTab = new TabItem { Header = $"Archive folders ({preview.FolderAudits.Count})", Content = matchesGrid };
        tabs.Items.Add(matchesTab);

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
        tabs.Items.Add(new TabItem { Header = "Full report", Content = reportBox });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

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

        Button? relinkConfidentButton = null;
        if (preview.ConfidentRelinks.Count > 0)
        {
            relinkConfidentButton = new Button
            {
                Content = $"Relink confident Z copies ({preview.ConfidentRelinks.Count})",
                MinWidth = 205,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
            };
            buttons.Children.Add(relinkConfidentButton);
        }

        var relinkSelectedButton = new Button
        {
            Content = "Relink selected",
            MinWidth = 125,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
        };
        buttons.Children.Add(relinkSelectedButton);

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

        matchesGrid.SelectionChanged += (_, _) =>
        {
            relinkSelectedButton.IsEnabled = matchesGrid.SelectedItem is QuizArchiveFolderAudit { HasSuggestion: true };
        };

        if (relinkConfidentButton is not null)
        {
            relinkConfidentButton.Click += async (_, _) =>
            {
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
                relinkSelectedButton.IsEnabled = false;
                try
                {
                    var result = await Task.Run(() => _data.ApplyQuizArchiveRelinks(
                        preview.ConfidentRelinks,
                        allowExistingPaths: true));
                    RefreshQuizHistory();
                    if (_quizHistoryAnalyticsStatusText is not null)
                        _quizHistoryAnalyticsStatusText.Text = $"Archive relink: {result.Updated} updated, {result.Skipped} skipped";
                    dialog.Close();
                    MessageBox.Show(
                        this,
                        $"Relinked {result.Updated} Quiz History record(s) to Z:.\n\n" +
                        $"Skipped because a path, record, or ownership check changed: {result.Skipped}\n\n" +
                        "Existing C: folders were left untouched. No files were moved or deleted.",
                        "Relink Confident Z Copies",
                        MessageBoxButton.OK,
                        result.Skipped == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                catch (Exception error)
                {
                    MessageBox.Show(dialog, error.Message, "Relink Confident Z Copies", MessageBoxButton.OK, MessageBoxImage.Error);
                    relinkConfidentButton.IsEnabled = true;
                }
            };
        }

        relinkSelectedButton.Click += async (_, _) =>
        {
            if (matchesGrid.SelectedItem is not QuizArchiveFolderAudit selected || !selected.HasSuggestion)
                return;

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
                RefreshQuizHistory();
                if (_quizHistoryAnalyticsStatusText is not null)
                    _quizHistoryAnalyticsStatusText.Text = $"Archive relink: {result.Updated} updated, {result.Skipped} skipped";
                dialog.Close();
                MessageBox.Show(
                    this,
                    result.Updated == 1
                        ? "Quiz History now points to the selected Z: archive folder.\n\nThe previous C:/other folder, if it exists, was left untouched."
                        : "The relink was skipped because the record, path, or archive ownership changed since the audit.",
                    "Relink Selected Archive Folder",
                    MessageBoxButton.OK,
                    result.Updated == 1 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, error.Message, "Relink Selected Archive Folder", MessageBoxButton.OK, MessageBoxImage.Error);
                relinkSelectedButton.IsEnabled = selected.HasSuggestion;
                if (relinkConfidentButton is not null)
                    relinkConfidentButton.IsEnabled = true;
            }
        };

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static DataGrid BuildQuizArchiveAuditGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Confidence",
            Binding = new Binding(nameof(QuizArchiveFolderAudit.ConfidenceDisplay)),
            Width = 115,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Z: archive folder",
            Binding = new Binding(nameof(QuizArchiveFolderAudit.ArchiveName)),
            Width = new DataGridLength(1.35, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Suggested Quiz History",
            Binding = new Binding(nameof(QuizArchiveFolderAudit.SuggestedQuiz)),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Current path",
            Binding = new Binding(nameof(QuizArchiveFolderAudit.CurrentFolderDisplay)),
            Width = new DataGridLength(1.7, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Evidence",
            Binding = new Binding(nameof(QuizArchiveFolderAudit.EvidenceDisplay)),
            Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
        });
        return grid;
    }

    private static string BuildQuizArchiveAuditReport(QuizArchiveMatchPreview preview)
    {
        var exact = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.Exact);
        var high = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.High);
        var possible = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.Possible);
        var noMatch = preview.FolderAudits.Count(item => item.Confidence == QuizArchiveMatchConfidence.NoMatch);
        var report = new StringBuilder();
        report.AppendLine("SUMMARY");
        report.AppendLine(new string('=', 88));
        report.AppendLine($"Archive folders found:                            {preview.ArchiveFolders}");
        report.AppendLine($"Quiz History entries:                            {preview.HistoryEntries}");
        report.AppendLine($"Already linked to Z:                             {preview.AlreadyLinked}");
        report.AppendLine($"Existing C:/other paths:                         {preview.LocalPathExists}");
        report.AppendLine($"Unlinked Z: archive folders audited:             {preview.FolderAudits.Count}");
        report.AppendLine($"Exact folder recommendations:                    {exact}");
        report.AppendLine($"High-confidence folder recommendations:          {high}");
        report.AppendLine($"Possible folder recommendations:                 {possible}");
        report.AppendLine($"No-match archive folders:                        {noMatch}");
        report.AppendLine($"Unique Exact/High relinks available:             {preview.ConfidentRelinks.Count}");
        report.AppendLine($"Existing-path records with confident Z: copy:    {preview.ExistingPathArchiveMatches.Count}");
        report.AppendLine($"Missing-path records ready to relink:            {preview.ReadyToMatch}");
        report.AppendLine($"Ambiguous missing-path records:                  {preview.Ambiguous}");
        report.AppendLine($"Unmatched missing-path records:                  {preview.Unmatched}");
        report.AppendLine();

        AppendAuditSection(
            report,
            $"DEEP Z: ARCHIVE FOLDER AUDIT ({preview.FolderAudits.Count})",
            preview.FolderAudits.Select(item =>
                $"• [{item.ConfidenceDisplay} | score {item.Score}] {item.ArchiveName}\n" +
                $"  Suggested: {(item.HistoryId.HasValue ? $"History #{item.HistoryId}: {item.HistoryLabel}" : "no Quiz History match")}\n" +
                $"  Current:   {item.CurrentFolderDisplay}\n" +
                $"  Z folder:  {item.ArchiveFolder}\n" +
                $"  Evidence:  {item.EvidenceDisplay}"));

        AppendAuditSection(
            report,
            $"UNIQUE EXACT/HIGH RELINKS ({preview.ConfidentRelinks.Count})",
            preview.ConfidentRelinks.Select(match =>
                $"• [{QuizArchiveDeepMatcher.ConfidenceDisplay(match.Confidence)}] History #{match.HistoryId}: {match.Label}\n" +
                $"  Current: {AuditCurrentPath(match.ExpectedCurrentFolder)}\n" +
                $"  Z match: {match.ArchiveFolder}"));

        AppendAuditSection(
            report,
            $"MISSING-PATH RECORDS NOT SAFE FOR AUTOMATIC RELINK ({preview.AmbiguousEntries.Count})",
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
        report.AppendLine(new string('=', 88));
        report.AppendLine("The audit itself is read-only.");
        report.AppendLine("'Relink confident Z copies' changes only unique Exact/High Quiz History database paths.");
        report.AppendLine("'Relink selected' requires explicit confirmation and can approve a Possible/ambiguous suggestion.");
        report.AppendLine("Existing C:/other folders remain on disk after a relink.");
        report.AppendLine("No archive action here copies, moves, renames, overwrites, or deletes files.");
        return report.ToString();
    }

    private static void AppendAuditSection(StringBuilder report, string title, IEnumerable<string> items)
    {
        report.AppendLine(title);
        report.AppendLine(new string('-', 88));
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
