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
    private ComboBox? _scheduledReadinessView;
    private Button? _scheduledReadinessOpenButton;
    private TextBlock? _scheduledReadinessScheduledText;
    private TextBlock? _scheduledReadinessReadyText;
    private TextBlock? _scheduledReadinessAttentionText;
    private TextBlock? _scheduledReadinessStatusText;
    private IReadOnlyList<ScheduledReleaseReadinessRow> _scheduledReadinessRows = Array.Empty<ScheduledReleaseReadinessRow>();
    private string _scheduledReadinessTrackerNote = "";
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
            ToolTip = "See which scheduled quizzes still need attention before release.",
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Release Readiness",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Start with anything that needs attention. Use Next action to go straight to the workflow that fixes it.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(heading);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 3; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddScheduledReadinessStat(stats, 0, 3, "Scheduled", Color.FromRgb(0, 204, 255), out _scheduledReadinessScheduledText);
        AddScheduledReadinessStat(stats, 1, 3, "Ready", Color.FromRgb(70, 235, 115), out _scheduledReadinessReadyText);
        AddScheduledReadinessStat(stats, 2, 3, "Need attention", Color.FromRgb(255, 202, 45), out _scheduledReadinessAttentionText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        filters.Children.Add(new TextBlock
        {
            Text = "Show",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        _scheduledReadinessView = new ComboBox
        {
            MinWidth = 150,
            Height = 36,
            ItemsSource = new[] { "Needs attention", "All scheduled", "Ready only" },
            SelectedIndex = 0,
        };
        _scheduledReadinessView.SelectionChanged += (_, _) => ApplyScheduledReadinessView();
        filters.Children.Add(_scheduledReadinessView);
        filters.Children.Add(new TextBlock
        {
            Text = "Range",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 7, 0),
        });
        _scheduledReadinessHorizon = new ComboBox
        {
            MinWidth = 130,
            Height = 36,
            ItemsSource = new[] { "Next 7 days", "Next 14 days", "All scheduled" },
            SelectedIndex = 1,
        };
        _scheduledReadinessHorizon.SelectionChanged += async (_, _) =>
            await RefreshScheduledReleaseReadinessAsync(false);
        filters.Children.Add(_scheduledReadinessHorizon);
        toolbar.Children.Add(filters);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var createLinks = new Button
        {
            Content = "Create missing tracking links",
            MinWidth = 188,
            MinHeight = 36,
            ToolTip = "Create missing Factburst tracking campaigns for quizzes in the selected date range.",
        };
        StyleQuizHistoryButton(createLinks, Color.FromRgb(255, 202, 45));
        createLinks.Click += async (_, _) => await CreateMissingScheduledTrackingLinksAsync(createLinks);
        actions.Children.Add(createLinks);

        _scheduledReadinessOpenButton = new Button
        {
            Content = "Open selected quiz",
            MinWidth = 150,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false,
            ToolTip = "Open the selected quiz in Upload Manager so you can fix its next release task.",
        };
        StyleQuizHistoryButton(_scheduledReadinessOpenButton, Color.FromRgb(70, 235, 115));
        _scheduledReadinessOpenButton.Click += (_, _) => OpenScheduledReadinessInUploadManager();
        actions.Children.Add(_scheduledReadinessOpenButton);

        var refresh = new Button
        {
            Content = "Refresh",
            MinWidth = 92,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshScheduledReleaseReadinessAsync(true);
        actions.Children.Add(refresh);
        Grid.SetColumn(actions, 2);
        toolbar.Children.Add(actions);
        Grid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        _scheduledReadinessGrid = BuildManagerGrid();
        ScrollViewer.SetHorizontalScrollBarVisibility(_scheduledReadinessGrid, ScrollBarVisibility.Disabled);
        _scheduledReadinessGrid.MouseDoubleClick += (_, _) => OpenScheduledReadinessInUploadManager();
        _scheduledReadinessGrid.SelectionChanged += (_, _) =>
        {
            if (_scheduledReadinessOpenButton is not null)
                _scheduledReadinessOpenButton.IsEnabled = _scheduledReadinessGrid.SelectedItem is ScheduledReleaseReadinessRow;
        };
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
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            MinWidth = 300,
        });
        _scheduledReadinessGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(ScheduledReleaseReadinessRow.Readiness)),
            Width = 145,
        });
        _scheduledReadinessGrid.Columns.Add(BuildVisibleScheduledReadinessActionColumn());
        var card = ManagerCard(_scheduledReadinessGrid);
        Grid.SetRow(card, 3);
        root.Children.Add(card);

        _scheduledReadinessStatusText = new TextBlock
        {
            Text = "Scheduled long-form quizzes will appear here automatically.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(_scheduledReadinessStatusText, 4);
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

    private static void AddScheduledReadinessStat(
        Grid parent,
        int column,
        int columnCount,
        string label,
        Color colour,
        out TextBlock value)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(colour),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == columnCount - 1 ? 0 : 5, 0),
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
            _scheduledReadinessTrackerNote = "";
            if (trackerSettings.IsConfigured)
            {
                try
                {
                    var remote = await _factburstLinkTracker.FetchStatsAsync(trackerSettings.BaseUrl, trackerSettings.ApiKey);
                    trackerCampaigns = remote.Select(item => item.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception error)
                {
                    _scheduledReadinessTrackerNote = " Tracker unavailable: " + error.Message;
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

            _scheduledReadinessRows = rows;
            var ready = rows.Count(row => row.ReadyCount == row.TotalChecks);
            _scheduledReadinessScheduledText!.Text = rows.Count.ToString("N0");
            _scheduledReadinessReadyText!.Text = ready.ToString("N0");
            _scheduledReadinessAttentionText!.Text = (rows.Count - ready).ToString("N0");
            ApplyScheduledReadinessView();
        }
        catch (Exception error)
        {
            SetScheduledReadinessStatus(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Release Readiness", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _scheduledReadinessRefreshing = false;
        }
    }

    private void ApplyScheduledReadinessView()
    {
        if (_scheduledReadinessGrid is null) return;

        var selectedHistoryId = (_scheduledReadinessGrid.SelectedItem as ScheduledReleaseReadinessRow)?.HistoryId;
        IEnumerable<ScheduledReleaseReadinessRow> filtered = _scheduledReadinessRows;
        filtered = _scheduledReadinessView?.SelectedIndex switch
        {
            0 => filtered.Where(row => row.ReadyCount < row.TotalChecks),
            2 => filtered.Where(row => row.ReadyCount == row.TotalChecks),
            _ => filtered,
        };

        var visibleRows = filtered.ToList();
        _scheduledReadinessGrid.ItemsSource = visibleRows;
        var selected = selectedHistoryId.HasValue
            ? visibleRows.FirstOrDefault(row => row.HistoryId == selectedHistoryId.Value)
            : null;
        _scheduledReadinessGrid.SelectedItem = selected ?? visibleRows.FirstOrDefault();
        if (_scheduledReadinessOpenButton is not null)
            _scheduledReadinessOpenButton.IsEnabled = _scheduledReadinessGrid.SelectedItem is ScheduledReleaseReadinessRow;

        UpdateScheduledReadinessStatus(visibleRows.Count);
    }

    private void UpdateScheduledReadinessStatus(int visibleCount)
    {
        if (_scheduledReadinessRows.Count == 0)
        {
            SetScheduledReadinessStatus("No future scheduled long-form quizzes are in the selected range." + _scheduledReadinessTrackerNote);
            return;
        }

        var ready = _scheduledReadinessRows.Count(row => row.ReadyCount == row.TotalChecks);
        var attention = _scheduledReadinessRows.Count - ready;
        var viewName = _scheduledReadinessView?.SelectedIndex switch
        {
            0 => "needing attention",
            2 => "ready",
            _ => "scheduled",
        };
        SetScheduledReadinessStatus(
            $"Showing {visibleCount:N0} {viewName} quiz(es) • {attention:N0} need attention • {ready:N0} ready. Use Next action or Fix selected to work on a quiz." +
            _scheduledReadinessTrackerNote);
    }

    private async Task CreateMissingScheduledTrackingLinksAsync(Button button)
    {
        var settings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!settings.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", "Create Tracking Links", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = _scheduledReadinessRows
            .Where(row => string.Equals(row.Tracking, "Missing", StringComparison.Ordinal))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Every scheduled quiz in the selected date range already has a tracking campaign.", "Create Tracking Links", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (selected is null) return;

        MainTabs.SelectedIndex = _uploadManagerTabIndex;
        ApplyNavigationSelection(_uploadManagerTabIndex);
        RefreshUploadManager();
        if (_uploadManagerGrid is null) return;
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