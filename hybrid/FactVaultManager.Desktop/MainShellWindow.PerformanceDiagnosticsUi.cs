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
    private TextBox? _performanceDiagnosticsResults;

    private void InitializePerformanceDiagnosticsUi()
    {
        if (_performanceDiagnosticsUiInitialized)
            return;

        if (!_settingsWorkflowInitialized || !_settingsNavButtons.TryGetValue("about", out var aboutButton))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(InitializePerformanceDiagnosticsUi));
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
            "Scan the current app UI and show performance measurements directly in the app. Diagnostics stay off until you enable them.");

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

        var scanButton = new Button
        {
            Content = "Scan app now",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 170,
        };
        scanButton.Click += (_, _) => ScanPerformanceDiagnostics();
        stack.Children.Add(scanButton);

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

        var resultsSection = SettingsSection("Scan Results");
        page.Children.Add(resultsSection);
        var resultsStack = (StackPanel)resultsSection.Child;
        _performanceDiagnosticsResults = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 260,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Text = "No scan has been run yet. Enable diagnostics and click Scan app now.",
        };
        resultsStack.Children.Add(_performanceDiagnosticsResults);

        var info = SettingsSection("How to use it");
        page.Children.Add(info);
        ((StackPanel)info.Child).Children.Add(new TextBlock
        {
            Text = "Enable diagnostics, use the app normally, and switch between menus several times. Scan app now shows the current visual-tree size plus the measured startup/navigation timings. The results are shown here without leaving the app.",
            Foreground = SettingsMutedBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        return SettingsScrollable(page);
    }

    private void ScanPerformanceDiagnostics()
    {
        if (!PerformanceDiagnostics.Enabled)
        {
            _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then run the app scan.";
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var visualElements = 0;
        var controls = 0;
        var buttons = 0;
        var textBlocks = 0;
        var textBoxes = 0;
        var navigationButtons = 0;

        ScanVisualTree(this, ref visualElements, ref controls, ref buttons, ref textBlocks, ref textBoxes, ref navigationButtons);
        stopwatch.Stop();

        var report = PerformanceDiagnostics.GetReport();
        _performanceDiagnosticsResults!.Text =
            "FACTBURST APP PERFORMANCE SCAN\r\n" +
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n" +
            "CURRENT UI\r\n" +
            $"Visual elements : {visualElements:N0}\r\n" +
            $"Controls        : {controls:N0}\r\n" +
            $"Buttons         : {buttons:N0}\r\n" +
            $"TextBlocks      : {textBlocks:N0}\r\n" +
            $"TextBoxes       : {textBoxes:N0}\r\n" +
            $"Navigation btns : {navigationButtons:N0}\r\n" +
            $"Scan time       : {stopwatch.Elapsed.TotalMilliseconds:F1} ms\r\n\r\n" +
            "MEASURED OPERATIONS\r\n" +
            report;

        _performanceDiagnosticsStatus!.Text = $"Scan complete in {stopwatch.Elapsed.TotalMilliseconds:F1} ms. Results are shown below.";
    }

    private static void ScanVisualTree(
        DependencyObject node,
        ref int visualElements,
        ref int controls,
        ref int buttons,
        ref int textBlocks,
        ref int textBoxes,
        ref int navigationButtons)
    {
        visualElements++;
        if (node is Control) controls++;
        if (node is Button button)
        {
            buttons++;
            if (button.Tag is not null && int.TryParse(button.Tag.ToString(), out _))
                navigationButtons++;
        }
        if (node is TextBlock) textBlocks++;
        if (node is TextBox) textBoxes++;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            ScanVisualTree(VisualTreeHelper.GetChild(node, index), ref visualElements, ref controls, ref buttons, ref textBlocks, ref textBoxes, ref navigationButtons);
    }

    private void UpdatePerformanceDiagnosticsUi()
    {
        if (_performanceDiagnosticsStatus is null || _performanceDiagnosticsToggleButton is null)
            return;

        _performanceDiagnosticsToggleButton.Content = PerformanceDiagnostics.Enabled
            ? "Disable diagnostics"
            : "Enable diagnostics";
        _performanceDiagnosticsStatus.Text = PerformanceDiagnostics.Enabled
            ? "Performance diagnostics are ON. Run Scan app now after reproducing the slowdown."
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
