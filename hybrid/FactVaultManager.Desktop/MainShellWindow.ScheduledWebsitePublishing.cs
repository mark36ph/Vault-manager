using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduledWebsitePublishingButtonTag = "scheduled-website-publishing";
    private Button? _scheduledWebsitePublishingButton;
    private int _scheduledWebsitePublishingUiAttempts;
    private bool _scheduledWebsitePublishingInitialized;

    public void InitializeScheduledWebsitePublishingForApp()
    {
        if (_scheduledWebsitePublishingInitialized) return;
        _scheduledWebsitePublishingInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingButton));
    }

    private void EnsureScheduledWebsitePublishingButton()
    {
        if (_scheduledWebsitePublishingButton?.Parent is not null) return;
        if (Content is not DependencyObject root)
        {
            RetryScheduledWebsitePublishingButton();
            return;
        }

        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                ScheduledWebsitePublishingButtonTag,
                StringComparison.Ordinal));
        if (existing is not null)
        {
            _scheduledWebsitePublishingButton = existing;
            return;
        }

        var uploadManager = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Open Upload Manager",
                StringComparison.Ordinal));
        if (uploadManager?.Parent is not StackPanel actions)
        {
            RetryScheduledWebsitePublishingButton();
            return;
        }

        var button = new Button
        {
            Content = "Prepare website",
            Tag = ScheduledWebsitePublishingButtonTag,
            MinWidth = 142,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Sync accessible scheduled quizzes and their question visuals to Cloudflare. Unavailable project folders are skipped safely and can be retried later.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(73, 190, 255));
        button.Click += async (_, _) => await PrepareScheduledWebsiteQuizzesAsync(button);

        var uploadIndex = actions.Children.IndexOf(uploadManager);
        actions.Children.Insert(uploadIndex < 0 ? actions.Children.Count : uploadIndex, button);
        _scheduledWebsitePublishingButton = button;
    }

    private void RetryScheduledWebsitePublishingButton()
    {
        if (++_scheduledWebsitePublishingUiAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureScheduledWebsitePublishingButton();
        };
        timer.Start();
    }

    private async Task PrepareScheduledWebsiteQuizzesAsync(Button sourceButton)
    {
        const string title = "Prepare Factburst Website";
        if (_scheduledReadinessGrid?.ItemsSource is not IEnumerable<ScheduledReleaseReadinessRow> visibleRowsSource)
        {
            MessageBox.Show(this, "Open Release Readiness and refresh it first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var visibleRows = visibleRowsSource.OrderBy(row => row.PublishAt).ThenBy(row => row.HistoryId).ToList();
        if (visibleRows.Count == 0)
        {
            MessageBox.Show(this, "There are no scheduled quizzes in the selected range.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!settings.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        sourceButton.IsEnabled = false;
        var originalContent = sourceButton.Content;
        try
        {
            sourceButton.Content = "Checking website...";
            SetScheduledReadinessStatus("Checking the website connection and preparing scheduled quiz data...");
            _data.RecoverQuizHistoryProjectFolders();

            using var website = new FactburstWebsitePublishingClient();
            _ = await website.FetchQuizzesAsync(settings.BaseUrl, settings.ApiKey);

            var histories = _data.GetQuizHistory(2_000)
                .GroupBy(history => history.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var questionImagePaths = _data.GetQuizQuestions(limit: 10_000)
                .Where(question => question.Id > 0 && !string.IsNullOrWhiteSpace(question.ImagePath))
                .ToDictionary(question => question.Id, question => question.ImagePath);

            var targets = new List<(ScheduledReleaseReadinessRow Row, QuizHistorySummary History)>();
            var preflightProblems = new List<string>();
            foreach (var row in visibleRows)
            {
                if (!histories.TryGetValue(row.HistoryId, out var history))
                {
                    preflightProblems.Add($"{row.Quiz}: quiz history record is missing.");
                    continue;
                }
                targets.Add((row, history));
            }

            if (targets.Count == 0)
            {
                var note = "No quizzes can be synced right now.\n\n" + string.Join("\n", preflightProblems.Take(8));
                MessageBox.Show(this, note, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmation =
                $"Sync {targets.Count:N0} scheduled quiz(es) to the Factburst website now?\n\n" +
                "Existing website copies will be refreshed too, so newly supported logo/question images are added without creating duplicates. Their normal website release schedule is preserved.\n\n" +
                "If a project folder or question image is currently unavailable, that quiz will be skipped safely and can be retried later. Nothing is uploaded to social platforms by this action.";
            if (MessageBox.Show(this, confirmation, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var synced = 0;
            var unavailable = 0;
            var failed = preflightProblems.Count;
            var problems = new List<string>(preflightProblems);

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                sourceButton.Content = $"Website {index + 1:N0}/{targets.Count:N0}";
                SetScheduledReadinessStatus(
                    $"Syncing website quiz {index + 1:N0}/{targets.Count:N0}: {target.Row.Quiz}");

                try
                {
                    var payload = FactburstWebsiteQuizBuilder.Build(
                        target.History,
                        target.Row.PublishAt,
                        questionImagePaths);
                    await website.PublishQuizAsync(settings.BaseUrl, settings.ApiKey, payload);
                    synced++;
                }
                catch (Exception error) when (IsUnavailableProjectError(error))
                {
                    unavailable++;
                    problems.Add($"{target.Row.Quiz}: project or question image files unavailable.");
                }
                catch (Exception error)
                {
                    failed++;
                    problems.Add($"{target.Row.Quiz}: {error.Message}");
                }
            }

            await RefreshScheduledReleaseReadinessAsync(false);
            var summary =
                $"Website synced {synced:N0} • Files unavailable {unavailable:N0} • Failed {failed:N0}";
            SetScheduledReadinessStatus(summary);

            var message = summary + "\n\n" +
                "Successfully synced quizzes are stored in Cloudflare. Logo and other available question images are included in the website copy.";
            if (unavailable > 0)
            {
                message += "\n\nUnavailable quizzes were not changed. Run Prepare website again after the recovered files are available on the new drive.";
            }
            if (problems.Count > 0)
                message += "\n\nNeeds attention:\n" + string.Join("\n", problems.Take(8).Select(problem => "• " + problem));

            MessageBox.Show(this, message, title, MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus("Website sync could not start: " + error.Message);
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private static bool IsUnavailableProjectError(Exception error) =>
        error is UnauthorizedAccessException or DriveNotFoundException ||
        error is IOException && error is not FileNotFoundException && error is not DirectoryNotFoundException;
}
