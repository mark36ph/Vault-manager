using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizPackagingSelectedMenuText = "Generate YouTube A/B Package";
    private const string QuizPackagingTodayMenuText = "Generate A/B Packages for Today's Quizzes";
    private static readonly bool QuizPackagingUiRegistered = RegisterQuizPackagingUi();

    private static bool RegisterQuizPackagingUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizPackagingToolsButton_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizPackagingToolsButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Quiz Tools ▾", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
        {
            return;
        }

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => window.EnsureQuizPackagingMenuItems(button)));
    }

    private void EnsureQuizPackagingMenuItems(Button toolsButton)
    {
        if (!TryGetUploadManagerPopup(toolsButton, out _, out var panel))
            return;
        if (panel.Children.OfType<Button>().Any(item =>
                string.Equals(item.Content?.ToString(), QuizPackagingSelectedMenuText, StringComparison.Ordinal)))
        {
            return;
        }

        AddUploadManagerMenuSeparator(toolsButton);
        AddUploadManagerMenuItem(toolsButton, QuizPackagingSelectedMenuText, (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                GenerateSelectedQuizYouTubePackage(history);
            else
                MessageBox.Show(this, "Select a long-form quiz first.", "YouTube A/B Package",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuItem(toolsButton, QuizPackagingTodayMenuText, async (_, _) =>
            await GenerateTodaysQuizYouTubePackagesAsync(toolsButton));
    }

    private void GenerateSelectedQuizYouTubePackage(QuizHistorySummary history)
    {
        try
        {
            var result = GenerateHistoricalYouTubePackage(history);
            RefreshUploadManager();
            MessageBox.Show(
                this,
                "YouTube A/B package created.\n\n" +
                "3 title candidates and 3 thumbnail candidates are ready for YouTube Test & Compare.\n\n" +
                "The Upload Manager now shows Package A's varied click-focused title instead of the older generic publishing title.\n\n" +
                $"Saved in:\n{result.ProjectFolder}\n\n" +
                $"Manifest:\n{result.ManifestPath}\n\n" +
                "Thumbnail.png and your existing upload records were not changed.",
                "YouTube A/B Package",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "YouTube A/B Package",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task GenerateTodaysQuizYouTubePackagesAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        _data.RecoverQuizHistoryProjectFolders();
        var todayPrefix = DateTime.Now.ToString("yyyy-MM-dd");
        var histories = _data.GetQuizHistory(2_000)
            .Where(QuizHistoricalThumbnailRegenerator.IsBatchEligible)
            .Where(history => (history.Created ?? "").Trim().StartsWith(todayPrefix, StringComparison.Ordinal))
            .OrderBy(history => history.Id)
            .ToList();

        if (histories.Count == 0)
        {
            MessageBox.Show(this, "There are no long-form quizzes in Quiz History from today.",
                "YouTube A/B Packages", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Generate 3 YouTube title candidates and 3 thumbnail candidates for {histories.Count:N0} long-form quiz{(histories.Count == 1 ? "" : "zes")} created today?\n\n" +
                "This also refreshes each Upload Manager row to the varied Package A title. Thumbnail.png, videos and upload records will not be changed.",
                "Generate Today's YouTube A/B Packages",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        var succeeded = 0;
        var failed = new List<string>();
        try
        {
            for (var index = 0; index < histories.Count; index++)
            {
                var history = histories[index];
                sourceButton.Content = $"Packaging {index + 1}/{histories.Count}";
                try
                {
                    GenerateHistoricalYouTubePackage(history);
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }

        RefreshUploadManager();

        var summary = new StringBuilder();
        summary.AppendLine($"A/B packages created: {succeeded:N0}");
        summary.AppendLine($"Failed: {failed.Count:N0}");
        if (failed.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Failures:");
            foreach (var item in failed.Take(8))
                summary.AppendLine("• " + item);
            if (failed.Count > 8)
                summary.AppendLine($"• …and {failed.Count - 8:N0} more");
        }
        summary.AppendLine();
        summary.AppendLine("Each successful project now contains YouTube Title A/B/C files, Thumbnail A/B/C PNGs and YouTube Packaging.json.");
        summary.AppendLine("Upload Manager has also been refreshed to show each quiz's Package A title.");

        MessageBox.Show(this, summary.ToString().Trim(), "YouTube A/B Packages",
            MessageBoxButton.OK,
            failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private QuizYouTubePackagingResult GenerateHistoricalYouTubePackage(QuizHistorySummary history)
    {
        history = ResolveThumbnailHistoryEntry(history);
        if (!QuizHistoricalThumbnailRegenerator.IsBatchEligible(history))
            throw new InvalidOperationException("YouTube A/B packaging is available for long-form quizzes only.");

        var plan = QuizHistoricalThumbnailRegenerator.BuildPlan(
            history,
            _data.GetQuizHistoryQuestions(history.Id),
            CreateQuizQuestionLookup(),
            _data.LoadQuizLogoPath());
        var result = QuizYouTubePackaging.Write(
            plan.ProjectFolder,
            plan.Metadata,
            plan.Questions,
            plan.Visual,
            plan.LogoPath,
            plan.Vertical);
        var initialTitle = result.Variants.FirstOrDefault(variant =>
            string.Equals(variant.Key, "A", StringComparison.OrdinalIgnoreCase))?.Title;
        if (!string.IsNullOrWhiteSpace(initialTitle))
            _data.UpdateQuizHistoryYouTubeTitle(history.Id, initialTitle);
        return result;
    }
}
