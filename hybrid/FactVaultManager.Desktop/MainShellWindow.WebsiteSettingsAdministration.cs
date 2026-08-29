using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteSettingsAdministrationInitialized;
    private DispatcherTimer? _websiteSettingsAdministrationTimer;
    private CheckBox? _websiteMaintenanceSettingsEnabled;
    private TextBox? _websiteMaintenanceSettingsMessage;
    private TextBlock? _websiteMaintenanceSettingsStatus;
    private Button? _websiteMaintenanceSettingsSaveButton;
    private DataGrid? _websiteQuestionReportsGrid;
    private ComboBox? _websiteQuestionReportsFilter;
    private TextBlock? _websiteQuestionReportsStatus;
    private Button? _websiteQuestionReportsResolveButton;
    private Button? _websiteQuestionReportsDismissButton;
    private Button? _websiteQuestionReportsReopenButton;

    public void InitializeWebsiteSettingsAdministrationPage()
    {
        if (_websiteSettingsAdministrationInitialized) return;
        _websiteSettingsAdministrationInitialized = true;

        _websiteSettingsAdministrationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _websiteSettingsAdministrationTimer.Tick += (_, _) => EnsureWebsiteSettingsAdministrationPage();
        _websiteSettingsAdministrationTimer.Start();
        Closed += (_, _) => _websiteSettingsAdministrationTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteSettingsAdministrationPage));
    }

    private void EnsureWebsiteSettingsAdministrationPage()
    {
        if (_settingsContentHost is null ||
            !_settingsNavButtons.TryGetValue("about", out var aboutButton) ||
            aboutButton.Parent is not Panel sidebar)
        {
            return;
        }

        if (!_settingsPages.ContainsKey("website"))
        {
            _settingsPages["website"] = BuildWebsiteSettingsAdministrationPage();
            AddSettingsNav(sidebar, "website", "Website");
            if (_settingsNavButtons.TryGetValue("website", out var websiteButton))
            {
                var aboutIndex = sidebar.Children.IndexOf(aboutButton);
                sidebar.Children.Remove(websiteButton);
                sidebar.Children.Insert(Math.Max(0, aboutIndex), websiteButton);
                websiteButton.Click += async (_, _) => await RefreshWebsiteAdministrationSettingsAsync(false);
            }
        }

        // Maintenance is a site-level setting, not a user-management action.
        // Keep the existing field alive so the legacy injector does not recreate it,
        // but remove its visual button from the Users page.
        if (_websiteMaintenanceButton?.Parent is Panel usersHeader)
            usersHeader.Children.Remove(_websiteMaintenanceButton);

        if (_websiteMaintenanceButton is not null && _settingsPages.ContainsKey("website"))
            _websiteSettingsAdministrationTimer?.Stop();
    }

    private FrameworkElement BuildWebsiteSettingsAdministrationPage()
    {
        var page = SettingsPageStack("Website", "Manage Factburst website availability, maintenance mode and reported quiz questions.");

        var maintenance = SettingsSection("Maintenance mode");
        page.Children.Add(maintenance);
        var maintenanceStack = (StackPanel)maintenance.Child;
        _websiteMaintenanceSettingsEnabled = new CheckBox
        {
            Content = "Put the entire Factburst Quiz website into maintenance mode",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 8),
        };
        maintenanceStack.Children.Add(_websiteMaintenanceSettingsEnabled);
        maintenanceStack.Children.Add(new TextBlock
        {
            Text = "Normal visitors and signed-in users will see only the maintenance notice. Administrators can still access the full site and will see the maintenance banner.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        maintenanceStack.Children.Add(SettingsFieldLabel("Maintenance message"));
        _websiteMaintenanceSettingsMessage = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 92,
            MaxLength = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 5, 0, 10),
        };
        maintenanceStack.Children.Add(_websiteMaintenanceSettingsMessage);
        var maintenanceActions = new StackPanel { Orientation = Orientation.Horizontal };
        var refreshMaintenance = new Button { Content = "Refresh", MinWidth = 90, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        refreshMaintenance.Click += async (_, _) => await RefreshWebsiteMaintenanceSettingsPageAsync(true);
        _websiteMaintenanceSettingsSaveButton = new Button { Content = "Save maintenance settings", MinWidth = 170, MinHeight = 34 };
        _websiteMaintenanceSettingsSaveButton.Click += async (_, _) => await SaveWebsiteMaintenanceSettingsPageAsync();
        maintenanceActions.Children.Add(refreshMaintenance);
        maintenanceActions.Children.Add(_websiteMaintenanceSettingsSaveButton);
        maintenanceStack.Children.Add(maintenanceActions);
        _websiteMaintenanceSettingsStatus = new TextBlock
        {
            Text = "Maintenance status will appear here.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        };
        maintenanceStack.Children.Add(_websiteMaintenanceSettingsStatus);

        var reports = SettingsSection("Question reports");
        page.Children.Add(reports);
        var reportsStack = (StackPanel)reports.Child;
        reportsStack.Children.Add(new TextBlock
        {
            Text = "Questions reported by players appear here for review. Resolve valid reports after correcting the question, dismiss reports that need no action, or reopen a report if needed.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 10),
        });

        var reportToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
        _websiteQuestionReportsFilter = new ComboBox { Width = 125, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        _websiteQuestionReportsFilter.Items.Add(new ComboBoxItem { Content = "Open", Tag = "open" });
        _websiteQuestionReportsFilter.Items.Add(new ComboBoxItem { Content = "All", Tag = "all" });
        _websiteQuestionReportsFilter.Items.Add(new ComboBoxItem { Content = "Resolved", Tag = "resolved" });
        _websiteQuestionReportsFilter.Items.Add(new ComboBoxItem { Content = "Dismissed", Tag = "dismissed" });
        _websiteQuestionReportsFilter.SelectedIndex = 0;
        _websiteQuestionReportsFilter.SelectionChanged += async (_, _) => await RefreshWebsiteQuestionReportsAsync(false);
        var refreshReports = new Button { Content = "Refresh reports", MinWidth = 112, MinHeight = 34 };
        refreshReports.Click += async (_, _) => await RefreshWebsiteQuestionReportsAsync(true);
        reportToolbar.Children.Add(_websiteQuestionReportsFilter);
        reportToolbar.Children.Add(refreshReports);
        reportsStack.Children.Add(reportToolbar);

        _websiteQuestionReportsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            MinHeight = 285,
            MaxHeight = 380,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        };
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.QuizTitle)),
            Width = new DataGridLength(180),
        });
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.QuestionPosition)),
            Width = new DataGridLength(42),
        });
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Question",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.QuestionText)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Reason",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.Reason)),
            Width = new DataGridLength(90),
        });
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Reporter",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.Reporter)),
            Width = new DataGridLength(110),
        });
        _websiteQuestionReportsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(FactburstWebsiteQuestionReport.Status)),
            Width = new DataGridLength(82),
        });
        _websiteQuestionReportsGrid.SelectionChanged += (_, _) => UpdateWebsiteQuestionReportButtons();
        reportsStack.Children.Add(_websiteQuestionReportsGrid);

        var reportActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _websiteQuestionReportsResolveButton = new Button { Content = "Mark resolved", MinWidth = 112, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _websiteQuestionReportsDismissButton = new Button { Content = "Dismiss", MinWidth = 90, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _websiteQuestionReportsReopenButton = new Button { Content = "Reopen", MinWidth = 90, MinHeight = 34, IsEnabled = false };
        _websiteQuestionReportsResolveButton.Click += async (_, _) => await SetSelectedWebsiteQuestionReportStatusAsync("resolved");
        _websiteQuestionReportsDismissButton.Click += async (_, _) => await SetSelectedWebsiteQuestionReportStatusAsync("dismissed");
        _websiteQuestionReportsReopenButton.Click += async (_, _) => await SetSelectedWebsiteQuestionReportStatusAsync("open");
        reportActions.Children.Add(_websiteQuestionReportsResolveButton);
        reportActions.Children.Add(_websiteQuestionReportsDismissButton);
        reportActions.Children.Add(_websiteQuestionReportsReopenButton);
        reportsStack.Children.Add(reportActions);

        _websiteQuestionReportsStatus = new TextBlock
        {
            Text = "Question report status will appear here.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        };
        reportsStack.Children.Add(_websiteQuestionReportsStatus);

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ = RefreshWebsiteAdministrationSettingsAsync(false)));
        return SettingsScrollable(page);
    }

    private async Task RefreshWebsiteAdministrationSettingsAsync(bool showErrors)
    {
        await RefreshWebsiteMaintenanceSettingsPageAsync(showErrors);
        await RefreshWebsiteQuestionReportsAsync(showErrors);
    }

    private async Task RefreshWebsiteMaintenanceSettingsPageAsync(bool showErrors)
    {
        if (_websiteMaintenanceSettingsEnabled is null || _websiteMaintenanceSettingsMessage is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            _websiteMaintenanceSettingsEnabled.IsEnabled = false;
            _websiteMaintenanceSettingsMessage.IsEnabled = false;
            if (_websiteMaintenanceSettingsSaveButton is not null) _websiteMaintenanceSettingsSaveButton.IsEnabled = false;
            if (_websiteMaintenanceSettingsStatus is not null)
                _websiteMaintenanceSettingsStatus.Text = "Configure the Link Tracker API key before changing website maintenance mode.";
            return;
        }

        try
        {
            using var client = new FactburstWebsiteAccessAdminClient();
            var settings = await client.GetMaintenanceAsync(tracker.BaseUrl, tracker.ApiKey);
            _websiteMaintenanceSettingsEnabled.IsEnabled = true;
            _websiteMaintenanceSettingsMessage.IsEnabled = true;
            if (_websiteMaintenanceSettingsSaveButton is not null) _websiteMaintenanceSettingsSaveButton.IsEnabled = true;
            _websiteMaintenanceSettingsEnabled.IsChecked = settings.Enabled;
            _websiteMaintenanceSettingsMessage.Text = settings.Message;
            if (_websiteMaintenanceSettingsStatus is not null)
                _websiteMaintenanceSettingsStatus.Text = settings.Enabled ? "Maintenance mode is ON." : "Maintenance mode is off.";
        }
        catch (Exception error)
        {
            if (_websiteMaintenanceSettingsStatus is not null) _websiteMaintenanceSettingsStatus.Text = error.Message;
            if (showErrors)
                MessageBox.Show(this, error.Message, "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveWebsiteMaintenanceSettingsPageAsync()
    {
        if (_websiteMaintenanceSettingsEnabled is null || _websiteMaintenanceSettingsMessage is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Link Tracker is not configured. Add the tracker API key in Website settings first.", "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (_websiteMaintenanceSettingsSaveButton is not null) _websiteMaintenanceSettingsSaveButton.IsEnabled = false;
            using var client = new FactburstWebsiteAccessAdminClient();
            var updated = await client.SetMaintenanceAsync(
                tracker.BaseUrl,
                tracker.ApiKey,
                _websiteMaintenanceSettingsEnabled.IsChecked == true,
                _websiteMaintenanceSettingsMessage.Text);
            _websiteMaintenanceSettingsEnabled.IsChecked = updated.Enabled;
            _websiteMaintenanceSettingsMessage.Text = updated.Message;
            if (_websiteMaintenanceSettingsStatus is not null)
                _websiteMaintenanceSettingsStatus.Text = updated.Enabled ? "Maintenance mode is ON. Normal visitors are blocked from the full website." : "Maintenance mode is off. The website is publicly available.";
        }
        catch (Exception error)
        {
            if (_websiteMaintenanceSettingsStatus is not null) _websiteMaintenanceSettingsStatus.Text = error.Message;
            MessageBox.Show(this, error.Message, "Website Maintenance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (_websiteMaintenanceSettingsSaveButton is not null) _websiteMaintenanceSettingsSaveButton.IsEnabled = true;
        }
    }

    private async Task RefreshWebsiteQuestionReportsAsync(bool showErrors)
    {
        if (_websiteQuestionReportsGrid is null) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            _websiteQuestionReportsGrid.ItemsSource = Array.Empty<FactburstWebsiteQuestionReport>();
            if (_websiteQuestionReportsStatus is not null)
                _websiteQuestionReportsStatus.Text = "Configure the Link Tracker API key to view reported questions.";
            UpdateWebsiteQuestionReportButtons();
            return;
        }

        var status = (_websiteQuestionReportsFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "open";
        try
        {
            using var client = new FactburstWebsiteQuestionReportsAdminClient();
            var result = await client.FetchAsync(tracker.BaseUrl, tracker.ApiKey, status);
            _websiteQuestionReportsGrid.ItemsSource = result.Reports;
            if (_websiteQuestionReportsStatus is not null)
                _websiteQuestionReportsStatus.Text = $"{result.Summary.Open:N0} open • {result.Summary.Resolved:N0} resolved • {result.Summary.Dismissed:N0} dismissed";
            UpdateWebsiteQuestionReportButtons();
        }
        catch (Exception error)
        {
            if (_websiteQuestionReportsStatus is not null) _websiteQuestionReportsStatus.Text = error.Message;
            if (showErrors)
                MessageBox.Show(this, error.Message, "Question Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateWebsiteQuestionReportButtons()
    {
        var selected = _websiteQuestionReportsGrid?.SelectedItem as FactburstWebsiteQuestionReport;
        if (_websiteQuestionReportsResolveButton is not null)
            _websiteQuestionReportsResolveButton.IsEnabled = selected is not null && !string.Equals(selected.Status, "resolved", StringComparison.OrdinalIgnoreCase);
        if (_websiteQuestionReportsDismissButton is not null)
            _websiteQuestionReportsDismissButton.IsEnabled = selected is not null && !string.Equals(selected.Status, "dismissed", StringComparison.OrdinalIgnoreCase);
        if (_websiteQuestionReportsReopenButton is not null)
            _websiteQuestionReportsReopenButton.IsEnabled = selected is not null && !string.Equals(selected.Status, "open", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SetSelectedWebsiteQuestionReportStatusAsync(string status)
    {
        if (_websiteQuestionReportsGrid?.SelectedItem is not FactburstWebsiteQuestionReport selected) return;
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured) return;

        try
        {
            using var client = new FactburstWebsiteQuestionReportsAdminClient();
            await client.SetStatusAsync(tracker.BaseUrl, tracker.ApiKey, selected.Id, status);
            await RefreshWebsiteQuestionReportsAsync(false);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Question Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
