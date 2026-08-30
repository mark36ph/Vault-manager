using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static bool _quizArchiveDiagnosticsUiRegistered;
    private static readonly object QuizArchiveDiagnosticsUiLock = new();

    public void InitializeQuizArchiveDiagnosticsUi()
    {
        lock (QuizArchiveDiagnosticsUiLock)
        {
            if (_quizArchiveDiagnosticsUiRegistered)
                return;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(QuizArchiveDiagnosticsWindow_Loaded),
                handledEventsToo: true);
            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.ClickEvent,
                new RoutedEventHandler(QuizArchiveDiagnosticsRelinkButton_Click),
                handledEventsToo: true);
            _quizArchiveDiagnosticsUiRegistered = true;
        }
    }

    private static void QuizArchiveDiagnosticsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window { Title: "Quiz Archive Audit", Owner: MainShellWindow owner } dialog)
            return;

        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => owner.EnsureQuizArchiveDiagnosticsTab(dialog)));
    }

    private void EnsureQuizArchiveDiagnosticsTab(Window dialog)
    {
        if (dialog.Content is not Grid root)
            return;

        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs is null)
            return;

        var existing = tabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Diagnostics", StringComparison.Ordinal));
        if (existing is not null)
            return;

        var panel = new Grid { Margin = new Thickness(12) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var box = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(12),
            Text = "Loading database and ambiguity diagnostics...",
        };
        Grid.SetRow(box, 0);
        panel.Children.Add(box);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var refresh = new Button
        {
            Content = "Refresh diagnostics",
            MinWidth = 135,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
        };
        var copy = new Button
        {
            Content = "Copy diagnostics",
            MinWidth = 125,
            Padding = new Thickness(14, 7, 14, 7),
        };
        refresh.Click += async (_, _) => await RefreshQuizArchiveDiagnosticsAsync(dialog, box, refresh);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(box.Text); }
            catch { }
        };
        actions.Children.Add(refresh);
        actions.Children.Add(copy);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);

        tabs.Items.Add(new TabItem { Header = "Diagnostics", Content = panel });
        _ = RefreshQuizArchiveDiagnosticsAsync(dialog, box, refresh);
    }

    private async Task RefreshQuizArchiveDiagnosticsAsync(Window dialog, TextBox box, Button refresh)
    {
        refresh.IsEnabled = false;
        box.Text = "Refreshing database and ambiguity diagnostics...";
        try
        {
            var diagnostics = await Task.Run(_data.BuildQuizArchiveAuditDiagnostics);
            if (!dialog.IsVisible)
                return;
            box.Text = FormatQuizArchiveDiagnostics(diagnostics);
        }
        catch (Exception error)
        {
            if (dialog.IsVisible)
                box.Text = "Diagnostics failed:\r\n" + error.Message;
        }
        finally
        {
            if (dialog.IsVisible)
                refresh.IsEnabled = true;
        }
    }

    private static string FormatQuizArchiveDiagnostics(QuizArchiveAuditDiagnostics diagnostics)
    {
        var database = diagnostics.Database;
        var report = new StringBuilder();
        report.AppendLine("DATABASE / PERSISTENCE DIAGNOSTIC");
        report.AppendLine(new string('=', 96));
        report.AppendLine($"Primary database:       {database.DatabasePath}");
        report.AppendLine($"Database size:          {database.DatabaseBytes:N0} bytes");
        report.AppendLine($"Database modified:      {database.DatabaseModified}");
        report.AppendLine($"Runtime root:           {database.RuntimeRoot}");
        report.AppendLine($"Data root:              {database.DataRoot}");
        report.AppendLine($"Stored Z: paths:        {database.StoredArchivePaths}");
        report.AppendLine($"Existing Z: paths:      {database.ExistingArchivePaths}");
        report.AppendLine($"Stored Z paths missing: {database.MissingArchivePaths}");
        report.AppendLine();
        report.AppendLine("DATABASE CANDIDATES");
        report.AppendLine(new string('-', 96));
        foreach (var candidate in database.DatabaseCandidates)
            report.AppendLine("• " + candidate);
        report.AppendLine();
        report.AppendLine("RELINK PERSISTENCE WARNINGS");
        report.AppendLine(new string('-', 96));
        if (database.PersistenceWarnings.Count == 0)
        {
            report.AppendLine("(none recorded)");
        }
        else
        {
            foreach (var warning in database.PersistenceWarnings)
                report.AppendLine("• " + warning);
        }
        report.AppendLine();
        report.AppendLine($"AMBIGUOUS ARCHIVE MATCHES ({diagnostics.Ambiguities.Count})");
        report.AppendLine(new string('=', 96));
        if (diagnostics.Ambiguities.Count == 0)
        {
            report.AppendLine("(none)");
        }
        else
        {
            foreach (var ambiguity in diagnostics.Ambiguities)
            {
                report.AppendLine($"• {Path.GetFileName(ambiguity.ArchiveFolder)}");
                report.AppendLine($"  Z folder:   {ambiguity.ArchiveFolder}");
                report.AppendLine($"  Suggested:  History #{ambiguity.SuggestedHistoryId}: {ambiguity.SuggestedLabel} (score {ambiguity.Score})");
                report.AppendLine("  Competing Quiz History rows:");
                if (ambiguity.CompetingHistories.Count == 0)
                    report.AppendLine("    (none within uniqueness margin)");
                else
                    foreach (var competitor in ambiguity.CompetingHistories)
                        report.AppendLine("    - " + competitor);

                report.AppendLine("  Competing Z folders for suggested History row:");
                if (ambiguity.CompetingArchiveFolders.Count == 0)
                    report.AppendLine("    (none within uniqueness margin)");
                else
                    foreach (var folder in ambiguity.CompetingArchiveFolders)
                        report.AppendLine("    - " + folder);
                report.AppendLine();
            }
        }

        report.AppendLine("NOTE");
        report.AppendLine(new string('-', 96));
        report.AppendLine("Diagnostics are read-only. Refresh after relinking if this tab was already open.");
        report.AppendLine("From Build 91 onward, successful selected relinks are journaled outside SQLite so a later rollback can be detected explicitly.");
        return report.ToString();
    }

    private static async void QuizArchiveDiagnosticsRelinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not Window { Title: "Quiz Archive Audit", Owner: MainShellWindow owner } dialog)
        {
            return;
        }

        var content = button.Content?.ToString() ?? "";
        if (!string.Equals(content, "Relink selected", StringComparison.Ordinal))
            return;

        if (!TryCaptureSelectedRelink(dialog, out var request))
            return;

        // The persistent-audit handler performs the actual relink. This watcher only verifies
        // the committed database value afterwards, writes the independent journal, then refreshes
        // the Diagnostics tab. It never changes the archive itself.
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(250);
            try
            {
                var histories = await Task.Run(() => owner._data.GetQuizHistory());
                var current = histories.FirstOrDefault(item => item.Id == request.HistoryId)?.ProjectFolder ?? "";
                if (!PathsEqualForDiagnostic(current, request.ArchiveFolder))
                    continue;

                await Task.Run(() => owner._data.RecordSuccessfulQuizArchiveRelinks([request]));
                owner.RefreshVisibleQuizArchiveDiagnostics(dialog);
                return;
            }
            catch
            {
                return;
            }
        }
    }

    private static bool TryCaptureSelectedRelink(Window dialog, out QuizArchiveRelinkRequest request)
    {
        request = null!;
        if (dialog.Content is not Grid root)
            return false;
        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        if (tabs?.Items.Count < 1 || tabs.Items[0] is not TabItem { Content: DataGrid grid } ||
            grid.SelectedItem is not QuizArchiveFolderAudit selected || !selected.HasSuggestion || !selected.HistoryId.HasValue)
        {
            return false;
        }

        request = new QuizArchiveRelinkRequest(
            selected.HistoryId.GetValueOrDefault(),
            selected.HistoryLabel,
            selected.CurrentFolder,
            selected.ArchiveFolder,
            selected.Confidence);
        return true;
    }

    private void RefreshVisibleQuizArchiveDiagnostics(Window dialog)
    {
        if (!dialog.IsVisible || dialog.Content is not Grid root)
            return;
        var tabs = root.Children.OfType<TabControl>().FirstOrDefault();
        var diagnosticTab = tabs?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Diagnostics", StringComparison.Ordinal));
        if (diagnosticTab?.Content is not Grid panel)
            return;
        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        var refresh = panel.Children.OfType<StackPanel>()
            .SelectMany(stack => stack.Children.OfType<Button>())
            .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), "Refresh diagnostics", StringComparison.Ordinal));
        if (box is null || refresh is null)
            return;
        _ = RefreshQuizArchiveDiagnosticsAsync(dialog, box, refresh);
    }

    private static bool PathsEqualForDiagnostic(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left.Trim()), Path.GetFullPath(right.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
