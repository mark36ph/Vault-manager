using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _uploadManagerThumbnailActionsInitialized;

    internal bool InitializeUploadManagerThumbnailRegenerationActions()
    {
        if (_uploadManagerThumbnailActionsInitialized)
            return true;

        InitializeUploadManagerPage();
        if (_uploadManagerTabIndex < 0 ||
            _uploadManagerTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_uploadManagerTabIndex] is not TabItem { Content: DependencyObject root })
            return false;

        var anchor = FindLogicalButton(root, "Retry Failed Step");
        if (anchor?.Parent is not WrapPanel actions)
            return false;

        var regenerate = new Button
        {
            Content = "Regenerate Thumbnail",
            MinWidth = 142,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Rebuild Thumbnail.png for the selected quiz without changing its video or upload records.",
        };
        StyleQuizHistoryButton(regenerate, Color.FromRgb(70, 235, 115));
        regenerate.Click += (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                RegenerateSelectedQuizThumbnail(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Regenerate Thumbnail",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };

        var regenerateAll = new Button
        {
            Content = "Regenerate All Thumbnails",
            MinWidth = 164,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Rebuild Thumbnail.png for every long-form quiz in Quiz History.",
        };
        StyleQuizHistoryButton(regenerateAll, Color.FromRgb(0, 204, 255));
        regenerateAll.Click += async (_, _) => await RegenerateAllLongFormQuizThumbnailsAsync(regenerateAll);

        var insertionIndex = actions.Children.IndexOf(anchor) + 1;
        actions.Children.Insert(insertionIndex, regenerate);
        actions.Children.Insert(insertionIndex + 1, regenerateAll);
        _uploadManagerThumbnailActionsInitialized = true;
        return true;
    }

    private static Button? FindLogicalButton(DependencyObject root, string content)
    {
        if (root is Button button &&
            string.Equals(Convert.ToString(button.Content), content, StringComparison.Ordinal))
        {
            return button;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject &&
                FindLogicalButton(dependencyObject, content) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void RegenerateSelectedQuizThumbnail(QuizHistorySummary history)
    {
        try
        {
            history = ResolveThumbnailHistoryEntry(history);
            var result = RegenerateHistoricalThumbnail(history, CreateQuizQuestionLookup());
            RefreshUploadManager();

            MessageBox.Show(
                this,
                $"Thumbnail regenerated.\n\n" +
                $"Featured question: {result.FeaturedQuestionNumber} of {result.QuestionCount}\n" +
                $"Hook: {result.Hook}\n\n" +
                $"Saved to:\n{result.ThumbnailPath}\n\n" +
                "The quiz video and upload records were not changed.",
                "Regenerate Thumbnail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Regenerate Thumbnail", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RegenerateAllLongFormQuizThumbnailsAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        if (MessageBox.Show(
                this,
                "Regenerate Thumbnail.png for every long-form quiz in Quiz History?\n\n" +
                "Existing thumbnails will be overwritten. Videos, Resolve projects, promo Shorts and upload records will not be changed.",
                "Regenerate All Long-Form Thumbnails",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000)
                .Where(QuizHistoricalThumbnailRegenerator.IsBatchEligible)
                .ToList();
            if (histories.Count == 0)
            {
                MessageBox.Show(this, "There are no long-form quizzes in Quiz History.",
                    "Regenerate All Long-Form Thumbnails", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lookup = CreateQuizQuestionLookup();
            var succeeded = 0;
            var failed = new List<string>();
            for (var index = 0; index < histories.Count; index++)
            {
                var history = histories[index];
                sourceButton.Content = $"Thumbnails {index + 1}/{histories.Count}";
                try
                {
                    RegenerateHistoricalThumbnail(history, lookup);
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            RefreshUploadManager();
            var summary = new StringBuilder();
            summary.AppendLine($"Regenerated: {succeeded:N0}");
            summary.AppendLine($"Skipped/failed: {failed.Count:N0}");
            if (failed.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Items needing attention:");
                foreach (var failure in failed.Take(8))
                    summary.AppendLine("• " + failure);
                if (failed.Count > 8)
                    summary.AppendLine($"• …and {failed.Count - 8:N0} more");
            }
            summary.AppendLine();
            summary.Append("Only Thumbnail.png files were changed.");

            MessageBox.Show(
                this,
                summary.ToString(),
                "Regenerate All Long-Form Thumbnails",
                MessageBoxButton.OK,
                failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Regenerate All Long-Form Thumbnails", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private QuizHistoricalThumbnailResult RegenerateHistoricalThumbnail(
        QuizHistorySummary history,
        Func<int, QuizQuestion?> questionLookup)
    {
        var questions = _data.GetQuizHistoryQuestions(history.Id);
        var logoPath = _data.LoadQuizLogoPath();
        return QuizHistoricalThumbnailRegenerator.Regenerate(
            history,
            questions,
            questionLookup,
            logoPath);
    }

    private QuizHistorySummary ResolveThumbnailHistoryEntry(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (Directory.Exists(history.ProjectFolder))
            return history;

        _data.RecoverQuizHistoryProjectFolders();
        return _data.GetQuizHistory(2_000).FirstOrDefault(item => item.Id == history.Id)
               ?? history;
    }

    private Func<int, QuizQuestion?> CreateQuizQuestionLookup()
    {
        var cached = _data.GetQuizQuestions(limit: 10_000, enabledOnly: false)
            .GroupBy(question => question.Id)
            .ToDictionary(group => group.Key, group => group.First());

        return id =>
        {
            if (id <= 0)
                return null;
            if (cached.TryGetValue(id, out var existing))
                return existing;

            var found = _data.GetQuizQuestions(
                    search: id.ToString(CultureInfo.InvariantCulture),
                    limit: 25,
                    enabledOnly: false)
                .FirstOrDefault(question => question.Id == id);
            if (found is not null)
                cached[id] = found;
            return found;
        };
    }
}
