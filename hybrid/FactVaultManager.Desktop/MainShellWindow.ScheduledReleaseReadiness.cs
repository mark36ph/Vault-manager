using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _scheduledReadinessUiInitialized;
    private int _scheduledReadinessUiAttempts;
    private int _scheduledReadinessTabIndex = -1;
    private DataGrid? _scheduledReadinessGrid;
    private ComboBox? _scheduledReadinessHorizon;
    private TextBlock? _scheduledReadinessScheduledText;
    private TextBlock? _scheduledReadinessReadyText;
    private TextBlock? _scheduledReadinessPromoText;
    private TextBlock? _scheduledReadinessTrackingText;
    private TextBlock? _scheduledReadinessStatusText;
    private bool _scheduledReadinessRefreshing;

    public void InitializeScheduledReleaseReadinessForApp()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledReleaseReadinessPage));
    }

    private void EnsureScheduledReleaseReadinessPage()
    {
        if (_scheduledReadinessUiInitialized) return;
        if (AddScheduledReleaseReadinessPage())
        {
            _scheduledReadinessUiInitialized = true;
            return;
        }

        if (++_scheduledReadinessUiAttempts >= 30) return;
        var retry = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            EnsureScheduledReleaseReadinessPage();
        };
        retry.Start();
    }

    private bool AddScheduledReleaseReadinessPage()
    {
        if (MainTabs is null) return false;

        if (_scheduledReadinessTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildScheduledReleaseReadinessPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _scheduledReadinessTabIndex = MainTabs.Items.Count - 1;
            MainTabs.SelectionChanged += async (_, eventArgs) =>
            {
                if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                    await RefreshScheduledReleaseReadinessAsync(false);
            };
        }

        if (_instagramManagerTabIndex < 0 || Content is not DependencyObject root) return false;
        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content),
                "◷   Release Readiness",
                StringComparison.Ordinal));
        if (existing is not null) return true;

        var instagramButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                _instagramManagerTabIndex.ToString(),
                StringComparison.Ordinal));
        if (instagramButton?.Parent is not StackPanel navigation) return false;

        var button = new Button
        {
            Content = "◷   Release Readiness",
            Tag = _scheduledReadinessTabIndex.ToString(),
            ToolTip = "Check every scheduled long-form quiz, promo, tracking link and release task before it goes live.",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;
        button.Click += (_, _) =>
        {
            MainTabs.SelectedIndex = _scheduledReadinessTabIndex;
            ApplyNavigationSelection(_scheduledReadinessTabIndex);
        };
        var instagramIndex = navigation.Children.IndexOf(instagramButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, instagramIndex + 1), button);
        return true;
    }

    private FrameworkElement BuildScheduledReleaseReadinessPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Scheduled Release Readiness",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "See what is ready before each scheduled long-form quiz goes live: package, promo, tracking, platform uploads, Related video and first comment.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _scheduledReadinessHorizon = new ComboBox
        {
            MinWidth = 130,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[] { "Next 7 days", "Next 14 days", "All scheduled" },
            SelectedIndex = 2,
        };
        _scheduledReadinessHorizon.SelectionChanged += async (_, _) =>
            await RefreshScheduledReleaseReadinessAsync(false);
        actions.Children.Add(_scheduledReadinessHorizon);

        var createLinks = new Button
        {
            Content = "Create missing links",
            MinWidth = 148,
            MinHeight = 36,
            ToolTip = "Create missing Factburst tracking campaigns for the scheduled quizzes currently shown.",
        };
        StyleQuizHistoryButton(createLinks, Color.FromRgb(255, 202, 45));
        createLinks.Click += async (_, _) => await CreateMissingScheduledTrackingLinksAsync(createLinks);
        actions.Children.Add(createLinks);

        var uploadManager = new Button
        {
            Content = "Open Upload Manager",
            MinWidth = 152,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(uploadManager, Color.FromRgb(70, 235, 115));
        uploadManager.Click += (_, _) => OpenScheduledReadinessInUploadManager();
        actions.Children.Add(uploadManager);

        var refresh = new Button
        {
            Content = "Refresh readiness",
            MinWidth = 138,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshScheduledReleaseReadinessAsync(true);
        actions.Children.Add(refresh);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddScheduledReadinessStat(stats, 0, "Scheduled", Color.FromRgb(0, 204, 255), out _scheduledReadinessScheduledText);
        AddScheduledReadinessStat(stats, 1, "Fully ready", Color.FromRgb(70, 235, 115), out _scheduledReadinessReadyText);
        AddScheduledReadinessStat(stats, 2, "Need promo", Color.FromRgb(204, 70, 255), out _scheduledReadinessPromoText);
        AddScheduledReadinessStat(stats, 3, "Need tracking", Color.FromRgb(255, 202, 45), out _scheduledReadinessTrackingText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _scheduledReadinessGrid = BuildManagerGrid();
        _scheduledReadinessGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scheduledReadinessGrid.MouseDoubleClick += (_, _) => OpenScheduledReadinessInUploadManager();
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Publish",
            Binding = new Binding(nameof(ScheduledReleaseReadinessRow.PublishAtDisplay)),
            Width = 145,
        });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Quiz)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 260,
        });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Category)), Width = 118 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Package", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Package)), Width = 86 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Promo", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Promo)), Width = 80 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Tracking", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Tracking)), Width = 104 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "YT promo", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.YouTubePromo)), Width = 86 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Facebook", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.FacebookPromo)), Width = 88 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Instagram", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.InstagramPromo)), Width = 88 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Related", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.RelatedVideo)), Width = 110 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "First comment", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.FirstComment)), Width = 105 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Readiness", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Readiness)), Width = 130 });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn { Header = "Next action", Binding = new Binding(nameof(ScheduledReleaseReadinessRow.NextAction)), Width = 170 });
        var card = ManagerCard(_scheduledReadinessGrid);
        Grid.SetRow(card, 2);
        root.Children.Add(card);

        _scheduledReadinessStatusText = new TextBlock
        {
            Text = "Scheduled long-form quizzes will appear here automatically.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(_scheduledReadinessStatusText, 3);
        root.Children.Add(_scheduledReadinessStatusText);

        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.58),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private static void AddScheduledReadinessStat(Grid parent, int column, string label, Color colour, out TextBlock value)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(colour),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 3 ? 0 : 5, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(184, 201, 235)),
            FontSize = 11,
        });
        value = new TextBlock
        {
            Text = "0",
            Foreground = Brushes.White,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0),
        };
        stack.Children.Add(value);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private async Task RefreshScheduledReleaseReadinessAsync(bool showErrors)
    {
        if (_scheduledReadinessRefreshing || _scheduledReadinessGrid is null) return;
        try
        {
            _scheduledReadinessRefreshing = true;
            SetScheduledReadinessStatus("Checking scheduled quizzes and tracker campaigns...");
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000);
            var trackerSettings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            HashSet<string>? trackerCampaigns = null;
            var trackerNote = "";
            if (trackerSettings.IsConfigured)
            {
                try
                {
                    var remote = await _factburstLinkTracker.FetchStatsAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey);
                    trackerCampaigns = remote.Select(item => item.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception error)
                {
                    trackerNote = " Tracker unavailable: " + error.Message;
                }
            }

            var now = DateTimeOffset.Now;
            var rows = ScheduledReleaseReadinessPlanner.Build(
                histories,
                trackerCampaigns,
                trackerSettings.IsConfigured,
                now);
            var horizonDays = ScheduledReadinessHorizonDays();
            if (horizonDays > 0)
            {
                var cutoff = now.AddDays(horizonDays);
                rows = rows.Where(row => row.PublishAt <= cutoff).ToList();
            }

            _scheduledReadinessGrid.ItemsSource = rows;
            if (rows.Count > 0 && _scheduledReadinessGrid.SelectedItem is null)
                _scheduledReadinessGrid.SelectedIndex = 0;

            _scheduledReadinessScheduledText!.Text = rows.Count.ToString("N0");
            _scheduledReadinessReadyText!.Text = rows.Count(row => row.ReadyCount == row.TotalChecks).ToString("N0");
            _scheduledReadinessPromoText!.Text = rows.Count(row => string.Equals(row.Promo, "Missing", StringComparison.Ordinal)).ToString("N0");
            _scheduledReadinessTrackingText!.Text = rows.Count(row => string.Equals(row.Tracking, "Missing", StringComparison.Ordinal)).ToString("N0");

            var ready = rows.Count(row => row.ReadyCount == row.TotalChecks);
            SetScheduledReadinessStatus(
                rows.Count == 0
                    ? "No future scheduled long-form quizzes are in the selected range." + trackerNote
                    : $"{rows.Count:N0} scheduled quiz(es) shown • {ready:N0} fully ready • double-click a row to open it in Upload Manager." + trackerNote);
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Scheduled Release Readiness", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _scheduledReadinessRefreshing = false;
        }
    }

    private async Task CreateMissingScheduledTrackingLinksAsync(Button button)
    {
        if (_scheduledReadinessGrid?.ItemsSource is not IEnumerable<ScheduledReleaseReadinessRow> visibleRows)
            return;

        var settings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!settings.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", "Create Tracking Links", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = visibleRows
            .Where(row => string.Equals(row.Tracking, "Missing", StringComparison.Ordinal))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Every scheduled quiz shown already has a tracking campaign.", "Create Tracking Links", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var histories = _data.GetQuizHistory(2_000).ToDictionary(item => item.Id);
        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "Creating...";
        var created = 0;
        var skipped = 0;
        try
        {
            foreach (var target in targets)
            {
                if (!histories.TryGetValue(target.HistoryId, out var history) || history.YouTubeUrl.Trim().Length == 0)
                {
                    skipped++;
                    continue;
                }

                SetScheduledReadinessStatus($"Creating tracking link {created + skipped + 1:N0}/{targets.Count:N0}: {history.UploadTitleDisplay}");
                await _factburstLinkTracker.CreateOrUpdateCampaignAsync(
                    settings.BaseUrl,
                    settings.ApiKey,
                    FactburstLinkTrackerClient.CampaignSlug(history),
                    history.Id,
                    history.UploadTitleDisplay,
                    history.YouTubeUrl);
                created++;
            }

            await RefreshScheduledReleaseReadinessAsync(false);
            MessageBox.Show(
                this,
                $"Created {created:N0} tracking campaign(s)." + (skipped == 0 ? "" : $"\n\nSkipped {skipped:N0} quiz(es) without a saved YouTube URL."),
                "Create Tracking Links",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Create Tracking Links", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private void OpenScheduledReadinessInUploadManager()
    {
        if (_uploadManagerTabIndex < 0 || MainTabs is null) return;
        var selected = _scheduledReadinessGrid?.SelectedItem as ScheduledReleaseReadinessRow;
        MainTabs.SelectedIndex = _uploadManagerTabIndex;
        ApplyNavigationSelection(_uploadManagerTabIndex);
        RefreshUploadManager();
        if (selected is null || _uploadManagerGrid is null) return;
        var history = _uploadManagerGrid.Items.OfType<QuizHistorySummary>()
            .FirstOrDefault(item => item.Id == selected.HistoryId);
        if (history is null) return;
        _uploadManagerGrid.SelectedItem = history;
        _uploadManagerGrid.ScrollIntoView(history);
    }

    private int ScheduledReadinessHorizonDays() =>
        _scheduledReadinessHorizon?.SelectedIndex switch
        {
            0 => 7,
            1 => 14,
            _ => 0,
        };

    private void SetScheduledReadinessStatus(string text)
    {
        if (_scheduledReadinessStatusText is not null)
            _scheduledReadinessStatusText.Text = text;
    }
}
