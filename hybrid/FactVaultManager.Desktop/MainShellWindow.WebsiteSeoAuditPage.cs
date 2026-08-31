using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteSeoAuditInitialized;
    private int _websiteSeoAuditTabIndex = -1;
    private DispatcherTimer? _websiteSeoAuditNavigationTimer;
    private DataGrid? _websiteSeoAuditGrid;
    private TextBlock? _websiteSeoAuditTotalText;
    private TextBlock? _websiteSeoAuditTotalNoteText;
    private TextBlock? _websiteSeoAuditReadyText;
    private TextBlock? _websiteSeoAuditReadyNoteText;
    private TextBlock? _websiteSeoAuditWarningsText;
    private TextBlock? _websiteSeoAuditWarningsNoteText;
    private TextBlock? _websiteSeoAuditNeedsAttentionText;
    private TextBlock? _websiteSeoAuditNeedsAttentionNoteText;
    private TextBlock? _websiteSeoAuditStatusText;
    private Button? _websiteSeoAuditEditButton;
    private IReadOnlyList<FactburstWebsiteSeoQuiz> _websiteSeoAuditQuizzes = Array.Empty<FactburstWebsiteSeoQuiz>();

    public void InitializeWebsiteSeoAuditPage()
    {
        if (_websiteSeoAuditInitialized) return;
        _websiteSeoAuditInitialized = true;
        SuppressLegacyWebsiteSeoButton();

        _websiteSeoAuditNavigationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _websiteSeoAuditNavigationTimer.Tick += (_, _) => EnsureWebsiteSeoAuditPage();
        _websiteSeoAuditNavigationTimer.Start();
        Closed += (_, _) => _websiteSeoAuditNavigationTimer?.Stop();

        MainTabs.SelectionChanged += async (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs)) return;
            if (MainTabs.SelectedIndex == _websiteSeoAuditTabIndex)
                await RefreshWebsiteSeoAuditAsync(false);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteSeoAuditPage));
    }

    private void SuppressLegacyWebsiteSeoButton()
    {
        // SEO now has its own Website administration page. Prevent the older
        // selected-row footer button from being installed (or remove it if its
        // Loaded handler happened to run first).
        _websiteSeoPublishingInitialized = true;
        _websiteSeoPublishingTimer?.Stop();
        if (_websiteSeoButton?.Parent is Panel parent)
            parent.Children.Remove(_websiteSeoButton);
        _websiteSeoButton = null;
    }

    private void EnsureWebsiteSeoAuditPage()
    {
        SuppressLegacyWebsiteSeoButton();
        if (_autopilotNavContainer is null || _autopilotNavContainer.Parent is null) return;

        if (_websiteSeoAuditTabIndex < 0)
        {
            var tab = new TabItem { Content = BuildWebsiteSeoAuditPage() };
            if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
                tab.Style = hiddenStyle;
            MainTabs.Items.Add(tab);
            _websiteSeoAuditTabIndex = MainTabs.Items.Count - 1;
        }

        // Comments is deliberately the anchor so the website-management order is:
        // Website -> Users -> Comments -> SEO.
        if (!_autopilotNavButtons.TryGetValue("Comments", out var commentsButton)) return;

        if (!_autopilotNavButtons.TryGetValue("SEO", out var seoButton))
        {
            seoButton = new Button
            {
                Content = "⌕   SEO",
                Tag = AutopilotFirstNavTag + ":SEO",
                ToolTip = "Audit search and social metadata across every website quiz",
            };
            if (FindResource("NavButtonStyle") is Style navStyle)
                seoButton.Style = navStyle;
            seoButton.Click += (_, _) => NavigateWebsiteSeoAudit();
            _autopilotNavButtons["SEO"] = seoButton;
        }

        var commentsIndex = _autopilotNavContainer.Children.IndexOf(commentsButton);
        var currentIndex = _autopilotNavContainer.Children.IndexOf(seoButton);
        if (commentsIndex < 0) return;
        if (currentIndex != commentsIndex + 1)
        {
            if (currentIndex >= 0)
                _autopilotNavContainer.Children.Remove(seoButton);
            commentsIndex = _autopilotNavContainer.Children.IndexOf(commentsButton);
            _autopilotNavContainer.Children.Insert(
                Math.Clamp(commentsIndex + 1, 0, _autopilotNavContainer.Children.Count),
                seoButton);
        }

        var finalCommentsIndex = _autopilotNavContainer.Children.IndexOf(commentsButton);
        var finalSeoIndex = _autopilotNavContainer.Children.IndexOf(seoButton);
        if (finalCommentsIndex >= 0 && finalSeoIndex == finalCommentsIndex + 1)
            _websiteSeoAuditNavigationTimer?.Stop();
    }

    private void NavigateWebsiteSeoAudit()
    {
        EnsureWebsiteSeoAuditPage();
        if (_websiteSeoAuditTabIndex < 0) return;
        MainTabs.SelectedIndex = _websiteSeoAuditTabIndex;
        SelectAutopilotNav("SEO");
        _ = RefreshWebsiteSeoAuditAsync(false);
    }

    private FrameworkElement BuildWebsiteSeoAuditPage()
    {
        var root = new Grid { Margin = new Thickness(26, 22, 26, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "SEO",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Audit search and social metadata across every Factburst website quiz at once. Fix only the rows that need attention.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 20, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var refresh = new Button
        {
            Content = "Refresh audit",
            MinWidth = 108,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
        };
        refresh.Click += async (_, _) => await RefreshWebsiteSeoAuditAsync(true);
        _websiteSeoAuditEditButton = new Button
        {
            Content = "Edit selected",
            MinWidth = 112,
            MinHeight = 36,
            IsEnabled = false,
            ToolTip = "Open the search and social preview editor for the selected quiz",
        };
        _websiteSeoAuditEditButton.Click += async (_, _) => await EditSelectedWebsiteSeoAuditAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(_websiteSeoAuditEditButton);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        for (var index = 0; index < 4; index++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddWebsiteSeoAuditStat(stats, 0, "All quizzes", out _websiteSeoAuditTotalText, out _websiteSeoAuditTotalNoteText);
        AddWebsiteSeoAuditStat(stats, 1, "Ready", out _websiteSeoAuditReadyText, out _websiteSeoAuditReadyNoteText);
        AddWebsiteSeoAuditStat(stats, 2, "Warnings", out _websiteSeoAuditWarningsText, out _websiteSeoAuditWarningsNoteText);
        AddWebsiteSeoAuditStat(stats, 3, "Needs attention", out _websiteSeoAuditNeedsAttentionText, out _websiteSeoAuditNeedsAttentionNoteText);
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        _websiteSeoAuditGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
            RowHeaderWidth = 0,
        };
        _websiteSeoAuditGrid.Columns.Add(SeoAuditTextColumn("Status", nameof(WebsiteSeoAuditRow.Status), 116));
        _websiteSeoAuditGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(WebsiteSeoAuditRow.Quiz)),
            Width = new DataGridLength(1.15, DataGridLengthUnitType.Star),
        });
        _websiteSeoAuditGrid.Columns.Add(SeoAuditTextColumn("Category", nameof(WebsiteSeoAuditRow.Category), 130));
        _websiteSeoAuditGrid.Columns.Add(SeoAuditTextColumn("SEO mode", nameof(WebsiteSeoAuditRow.Mode), 92));
        _websiteSeoAuditGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "SEO title",
            Binding = new Binding(nameof(WebsiteSeoAuditRow.SeoTitle)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
        });
        _websiteSeoAuditGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Audit result",
            Binding = new Binding(nameof(WebsiteSeoAuditRow.Issues)),
            Width = new DataGridLength(1.7, DataGridLengthUnitType.Star),
        });
        _websiteSeoAuditGrid.Columns.Add(SeoAuditTextColumn("Slug", nameof(WebsiteSeoAuditRow.Slug), 170));
        _websiteSeoAuditGrid.SelectionChanged += (_, _) => UpdateWebsiteSeoAuditSelection();
        _websiteSeoAuditGrid.MouseDoubleClick += async (_, _) =>
        {
            if (_websiteSeoAuditGrid.SelectedItem is WebsiteSeoAuditRow)
                await EditSelectedWebsiteSeoAuditAsync();
        };

        var gridCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(1),
            Child = _websiteSeoAuditGrid,
        };
        Grid.SetRow(gridCard, 2);
        root.Children.Add(gridCard);

        _websiteSeoAuditStatusText = new TextBlock
        {
            Text = "Open SEO to audit every website quiz. Automatic defaults are valid; warnings only identify metadata worth reviewing.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(_websiteSeoAuditStatusText, 3);
        root.Children.Add(_websiteSeoAuditStatusText);

        return root;
    }

    private async Task RefreshWebsiteSeoAuditAsync(bool showErrors)
    {
        if (_websiteSeoAuditGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            SetWebsiteSeoAuditUnavailable("Configure Settings → Link Tracker first to audit website SEO.");
            return;
        }

        try
        {
            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = "Checking search and social metadata across all website quizzes…";

            using var client = new FactburstWebsiteSeoAdminClient();
            _websiteSeoAuditQuizzes = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey);
            var rows = FactburstWebsiteSeoAudit.Build(_websiteSeoAuditQuizzes);
            var summary = FactburstWebsiteSeoAudit.Summarize(rows);
            _websiteSeoAuditGrid.ItemsSource = rows;

            if (_websiteSeoAuditTotalText is not null) _websiteSeoAuditTotalText.Text = summary.Total.ToString("N0");
            if (_websiteSeoAuditTotalNoteText is not null)
                _websiteSeoAuditTotalNoteText.Text = $"{summary.Custom:N0} custom • {summary.Automatic:N0} automatic";
            if (_websiteSeoAuditReadyText is not null) _websiteSeoAuditReadyText.Text = summary.Ready.ToString("N0");
            if (_websiteSeoAuditReadyNoteText is not null) _websiteSeoAuditReadyNoteText.Text = "no SEO issues found";
            if (_websiteSeoAuditWarningsText is not null) _websiteSeoAuditWarningsText.Text = summary.Warnings.ToString("N0");
            if (_websiteSeoAuditWarningsNoteText is not null) _websiteSeoAuditWarningsNoteText.Text = "worth reviewing";
            if (_websiteSeoAuditNeedsAttentionText is not null) _websiteSeoAuditNeedsAttentionText.Text = summary.NeedsAttention.ToString("N0");
            if (_websiteSeoAuditNeedsAttentionNoteText is not null) _websiteSeoAuditNeedsAttentionNoteText.Text = "structural SEO issues";
            if (_websiteSeoAuditStatusText is not null)
            {
                _websiteSeoAuditStatusText.Text = summary.Total == 0
                    ? "No website quizzes were returned by Cloudflare."
                    : $"Audited {summary.Total:N0} quizzes • {summary.Ready:N0} ready • {summary.Warnings:N0} warnings • {summary.NeedsAttention:N0} need attention. Double-click any row to review its search and social preview.";
            }
            UpdateWebsiteSeoAuditSelection();
        }
        catch (Exception error)
        {
            SetWebsiteSeoAuditUnavailable(error.Message);
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website SEO", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EditSelectedWebsiteSeoAuditAsync()
    {
        if (_websiteSeoAuditGrid?.SelectedItem is not WebsiteSeoAuditRow row) return;
        const string title = "Website Quiz SEO";
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_websiteSeoAuditEditButton is not null) _websiteSeoAuditEditButton.IsEnabled = false;
            var dialog = new WebsiteQuizSeoDialog(row.Source, _websiteSeoAuditQuizzes) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultValues is null) return;

            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = $"Saving SEO for {row.Quiz}…";
            using var client = new FactburstWebsiteSeoAdminClient();
            await client.UpdateAsync(tracker.BaseUrl, tracker.ApiKey, row.Slug, dialog.ResultValues);
            await RefreshWebsiteSeoAuditAsync(false);
            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = $"SEO saved for {row.Quiz}. The full catalogue audit has been refreshed.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = "SEO update failed: " + error.Message;
        }
        finally
        {
            UpdateWebsiteSeoAuditSelection();
        }
    }

    private void SetWebsiteSeoAuditUnavailable(string note)
    {
        _websiteSeoAuditQuizzes = Array.Empty<FactburstWebsiteSeoQuiz>();
        if (_websiteSeoAuditGrid is not null) _websiteSeoAuditGrid.ItemsSource = null;
        if (_websiteSeoAuditTotalText is not null) _websiteSeoAuditTotalText.Text = "—";
        if (_websiteSeoAuditReadyText is not null) _websiteSeoAuditReadyText.Text = "—";
        if (_websiteSeoAuditWarningsText is not null) _websiteSeoAuditWarningsText.Text = "—";
        if (_websiteSeoAuditNeedsAttentionText is not null) _websiteSeoAuditNeedsAttentionText.Text = "—";
        if (_websiteSeoAuditTotalNoteText is not null) _websiteSeoAuditTotalNoteText.Text = "Website inventory unavailable";
        if (_websiteSeoAuditReadyNoteText is not null) _websiteSeoAuditReadyNoteText.Text = "—";
        if (_websiteSeoAuditWarningsNoteText is not null) _websiteSeoAuditWarningsNoteText.Text = "—";
        if (_websiteSeoAuditNeedsAttentionNoteText is not null) _websiteSeoAuditNeedsAttentionNoteText.Text = "—";
        if (_websiteSeoAuditStatusText is not null) _websiteSeoAuditStatusText.Text = note;
        UpdateWebsiteSeoAuditSelection();
    }

    private void UpdateWebsiteSeoAuditSelection()
    {
        if (_websiteSeoAuditEditButton is not null)
            _websiteSeoAuditEditButton.IsEnabled = _websiteSeoAuditGrid?.SelectedItem is WebsiteSeoAuditRow;
    }

    private static DataGridTextColumn SeoAuditTextColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width),
    };

    private static void AddWebsiteSeoAuditStat(
        Grid parent,
        int column,
        string label,
        out TextBlock value,
        out TextBlock note)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        });
        value = new TextBlock
        {
            Text = "—",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(0, 5, 0, 0),
        };
        note = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0),
        };
        stack.Children.Add(value);
        stack.Children.Add(note);
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }
}
