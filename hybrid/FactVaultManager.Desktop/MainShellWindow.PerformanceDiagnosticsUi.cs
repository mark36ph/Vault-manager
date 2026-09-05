using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsUiInitialized;
    private TextBlock? _performanceDiagnosticsStatus;
    private Button? _performanceDiagnosticsToggleButton;

    private void InitializePerformanceDiagnosticsUi()
    {
        if (_performanceDiagnosticsUiInitialized)
            return;

        if (!_settingsWorkflowInitialized || !_settingsNavButtons.TryGetValue("about", out var aboutButton))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(InitializePerformanceDiagnosticsUi));
            return;
        }

        if (aboutButton.Parent is not Panel sidebar)
            return;

        _settingsPages["performance"] = BuildPerformanceDiagnosticsPage();
        AddSettingsNav(sidebar, "performance", "Performance Diagnostics");
        _performanceDiagnosticsUiInitialized = true;
        UpdatePerformanceDiagnosticsUi();
    }

    private FrameworkElement BuildPerformanceDiagnosticsPage()
    {
        var page = SettingsPageStack(
            "Performance Diagnostics",
            "Measure startup and navigation timings to find slow UI operations. Diagnostics stay off until you enable them.");

        var controls = SettingsSection("Diagnostics");
        page.Children.Add(controls);
        var stack = (StackPanel)controls.Child;

        _performanceDiagnosticsStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = SettingsMutedBrush(),
            Margin = new Thickness(0, 6, 0, 10),
        };
        stack.Children.Add(_performanceDiagnosticsStatus);

        _performanceDiagnosticsToggleButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 170,
        };
        _performanceDiagnosticsToggleButton.Click += (_, _) =>
        {
            PerformanceDiagnostics.SetEnabled(!PerformanceDiagnostics.Enabled);
            UpdatePerformanceDiagnosticsUi();
        };
        stack.Children.Add(_performanceDiagnosticsToggleButton);

        var reportButton = new Button
        {
            Content = "Write report now",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 170,
        };
        reportButton.Click += (_, _) =>
        {
            if (!PerformanceDiagnostics.Enabled)
            {
                _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then use Write report now.";
                return;
            }

            var path = PerformanceDiagnostics.WriteReport();
            _performanceDiagnosticsStatus!.Text = path is null
                ? "The diagnostics report could not be written."
                : $"Report written to:\n{path}";
        };
        stack.Children.Add(reportButton);

        var folderButton = new Button
        {
            Content = "Open diagnostics folder",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 170,
        };
        folderButton.Click += (_, _) => OpenPerformanceDiagnosticsFolder();
        stack.Children.Add(folderButton);

        var info = SettingsSection("How to use it");
        page.Children.Add(info);
        ((StackPanel)info.Child).Children.Add(new TextBlock
        {
            Text = "Enable diagnostics, use the app normally, and switch between menus several times. Use Write report now or close the app to save a timestamped report. The report lists call count, total time, average time, and maximum time for each measured operation.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        return SettingsScrollable(page);
    }

    private void UpdatePerformanceDiagnosticsUi()
    {
        if (_performanceDiagnosticsStatus is null || _performanceDiagnosticsToggleButton is null)
            return;

        _performanceDiagnosticsToggleButton.Content = PerformanceDiagnostics.Enabled
            ? "Disable diagnostics"
            : "Enable diagnostics";
        _performanceDiagnosticsStatus.Text = PerformanceDiagnostics.Enabled
            ? "Performance diagnostics are ON. Use the app normally, then write a report when you have reproduced the slowdown."
            : "Performance diagnostics are OFF.";
    }

    private static void OpenPerformanceDiagnosticsFolder()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactburstQuizManager",
            "Diagnostics");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true,
        });
    }
}
