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
            Content = "Match archive",
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Match existing folders in the configured Z: quiz archive back to Quiz History without moving or deleting files",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(255, 202, 45));
        button.Click += async (_, _) => await MatchQuizHistoryArchiveAsync(button);
        actions.Children.Add(button);
        _quizHistoryArchiveMatcherButton = button;
    }

    private async Task MatchQuizHistoryArchiveAsync(Button button)
    {
        button.IsEnabled = false;
        if (_quizHistoryAnalyticsStatusText is not null)
            _quizHistoryAnalyticsStatusText.Text = "Scanning archive for Quiz History matches...";

        try
        {
            var preview = await Task.Run(_data.PreviewQuizArchiveMatches);
            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text =
                    $"Archive scan: {preview.ReadyToMatch} ready, {preview.Ambiguous} ambiguous, {preview.Unmatched} unmatched";

            var examples = preview.Matches
                .Take(6)
                .Select(match => $"• {match.Label}\n  {match.ArchiveFolder}")
                .ToList();
            var exampleText = examples.Count == 0
                ? ""
                : "\n\nExamples:\n" + string.Join("\n", examples) +
                  (preview.Matches.Count > examples.Count ? "\n  ..." : "");

            var summary =
                $"Archive folders found: {preview.ArchiveFolders}\n" +
                $"Quiz History entries: {preview.HistoryEntries}\n" +
                $"Already linked to Z: {preview.AlreadyLinked}\n" +
                $"Still have an existing local/other path: {preview.LocalPathExists}\n" +
                $"Ready to match: {preview.ReadyToMatch}\n" +
                $"Ambiguous - left unchanged: {preview.Ambiguous}\n" +
                $"Unmatched - left unchanged: {preview.Unmatched}";

            if (preview.ReadyToMatch == 0)
            {
                MessageBox.Show(
                    this,
                    summary + "\n\nNo Quiz History paths need changing.",
                    "Match Quiz Archive",
                    MessageBoxButton.OK,
                    preview.Ambiguous == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                summary + exampleText +
                "\n\nUpdate the ready Quiz History records to these existing archive folders?" +
                "\n\nNo files will be copied, moved, renamed, overwritten, or deleted.",
                "Match Quiz Archive",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

            if (_quizHistoryAnalyticsStatusText is not null)
                _quizHistoryAnalyticsStatusText.Text = "Updating matched Quiz History archive paths...";

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
                _quizHistoryAnalyticsStatusText.Text = "Archive matching failed; no files were changed";
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
}
