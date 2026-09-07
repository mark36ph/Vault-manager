using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsDashboardAttached;
    private bool _performanceDiagnosticsDashboardBusy;
    private TextBlock? _performanceDiagnosticsDashboardSummary;
    private Button? _performanceDiagnosticsDashboardProfileButton;
    private Button? _performanceDiagnosticsDashboardFullButton;
    private Button? _performanceDiagnosticsDashboardScanButton;
    private Button? _performanceDiagnosticsDashboardCopyButton;
    private Button? _performanceDiagnosticsDashboardClearButton;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void InitializePerformanceDiagnosticsDashboard()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPerformanceDiagnosticsDashboardLoaded),
            handledEventsToo: true);
    }

    private static void OnPerformanceDiagnosticsDashboardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (Window.GetWindow(textBox) is MainShellWindow window &&
            ReferenceEquals(textBox, window._performanceDiagnosticsResults))
        {
            window.AttachPerformanceDiagnosticsDashboard();
        }
    }

    private void AttachPerformanceDiagnosticsDashboard()
    {
        if (_performanceDiagnosticsDashboardAttached || _performanceDiagnosticsResults is null)
            return;

        _performanceDiagnosticsDashboardAttached = true;

        foreach (var button in FindVisualChildren<Button>(this)
                     .Where(button => button.Content?.ToString() is
                         "Scan app now" or
                         "Benchmark navigation" or
                         "Run full performance profile" or
                         "Profile next startup" or
                         "Write report now" or
                         "Open diagnostics folder"))
        {
            button.Visibility = Visibility.Collapsed;
        }

        var resultsStack = FindVisualParent<StackPanel>(_performanceDiagnosticsResults);
        var pageStack = resultsStack is null ? null : FindVisualParent<StackPanel>(resultsStack);
        if (pageStack is null)
            return;

        var dashboard = SettingsSection("Diagnostic Dashboard");
        var dashboardContent = (StackPanel)dashboard.Child;
        dashboardContent.Children.Add(new TextBlock
        {
            Text = "A simpler control panel for diagnosing freezes and slow pages. The current-sidebar profile measures the navigation you actually see, while the full profile also stresses the quiz database.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        _performanceDiagnosticsDashboardSummary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = FontWeights.SemiBold,
        };
        dashboardContent.Children.Add(_performanceDiagnosticsDashboardSummary);

        var actions = new UniformGrid
        {
            Columns = 2,
            Rows = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        dashboardContent.Children.Add(actions);

        _performanceDiagnosticsDashboardScanButton = CreateDiagnosticsDashboardButton("Quick UI health scan", async () =>
        {
            SetDiagnosticsDashboardBusy(true, "Scanning the current WPF visual tree...");
            try
            {
                ScanPerformanceDiagnostics();
                UpdatePerformanceDiagnosticsDashboardSummary();
                await YieldDiagnosticsUiAsync();
            }
            finally
            {
                SetDiagnosticsDashboardBusy(false);
            }
        });
        actions.Children.Add(_performanceDiagnosticsDashboardScanButton);

        _performanceDiagnosticsDashboardProfileButton = CreateDiagnosticsDashboardButton("Profile current sidebar", async () =>
        {
            SetDiagnosticsDashboardBusy(true, "Profiling the visible Factburst sidebar...");
            try
            {
                await RunNavigationHotspotProfileAsync();
                UpdatePerformanceDiagnosticsDashboardSummary();
            }
            finally
            {
                SetDiagnosticsDashboardBusy(false);
            }
        });
        actions.Children.Add(_performanceDiagnosticsDashboardProfileButton);

        _performanceDiagnosticsDashboardFullButton = CreateDiagnosticsDashboardButton("Run full performance profile", async () =>
        {
            SetDiagnosticsDashboardBusy(true, "Running the full performance profile...");
            try
            {
                await RunFullPerformanceProfileAsync();
                UpdatePerformanceDiagnosticsDashboardSummary();
            }
            finally
            {
                SetDiagnosticsDashboardBusy(false);
            }
        });
        actions.Children.Add(_performanceDiagnosticsDashboardFullButton);

        actions.Children.Add(CreateDiagnosticsDashboardButton("Profile next startup", () =>
        {
            PerformanceDiagnostics.RequestStartupProfile();
            if (_performanceDiagnosticsStatus is not null)
                _performanceDiagnosticsStatus.Text = "Startup profiling is armed. Restart the app, then open Performance Diagnostics to review the launch profile.";
            UpdatePerformanceDiagnosticsDashboardSummary("Startup profile armed");
            return Task.CompletedTask;
        }));

        _performanceDiagnosticsDashboardClearButton = CreateDiagnosticsDashboardButton("Clear measurements", () =>
        {
            PerformanceDiagnostics.Reset();
            if (_performanceDiagnosticsResults is not null)
                _performanceDiagnosticsResults.Text = "Measurements cleared. Run a health scan or profile to collect fresh data.";
            UpdatePerformanceDiagnosticsDashboardSummary("Measurements cleared");
            return Task.CompletedTask;
        });
        actions.Children.Add(_performanceDiagnosticsDashboardClearButton);

        _performanceDiagnosticsDashboardCopyButton = CreateDiagnosticsDashboardButton("Copy results", () =>
        {
            try
            {
                Clipboard.SetText(_performanceDiagnosticsResults?.Text ?? string.Empty);
                UpdatePerformanceDiagnosticsDashboardSummary("Results copied to clipboard");
            }
            catch
            {
                UpdatePerformanceDiagnosticsDashboardSummary("Clipboard is unavailable");
            }
            return Task.CompletedTask;
        });
        actions.Children.Add(_performanceDiagnosticsDashboardCopyButton);

        actions.Children.Add(CreateDiagnosticsDashboardButton("Write report", () =>
        {
            if (!PerformanceDiagnostics.Enabled)
            {
                UpdatePerformanceDiagnosticsDashboardSummary("Enable diagnostics before writing a report");
                return Task.CompletedTask;
            }

            var path = PerformanceDiagnostics.WriteReport();
            UpdatePerformanceDiagnosticsDashboardSummary(path is null ? "Report could not be written" : $"Report written: {path}");
            return Task.CompletedTask;
        }));

        actions.Children.Add(CreateDiagnosticsDashboardButton("Open diagnostics folder", () =>
        {
            OpenPerformanceDiagnosticsFolder();
            return Task.CompletedTask;
        }));

        pageStack.Children.Add(dashboard);
        UpdatePerformanceDiagnosticsDashboardSummary();
    }

    private Button CreateDiagnosticsDashboardButton(string content, Func<Task> action)
    {
        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 10, 10),
            MinHeight = 42,
            FontWeight = FontWeights.SemiBold,
        };
        button.Click += async (_, _) =>
        {
            if (_performanceDiagnosticsDashboardBusy)
                return;

            await action();
        };
        return button;
    }

    private void SetDiagnosticsDashboardBusy(bool busy, string? message = null)
    {
        _performanceDiagnosticsDashboardBusy = busy;
        if (!string.IsNullOrWhiteSpace(message))
            UpdatePerformanceDiagnosticsDashboardSummary(message);

        foreach (var button in new[]
                 {
                     _performanceDiagnosticsDashboardScanButton,
                     _performanceDiagnosticsDashboardProfileButton,
                     _performanceDiagnosticsDashboardFullButton,
                     _performanceDiagnosticsDashboardClearButton,
                     _performanceDiagnosticsDashboardCopyButton,
                 })
        {
            if (button is not null)
                button.IsEnabled = !busy;
        }
    }

    private void UpdatePerformanceDiagnosticsDashboardSummary(string? message = null)
    {
        if (_performanceDiagnosticsDashboardSummary is null)
            return;

        var report = PerformanceDiagnostics.GetReport();
        var operationLines = report
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(4)
            .Count(line => line.Contains(" | ", StringComparison.Ordinal));
        var slowCount = report
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(4)
            .Count(line =>
            {
                var parts = line.Split('|');
                return parts.Length >= 5 && double.TryParse(parts[^1].Trim(), out var max) && max >= 50;
            });

        _performanceDiagnosticsDashboardSummary.Text = message is not null
            ? $"{message}\nDiagnostics: {(PerformanceDiagnostics.Enabled ? "ON" : "OFF")} • Measured operations: {operationLines:N0} • Slow operations (50 ms+): {slowCount:N0}"
            : $"Diagnostics: {(PerformanceDiagnostics.Enabled ? "ON" : "OFF")} • Measured operations: {operationLines:N0} • Slow operations (50 ms+): {slowCount:N0}\nTip: use Profile current sidebar for the navigation that is actually visible in the app.";
    }

    private async Task YieldDiagnosticsUiAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
