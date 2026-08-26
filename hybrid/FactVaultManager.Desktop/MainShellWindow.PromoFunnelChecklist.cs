using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record QuizPromoRelatedVideoChecklistRow(
    string Quiz,
    string PromoVideoId,
    string LongVideoId,
    string Tracker,
    string RelatedVideo,
    string ProjectFolder,
    string CampaignSlug);

public partial class MainShellWindow
{
    private Button? _promoRelatedVideoChecklistButton;

    public void InitializePromoRelatedVideoChecklistForApp()
    {
        Loaded += (_, _) =>
        {
            MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(EnsurePromoRelatedVideoChecklistButton));
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(EnsurePromoRelatedVideoChecklistButton));
        };
    }

    private void EnsurePromoRelatedVideoChecklistButton()
    {
        if (_promoRelatedVideoChecklistButton?.Parent is not null)
        {
            UpdatePromoRelatedVideoChecklistButtonLabel();
            return;
        }
        if (Content is not DependencyObject root) return;

        var refresh = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "Refresh tracker",
                StringComparison.Ordinal));
        if (refresh?.Parent is not StackPanel actions) return;

        var button = new Button
        {
            Content = "Related videos",
            MinWidth = 168,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Work through YouTube Promo Shorts that still need their Related video set to the matching long-form quiz.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(70, 235, 115));
        button.Click += async (_, _) => await ShowPromoRelatedVideoChecklistAsync(button);

        var refreshIndex = actions.Children.IndexOf(refresh);
        actions.Children.Insert(refreshIndex < 0 ? actions.Children.Count : refreshIndex, button);
        _promoRelatedVideoChecklistButton = button;
        UpdatePromoRelatedVideoChecklistButtonLabel();
    }

    private void UpdatePromoRelatedVideoChecklistButtonLabel()
    {
        if (_promoRelatedVideoChecklistButton is null) return;
        try
        {
            var targets = QuizPromoRelatedVideoPlanner.Build(_data.GetQuizHistory(2_000));
            var completed = targets.Count(target =>
                target.LongVideoId.Length > 0 &&
                QuizPromoRelatedVideoStore.IsSetFor(
                    target.ProjectFolder,
                    target.PromoVideoId,
                    target.LongVideoId));
            var pending = targets.Count - completed;
            _promoRelatedVideoChecklistButton.Content = targets.Count == 0
                ? "Related videos"
                : pending == 0
                    ? $"Related videos ✓ {targets.Count:N0}"
                    : $"Related videos • {pending:N0} need";
        }
        catch
        {
            _promoRelatedVideoChecklistButton.Content = "Related videos";
        }
    }

    private async Task ShowPromoRelatedVideoChecklistAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        const string title = "YouTube Promo Related Videos";
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var targets = QuizPromoRelatedVideoPlanner.Build(_data.GetQuizHistory(2_000));
            if (targets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No long-form quizzes have a saved YouTube Promo Short upload yet.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            HashSet<string>? trackerCampaigns = null;
            var trackerNote = "";
            var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (trackerSettings.IsConfigured)
            {
                try
                {
                    var stats = await _factburstLinkTracker.FetchStatsAsync(
                        trackerSettings.BaseUrl,
                        trackerSettings.ApiKey);
                    trackerCampaigns = stats
                        .Select(item => item.Slug)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception error)
                {
                    trackerNote = "Tracker status unavailable: " + error.Message;
                }
            }
            else
            {
                trackerNote = "Link Tracker is not configured; Related video checklist state still works locally.";
            }

            var window = new Window
            {
                Title = title,
                Owner = this,
                Width = 1080,
                Height = 650,
                MinWidth = 900,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
            };

            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            intro.Children.Add(new TextBlock
            {
                Text = "Promo Funnel Checklist",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
            });
            intro.Children.Add(new TextBlock
            {
                Text = "Open the exact Promo Short in YouTube Studio, set Related video to the matching long-form quiz, then mark it set here. YouTube does not expose this Shorts field through the Data API, so this is an explicit manual checklist rather than a fake API verification.",
                Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            layout.Children.Add(intro);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                Background = new SolidColorBrush(Color.FromRgb(8, 20, 67)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
                BorderThickness = new Thickness(1),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(44, 70, 130)),
                Margin = new Thickness(0, 0, 0, 10),
            };
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Quiz",
                Binding = new Binding(nameof(QuizPromoRelatedVideoChecklistRow.Quiz)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Promo ID",
                Binding = new Binding(nameof(QuizPromoRelatedVideoChecklistRow.PromoVideoId)),
                Width = 120,
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Full quiz ID",
                Binding = new Binding(nameof(QuizPromoRelatedVideoChecklistRow.LongVideoId)),
                Width = 120,
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Tracking",
                Binding = new Binding(nameof(QuizPromoRelatedVideoChecklistRow.Tracker)),
                Width = 118,
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Related video",
                Binding = new Binding(nameof(QuizPromoRelatedVideoChecklistRow.RelatedVideo)),
                Width = 130,
            });
            Grid.SetRow(grid, 1);
            layout.Children.Add(grid);

            var status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(status, 2);
            layout.Children.Add(status);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var filter = ChecklistButton("Show all", Color.FromRgb(255, 202, 45));
            var openStudio = ChecklistButton("Open Studio", Color.FromRgb(0, 204, 255));
            var openQuiz = ChecklistButton("Open full quiz", Color.FromRgb(88, 188, 255));
            var markSet = ChecklistButton("Mark set", Color.FromRgb(70, 235, 115));
            var markNeeds = ChecklistButton("Mark needs setting", Color.FromRgb(248, 90, 105));
            var close = ChecklistButton("Close", Color.FromRgb(180, 190, 210));
            actions.Children.Add(filter);
            actions.Children.Add(openStudio);
            actions.Children.Add(openQuiz);
            actions.Children.Add(markSet);
            actions.Children.Add(markNeeds);
            actions.Children.Add(close);
            Grid.SetRow(actions, 3);
            layout.Children.Add(actions);

            var showAll = false;
            IReadOnlyList<QuizPromoRelatedVideoChecklistRow> currentRows = Array.Empty<QuizPromoRelatedVideoChecklistRow>();

            bool IsComplete(QuizPromoRelatedVideoTarget target) =>
                target.LongVideoId.Length > 0 &&
                QuizPromoRelatedVideoStore.IsSetFor(
                    target.ProjectFolder,
                    target.PromoVideoId,
                    target.LongVideoId);

            string TrackerStatus(QuizPromoRelatedVideoTarget target) => trackerCampaigns is null
                ? trackerSettings.IsConfigured ? "Unavailable" : "Not configured"
                : trackerCampaigns.Contains(target.CampaignSlug) ? "Ready" : "Missing";

            void RefreshRows()
            {
                var completed = targets.Count(IsComplete);
                var pending = targets.Count - completed;
                if (pending == 0) showAll = true;
                currentRows = targets
                    .Where(target => showAll || !IsComplete(target))
                    .Select(target => new QuizPromoRelatedVideoChecklistRow(
                        target.Title,
                        target.PromoVideoId,
                        target.LongVideoId.Length == 0 ? "Invalid link" : target.LongVideoId,
                        TrackerStatus(target),
                        IsComplete(target) ? "Set" : "Needs setting",
                        target.ProjectFolder,
                        target.CampaignSlug))
                    .OrderBy(row => string.Equals(row.RelatedVideo, "Set", StringComparison.Ordinal) ? 1 : 0)
                    .ThenBy(row => row.Quiz, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                grid.ItemsSource = currentRows;
                if (currentRows.Count > 0) grid.SelectedIndex = 0;
                filter.Content = showAll ? "Show needs only" : $"Show all ({targets.Count:N0})";
                status.Text = $"Related video set: {completed:N0}/{targets.Count:N0} • Need setting: {pending:N0}" +
                              (trackerNote.Length == 0 ? "" : Environment.NewLine + trackerNote);
                UpdatePromoRelatedVideoChecklistButtonLabel();
            }

            QuizPromoRelatedVideoChecklistRow? Selected() =>
                grid.SelectedItem as QuizPromoRelatedVideoChecklistRow;

            QuizPromoRelatedVideoTarget? TargetFor(QuizPromoRelatedVideoChecklistRow row) =>
                targets.FirstOrDefault(target =>
                    string.Equals(target.ProjectFolder, row.ProjectFolder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(target.PromoVideoId, row.PromoVideoId, StringComparison.Ordinal));

            filter.Click += (_, _) =>
            {
                showAll = !showAll;
                RefreshRows();
            };
            openStudio.Click += (_, _) =>
            {
                var row = Selected();
                if (row is null) return;
                OpenChecklistUrl(QuizPromoRelatedVideoLinks.StudioEditUrl(row.PromoVideoId), title);
            };
            openQuiz.Click += (_, _) =>
            {
                var row = Selected();
                if (row is null) return;
                var target = TargetFor(row);
                if (target is null || target.LongVideoId.Length == 0)
                {
                    MessageBox.Show(window, "The saved long-form YouTube link is invalid.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                OpenChecklistUrl(QuizPromoRelatedVideoLinks.WatchUrl(target.LongVideoId), title);
            };
            markSet.Click += (_, _) =>
            {
                var row = Selected();
                if (row is null) return;
                var target = TargetFor(row);
                if (target is null || target.LongVideoId.Length == 0)
                {
                    MessageBox.Show(window, "Fix the saved long-form YouTube link before marking Related video as set.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                QuizPromoRelatedVideoStore.MarkSet(
                    target.ProjectFolder,
                    target.PromoVideoId,
                    target.LongVideoId,
                    DateTimeOffset.UtcNow);
                RefreshRows();
            };
            markNeeds.Click += (_, _) =>
            {
                var row = Selected();
                if (row is null) return;
                QuizPromoRelatedVideoStore.MarkNeedsSetting(row.ProjectFolder);
                RefreshRows();
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
                Child = layout,
            };

            RefreshRows();
            window.ShowDialog();
            UpdatePromoRelatedVideoChecklistButtonLabel();
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

    private Button ChecklistButton(string text, Color colour)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            MinWidth = 108,
            Margin = new Thickness(6, 0, 0, 0),
        };
        StyleQuizHistoryButton(button, colour);
        return button;
    }

    private static void OpenChecklistUrl(string url, string title)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
