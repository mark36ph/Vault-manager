using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record FunnelPerformanceRow(
    int HistoryId,
    string Quiz,
    string Category,
    long FacebookClicks,
    long InstagramClicks,
    long YouTubePromoClicks,
    long TotalClicks,
    long LongFormViews,
    string PromoStatus,
    string Signal);

public partial class MainShellWindow
{
    private readonly FactburstLinkTrackerClient _factburstLinkTracker = new();
    private bool _factburstTrackerUiInitialized;
    private int _factburstTrackerUiAttempts;
    private bool _funnelNavigationAdded;
    private int _funnelPerformanceTabIndex = -1;
    private bool _funnelPerformanceRefreshing;
    private DataGrid? _funnelPerformanceGrid;
    private TextBlock? _funnelTotalClicksText;
    private TextBlock? _funnelFacebookClicksText;
    private TextBlock? _funnelInstagramClicksText;
    private TextBlock? _funnelYouTubeClicksText;
    private TextBlock? _funnelTopSourceText;
    private TextBlock? _funnelStatusText;
    private TextBox? _settingsTrackerBaseUrl;
    private PasswordBox? _settingsTrackerApiKey;
    private TextBlock? _settingsTrackerStatus;

    public void InitializeFactburstTrackerForApp()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(InitializeFactburstTrackerUi));
    }

    private void InitializeFactburstTrackerUi()
    {
        if (_factburstTrackerUiInitialized) return;

        var settingsReady = InjectTrackerSettingsPage();
        var funnelReady = AddFunnelPerformancePage();
        if (settingsReady && funnelReady)
        {
            _factburstTrackerUiInitialized = true;
            return;
        }

        if (++_factburstTrackerUiAttempts >= 30) return;
        var retry = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            InitializeFactburstTrackerUi();
        };
        retry.Start();
    }

    private bool InjectTrackerSettingsPage()
    {
        if (_settingsPages.ContainsKey("tracker")) return true;
        if (!_settingsNavButtons.TryGetValue("about", out var aboutButton) ||
            aboutButton.Parent is not StackPanel sidebar)
            return false;

        _settingsPages["tracker"] = BuildTrackerSettingsPage();
        var trackerButton = new Button
        {
            Content = "Link Tracker",
            Tag = "tracker",
            Height = 36,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        };
        trackerButton.Click += (_, _) => SelectSettingsPage("tracker");
        _settingsNavButtons["tracker"] = trackerButton;
        var aboutIndex = sidebar.Children.IndexOf(aboutButton);
        sidebar.Children.Insert(Math.Max(0, aboutIndex), trackerButton);
        return true;
    }

    private FrameworkElement BuildTrackerSettingsPage()
    {
        var page = SettingsPageStack(
            "Link Tracker",
            "Connect the Cloudflare Worker that attributes promo clicks to Facebook, Instagram and YouTube promo links.");
        var current = FactburstTrackerSettingsStore.Load(_data.SettingsPath);

        var connection = SettingsSection("Cloudflare Worker");
        page.Children.Add(connection);
        var stack = (StackPanel)connection.Child;
        stack.Children.Add(SettingsFieldLabel("Worker base URL"));
        _settingsTrackerBaseUrl = new TextBox
        {
            Text = current.BaseUrl,
            Margin = new Thickness(0, 5, 0, 8),
            ToolTip = "Example: https://factburst-link-tracker.example.workers.dev",
        };
        stack.Children.Add(_settingsTrackerBaseUrl);
        stack.Children.Add(SettingsFieldLabel("TRACKER_API_KEY"));
        _settingsTrackerApiKey = new PasswordBox
        {
            Password = current.ApiKey,
            Margin = new Thickness(0, 5, 0, 8),
        };
        stack.Children.Add(_settingsTrackerApiKey);
        stack.Children.Add(new TextBlock
        {
            Text = "The API key is encrypted on this PC. Public tracking links never expose it.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
        });

        var behaviour = SettingsSection("Attribution behaviour");
        page.Children.Add(behaviour);
        ((StackPanel)behaviour.Child).Children.Add(new TextBlock
        {
            Text = "Promo uploads automatically create one campaign per long-form quiz. Facebook and YouTube descriptions receive source-specific links. Instagram receives its own link-in-bio URL because Reel caption URLs are not reliably clickable.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var actions = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _settingsTrackerStatus = new TextBlock
        {
            Text = current.IsConfigured ? "Tracker settings loaded." : "Enter the Worker URL and API key.",
            Foreground = SettingsMutedBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        actions.Children.Add(_settingsTrackerStatus);
        var saveAndTest = new Button
        {
            Content = "Save and test",
            MinWidth = 126,
            MinHeight = 36,
            Margin = new Thickness(10, 0, 0, 0),
        };
        saveAndTest.Click += async (_, _) => await SaveAndTestTrackerSettingsAsync(saveAndTest);
        Grid.SetColumn(saveAndTest, 1);
        actions.Children.Add(saveAndTest);
        page.Children.Add(actions);
        return SettingsScrollable(page);
    }

    private async Task SaveAndTestTrackerSettingsAsync(Button button)
    {
        if (_settingsTrackerBaseUrl is null || _settingsTrackerApiKey is null || _settingsTrackerStatus is null)
            return;
        try
        {
            button.IsEnabled = false;
            _settingsTrackerStatus.Text = "Saving and testing tracker...";
            FactburstTrackerSettingsStore.Save(
                _data.SettingsPath,
                _settingsTrackerBaseUrl.Text,
                _settingsTrackerApiKey.Password);
            var saved = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
            if (!await _factburstLinkTracker.HealthAsync(saved.BaseUrl))
                throw new InvalidOperationException("The Worker did not return a healthy response from /health.");
            var campaigns = await _factburstLinkTracker.FetchStatsAsync(saved.BaseUrl, saved.ApiKey);
            _settingsTrackerStatus.Text = $"Connected. Tracker API authenticated successfully • {campaigns.Count:N0} campaign(s).";
            if (_settingsPageStatus is not null) _settingsPageStatus.Text = "Link Tracker connected.";
        }
        catch (Exception error)
        {
            _settingsTrackerStatus.Text = error.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private bool AddFunnelPerformancePage()
    {
        if (MainTabs is null) return false;

        if (_funnelPerformanceTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildFunnelPerformancePage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _funnelPerformanceTabIndex = MainTabs.Items.Count - 1;
            MainTabs.SelectionChanged += async (_, eventArgs) =>
            {
                if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) && ReferenceEquals(MainTabs.SelectedItem, tab))
                    await RefreshFunnelPerformanceAsync(false);
            };
        }

        if (_funnelNavigationAdded) return true;
        if (_instagramManagerTabIndex < 0 || Content is not DependencyObject root) return false;
        var instagramButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _instagramManagerTabIndex.ToString(), StringComparison.Ordinal));
        if (instagramButton?.Parent is not StackPanel navigation) return false;

        var funnelButton = new Button { Content = "↗   Funnel Performance", Tag = _funnelPerformanceTabIndex.ToString() };
        if (FindResource("NavButtonStyle") is Style navStyle)
            funnelButton.Style = navStyle;
        funnelButton.Click += (_, _) =>
        {
            MainTabs.SelectedIndex = _funnelPerformanceTabIndex;
            ApplyNavigationSelection(_funnelPerformanceTabIndex);
        };
        var instagramIndex = navigation.Children.IndexOf(instagramButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, instagramIndex + 1), funnelButton);
        _funnelNavigationAdded = true;
        return true;
    }

    private FrameworkElement BuildFunnelPerformancePage()
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
            Text = "Funnel Performance",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Only quizzes with a created tracking campaign are shown. See which promo sources are sending people toward each long-form Factburst quiz.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        header.Children.Add(heading);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var backfill = new Button
        {
            Content = "Backfill existing promos",
            MinWidth = 170,
            MinHeight = 36,
            ToolTip = "Create tracking campaigns for already-published promos and replace their old full-quiz links without re-uploading media.",
        };
        StyleQuizHistoryButton(backfill, Color.FromRgb(255, 202, 45));
        backfill.Click += async (_, _) => await BackfillExistingPromosAsync(backfill);
        headerActions.Children.Add(backfill);

        var refresh = new Button
        {
            Content = "Refresh tracker",
            MinWidth = 136,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += async (_, _) => await RefreshFunnelPerformanceAsync(true);
        headerActions.Children.Add(refresh);
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 5; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddFunnelStat(stats, 0, "Tracked clicks", Color.FromRgb(255, 202, 45), out _funnelTotalClicksText);
        AddFunnelStat(stats, 1, "Facebook", Color.FromRgb(0, 140, 255), out _funnelFacebookClicksText);
        AddFunnelStat(stats, 2, "Instagram", Color.FromRgb(248, 90, 160), out _funnelInstagramClicksText);
        AddFunnelStat(stats, 3, "YouTube Promo", Color.FromRgb(248, 90, 105), out _funnelYouTubeClicksText);
        AddFunnelStat(stats, 4, "Top source", Color.FromRgb(70, 235, 115), out _funnelTopSourceText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _funnelPerformanceGrid = BuildManagerGrid();
        _funnelPerformanceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(FunnelPerformanceRow.Quiz)),
            SortMemberPath = nameof(FunnelPerformanceRow.Quiz),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _funnelPerformanceGrid.Columns.Add(TextColumn("Category", nameof(FunnelPerformanceRow.Category), 118));
        _funnelPerformanceGrid.Columns.Add(NumberColumn("Facebook", nameof(FunnelPerformanceRow.FacebookClicks), 92));
        _funnelPerformanceGrid.Columns.Add(NumberColumn("Instagram", nameof(FunnelPerformanceRow.InstagramClicks), 92));
        _funnelPerformanceGrid.Columns.Add(NumberColumn("YT Promo", nameof(FunnelPerformanceRow.YouTubePromoClicks), 88));
        _funnelPerformanceGrid.Columns.Add(NumberColumn("Tracked", nameof(FunnelPerformanceRow.TotalClicks), 82));
        _funnelPerformanceGrid.Columns.Add(NumberColumn("Long views", nameof(FunnelPerformanceRow.LongFormViews), 96));
        _funnelPerformanceGrid.Columns.Add(TextColumn("Promo", nameof(FunnelPerformanceRow.PromoStatus), 104));
        _funnelPerformanceGrid.Columns.Add(TextColumn("Signal", nameof(FunnelPerformanceRow.Signal), 218));
        var card = ManagerCard(_funnelPerformanceGrid);
        Grid.SetRow(card, 2);
        root.Children.Add(card);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _funnelStatusText = new TextBlock
        {
            Text = "Configure Settings → Link Tracker, then refresh.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(_funnelStatusText);
        footer.Children.Add(new TextBlock
        {
            Text = "Tracked clicks show source attribution. Long-form YouTube views are shown alongside them for context; the app does not claim that every tracked click became a counted YouTube view.",
            Foreground = new SolidColorBrush(Color.FromRgb(158, 180, 225)),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

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

    private static void AddFunnelStat(Grid parent, int column, string label, Color colour, out TextBlock value)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(colour),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 4 ? 0 : 5, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(184, 201, 235)), FontSize = 11 });
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

    private async Task RefreshFunnelPerformanceAsync(bool showErrors)
    {
        if (_funnelPerformanceRefreshing || _funnelPerformanceGrid is null) return;
        var settings = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!settings.IsConfigured)
        {
            SetFunnelStatus("Configure the Worker URL and TRACKER_API_KEY in Settings → Link Tracker first.");
            return;
        }

        try
        {
            _funnelPerformanceRefreshing = true;
            SetFunnelStatus("Loading source-attributed clicks from Factburst Link Tracker...");
            var remote = await _factburstLinkTracker.FetchStatsAsync(settings.BaseUrl, settings.ApiKey);
            var byQuizId = remote
                .Where(item => item.QuizId is > 0)
                .GroupBy(item => item.QuizId!.Value)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.TotalClicks).First());
            var bySlug = remote.ToDictionary(item => item.Slug, StringComparer.OrdinalIgnoreCase);
            var rows = new List<FunnelPerformanceRow>();
            foreach (var history in _data.GetQuizHistory()
                         .Where(item => item.PublishedOnYouTube && string.Equals(item.VideoType, "Video", StringComparison.Ordinal)))
            {
                var slug = FactburstLinkTrackerClient.CampaignSlug(history);
                FactburstTrackerCampaignStats? tracked = null;
                if (!byQuizId.TryGetValue(history.Id, out tracked))
                    bySlug.TryGetValue(slug, out tracked);
                if (tracked is null)
                    continue;

                var facebook = tracked.FacebookClicks;
                var instagram = tracked.InstagramClicks;
                var youtube = tracked.YouTubePromoClicks;
                var total = tracked.TotalClicks;
                var promoStatus = QuizPromoShortUploadState.Display(history.ProjectFolder);
                rows.Add(new FunnelPerformanceRow(
                    history.Id,
                    history.UploadTitleDisplay,
                    history.AnalyticsCategory,
                    facebook,
                    instagram,
                    youtube,
                    total,
                    Math.Max(0, history.YouTubeViews),
                    promoStatus,
                    FactburstFunnelClassifier.Label(total, history.YouTubeViews,
                        QuizPromoShortPaths.FindExisting(history.ProjectFolder) is not null)));
            }

            rows = rows.OrderByDescending(row => row.TotalClicks).ThenByDescending(row => row.LongFormViews).ToList();
            if (rows.Count > 0 && rows[0].TotalClicks > 0)
                rows[0] = rows[0] with { Signal = "Best tracked promo" };
            _funnelPerformanceGrid.ItemsSource = rows;

            var facebookTotal = rows.Sum(row => row.FacebookClicks);
            var instagramTotal = rows.Sum(row => row.InstagramClicks);
            var youtubeTotal = rows.Sum(row => row.YouTubePromoClicks);
            var totalClicks = facebookTotal + instagramTotal + youtubeTotal;
            _funnelTotalClicksText!.Text = totalClicks.ToString("N0");
            _funnelFacebookClicksText!.Text = facebookTotal.ToString("N0");
            _funnelInstagramClicksText!.Text = instagramTotal.ToString("N0");
            _funnelYouTubeClicksText!.Text = youtubeTotal.ToString("N0");
            _funnelTopSourceText!.Text = TopFunnelSource(facebookTotal, instagramTotal, youtubeTotal);
            SetFunnelStatus($"Loaded {remote.Count:N0} tracker campaign(s). {rows.Count:N0} linked long-form quiz row(s) are shown.");
        }
        catch (Exception error)
        {
            SetFunnelStatus(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Funnel Performance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _funnelPerformanceRefreshing = false;
        }
    }

    private static string TopFunnelSource(long facebook, long instagram, long youtube)
    {
        var best = new[]
        {
            (Name: "Facebook", Value: facebook),
            (Name: "Instagram", Value: instagram),
            (Name: "YT Promo", Value: youtube),
        }
        .OrderByDescending(item => item.Value)
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .First();
        return best.Value > 0 ? best.Name : "—";
    }

    private void SetFunnelStatus(string text)
    {
        if (_funnelStatusText is not null) _funnelStatusText.Text = text;
    }
}
