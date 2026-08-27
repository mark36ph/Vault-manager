using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string ScheduledRelatedVideoGuideButtonTag = "scheduled-related-video-guide";
    private Button? _scheduledRelatedVideoGuideButton;
    private int _scheduledRelatedVideoGuideUiAttempts;
    private bool _scheduledRelatedVideoGuideInitialized;

    private sealed record ScheduledRelatedVideoGuideItem(
        QuizHistorySummary History,
        string PromoVideoId,
        string LongVideoId);

    public void InitializeScheduledRelatedVideoGuideForApp()
    {
        if (_scheduledRelatedVideoGuideInitialized) return;
        _scheduledRelatedVideoGuideInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledRelatedVideoGuideButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledRelatedVideoGuideButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledRelatedVideoGuideButton));
    }

    private void EnsureScheduledRelatedVideoGuideButton()
    {
        if (_scheduledRelatedVideoGuideButton?.Parent is not null) return;
        if (Content is not DependencyObject root)
        {
            RetryScheduledRelatedVideoGuideButton();
            return;
        }

        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                ScheduledRelatedVideoGuideButtonTag,
                StringComparison.Ordinal));
        if (existing is not null)
        {
            _scheduledRelatedVideoGuideButton = existing;
            return;
        }

        var uploadManager = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Open Upload Manager",
                StringComparison.Ordinal));
        if (uploadManager?.Parent is not StackPanel actions)
        {
            RetryScheduledRelatedVideoGuideButton();
            return;
        }

        var button = new Button
        {
            Content = "Related video setup",
            Tag = ScheduledRelatedVideoGuideButtonTag,
            MinWidth = 150,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Work through missing YouTube Shorts Related videos for the scheduled range shown. YouTube requires this field to be saved in Studio, so the app guides and records each manual save.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(90, 220, 170));
        button.Click += async (_, _) => await ShowScheduledRelatedVideoGuideAsync(button);

        var uploadIndex = actions.Children.IndexOf(uploadManager);
        actions.Children.Insert(uploadIndex < 0 ? actions.Children.Count : uploadIndex, button);
        _scheduledRelatedVideoGuideButton = button;
    }

    private void RetryScheduledRelatedVideoGuideButton()
    {
        if (++_scheduledRelatedVideoGuideUiAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureScheduledRelatedVideoGuideButton();
        };
        timer.Start();
    }

    private async Task ShowScheduledRelatedVideoGuideAsync(Button sourceButton)
    {
        const string title = "Scheduled Related Video Setup";
        if (_scheduledReadinessGrid?.ItemsSource is not IEnumerable<ScheduledReleaseReadinessRow> visibleRows)
        {
            MessageBox.Show(this, "Open Release Readiness and refresh it first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetRows = ScheduledPromoBatchPlanner.SelectMissingRelatedVideos(visibleRows).ToList();
        if (targetRows.Count == 0)
        {
            MessageBox.Show(this,
                "Every uploaded YouTube promo in the current scheduled range is already marked with its Related video, or no YouTube promo has been uploaded yet.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        sourceButton.IsEnabled = false;
        try
        {
            var histories = _data.GetQuizHistory(2_000)
                .GroupBy(history => history.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var items = new List<ScheduledRelatedVideoGuideItem>();
            var problems = new List<string>();

            foreach (var row in targetRows)
            {
                if (!histories.TryGetValue(row.HistoryId, out var history))
                {
                    problems.Add($"{row.Quiz}: quiz history record is missing.");
                    continue;
                }

                var promo = QuizPromoShortPublicationStore.LoadYouTube(history.ProjectFolder);
                var longVideoId = YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl) ?? "";
                if (promo is null)
                {
                    problems.Add($"{row.Quiz}: YouTube promo upload record is missing.");
                    continue;
                }
                if (longVideoId.Length == 0)
                {
                    problems.Add($"{row.Quiz}: the saved long-form YouTube URL is invalid.");
                    continue;
                }
                if (QuizPromoRelatedVideoStore.IsSetFor(history.ProjectFolder, promo.VideoId, longVideoId))
                    continue;

                items.Add(new ScheduledRelatedVideoGuideItem(history, promo.VideoId, longVideoId));
            }

            if (items.Count == 0)
            {
                await RefreshScheduledReleaseReadinessAsync(false);
                MessageBox.Show(this,
                    "There are no valid Related video items to work through." +
                    (problems.Count == 0 ? "" : "\n\n" + string.Join("\n", problems.Take(8))),
                    title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new Window
            {
                Title = title,
                Owner = this,
                Width = 860,
                Height = 520,
                MinWidth = 760,
                MinHeight = 470,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
            };

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            intro.Children.Add(new TextBlock
            {
                Text = "Work through Related videos",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
            });
            intro.Children.Add(new TextBlock
            {
                Text = "YouTube does not expose the Shorts Related video field through the Data API. This guide opens the exact Short in Studio, copies the matching full-quiz title for you, and records each item only after you confirm you saved it in Studio.",
                Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0),
            });
            root.Children.Add(intro);

            var progress = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(255, 213, 82)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(progress, 1);
            root.Children.Add(progress);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(9, 24, 78)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 16),
            };
            var details = new StackPanel();
            var quizTitle = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            var ids = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var instruction = new TextBlock
            {
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 18, 0, 0),
            };
            details.Children.Add(quizTitle);
            details.Children.Add(ids);
            details.Children.Add(instruction);
            card.Child = details;
            Grid.SetRow(card, 2);
            root.Children.Add(card);

            var actions = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var openStudio = ChecklistButton("Copy title + open Studio", Color.FromRgb(0, 204, 255));
            openStudio.MinWidth = 178;
            var openQuiz = ChecklistButton("Open full quiz", Color.FromRgb(88, 188, 255));
            var skip = ChecklistButton("Skip", Color.FromRgb(255, 202, 45));
            var markSaved = ChecklistButton("Mark saved & next", Color.FromRgb(70, 235, 115));
            markSaved.MinWidth = 146;
            var close = ChecklistButton("Close", Color.FromRgb(180, 190, 210));
            actions.Children.Add(openStudio);
            actions.Children.Add(openQuiz);
            actions.Children.Add(skip);
            actions.Children.Add(markSaved);
            actions.Children.Add(close);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            var index = 0;
            var marked = 0;
            var skipped = 0;

            ScheduledRelatedVideoGuideItem Current() => items[index];

            void UpdateCurrent()
            {
                if (index >= items.Count)
                {
                    window.Close();
                    return;
                }

                var item = Current();
                progress.Text = $"{index + 1:N0} of {items.Count:N0} • Marked {marked:N0} • Skipped {skipped:N0}";
                quizTitle.Text = item.History.UploadTitleDisplay;
                ids.Text = $"Promo Short ID: {item.PromoVideoId}\nFull quiz ID: {item.LongVideoId}";
                instruction.Text =
                    "1. Click “Copy title + open Studio”.\n" +
                    "2. In YouTube Studio choose Related video, paste/search the copied full-quiz title, select the matching quiz, then click SAVE.\n" +
                    "3. Return here and click “Mark saved & next”.";
            }

            openStudio.Click += (_, _) =>
            {
                var item = Current();
                try
                {
                    Clipboard.SetText(item.History.UploadTitleDisplay);
                }
                catch (Exception error)
                {
                    MessageBox.Show(window, "Could not copy the quiz title: " + error.Message,
                        title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                OpenChecklistUrl(QuizPromoRelatedVideoLinks.StudioEditUrl(item.PromoVideoId), title);
            };
            openQuiz.Click += (_, _) =>
            {
                var item = Current();
                OpenChecklistUrl(QuizPromoRelatedVideoLinks.WatchUrl(item.LongVideoId), title);
            };
            skip.Click += (_, _) =>
            {
                skipped++;
                index++;
                UpdateCurrent();
            };
            markSaved.Click += (_, _) =>
            {
                var item = Current();
                QuizPromoRelatedVideoStore.MarkSet(
                    item.History.ProjectFolder,
                    item.PromoVideoId,
                    item.LongVideoId,
                    DateTimeOffset.UtcNow);
                marked++;
                index++;
                UpdateCurrent();
            };
            close.Click += (_, _) => window.Close();

            window.Content = new Border
            {
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromRgb(7, 13, 57), 0),
                        new(Color.FromRgb(18, 34, 115), 0.65),
                        new(Color.FromRgb(80, 30, 145), 1),
                    },
                    new Point(0, 0),
                    new Point(1, 1)),
                Child = root,
            };

            UpdateCurrent();
            window.ShowDialog();
            await RefreshScheduledReleaseReadinessAsync(false);
            UpdatePromoRelatedVideoChecklistButtonLabel();
            SetScheduledReadinessStatus(
                $"Related video setup: marked {marked:N0} • skipped {skipped:N0}. " +
                "Only items you confirmed after saving in YouTube Studio were marked complete.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.IsEnabled = true;
        }
    }
}
