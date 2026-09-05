using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsUiInitialized;
    internal TextBlock? _performanceDiagnosticsStatus;
    private Button? _performanceDiagnosticsToggleButton;
    internal TextBox? _performanceDiagnosticsResults;

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
            "Profile startup, activation, navigation, quiz database refreshes, memory, and garbage collection to find the real cause of slow app behavior.");

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
            MinWidth = 190,
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
            MinWidth = 190,
        };
        scanButton.Click += (_, _) => ScanPerformanceDiagnostics();
        stack.Children.Add(scanButton);

        var benchmarkButton = new Button
        {
            Content = "Benchmark navigation",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 190,
        };
        benchmarkButton.Click += async (_, _) => await BenchmarkNavigationAsync();
        stack.Children.Add(benchmarkButton);

        var fullProfileButton = new Button
        {
            Content = "Run full performance profile",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 230,
            FontWeight = FontWeights.SemiBold,
        };
        fullProfileButton.Click += async (_, _) => await RunFullPerformanceProfileAsync();
        stack.Children.Add(fullProfileButton);

        var startupProfileButton = new Button
        {
            Content = "Profile next startup",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 230,
        };
        startupProfileButton.Click += (_, _) =>
        {
            PerformanceDiagnostics.RequestStartupProfile();
            _performanceDiagnosticsStatus!.Text = "Next-startup profiling is armed. Close and reopen Factburst Quiz Manager, then return here to review the startup timings.";
        };
        stack.Children.Add(startupProfileButton);

        var reportButton = new Button
        {
            Content = "Write report now",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 190,
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
            MinWidth = 190,
        };
        folderButton.Click += (_, _) => OpenPerformanceDiagnosticsFolder();
        stack.Children.Add(folderButton);

        var resultsSection = SettingsSection("Performance Results");
        page.Children.Add(resultsSection);
        var resultsStack = (StackPanel)resultsSection.Child;
        _performanceDiagnosticsResults = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 420,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Text = "No performance profile has been run yet. Enable diagnostics and run the full performance profile.",
        };
        resultsStack.Children.Add(_performanceDiagnosticsResults);

        var info = SettingsSection("How to use it");
        page.Children.Add(info);
        ((StackPanel)info.Child).Children.Add(new TextBlock
        {
            Text = "Full profile runs 10 navigation cycles, measures repeated quiz database refreshes, records memory and GC changes, and lists operations whose maximum time reaches 50 ms. Use Profile next startup before restarting if the app feels slow immediately after launch; startup timings are captured from the moment the process starts.",
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

    private async Task BenchmarkNavigationAsync()
    {
        if (!PerformanceDiagnostics.Enabled)
        {
            _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then run the navigation benchmark.";
            return;
        }

        var buttons = GetCurrentNavigationButtons();
        if (buttons.Count == 0)
        {
            _performanceDiagnosticsStatus!.Text = "No navigation buttons were available for benchmarking. The navigation panel has not produced any tagged buttons yet.";
            return;
        }

        var originalIndex = MainTabs.SelectedIndex;
        var rows = new List<(int Index, string Name, double Ms)>();
        _performanceDiagnosticsStatus!.Text = $"Benchmarking {buttons.Count} navigation sections...";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            foreach (var button in buttons)
                await NavigateAndWaitAsync(button);

            foreach (var button in buttons)
            {
                if (!int.TryParse(button.Tag?.ToString(), out var index))
                    continue;

                var name = GetNavigationBenchmarkName(button, index);
                var stopwatch = Stopwatch.StartNew();
                await NavigateAndWaitAsync(button);
                stopwatch.Stop();
                rows.Add((index, name, stopwatch.Elapsed.TotalMilliseconds));
            }
        }
        finally
        {
            if (originalIndex >= 0)
            {
                MainTabs.SelectedIndex = originalIndex;
                ApplyNavigationSelection(originalIndex);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
        }

        var ordered = rows.OrderByDescending(row => row.Ms).ToList();
        var slowCount = ordered.Count(row => row.Ms >= 100);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("FACTBURST NAVIGATION BENCHMARK");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Sections tested : {ordered.Count}");
        builder.AppendLine("Warm-up passes   : 1");
        builder.AppendLine("Measured passes  : 1");
        builder.AppendLine();
        builder.AppendLine("NAVIGATION RESULTS (slowest first)");
        builder.AppendLine("Section | Index | Time ms | Status");
        builder.AppendLine("--- | ---: | ---: | ---");

        foreach (var row in ordered)
            builder.AppendLine($"{row.Name} | {row.Index} | {row.Ms:F1} | {(row.Ms >= 100 ? "SLOW" : "OK")}");

        if (ordered.Count > 0)
        {
            var slowest = ordered[0];
            builder.AppendLine();
            builder.AppendLine($"SLOWEST: {slowest.Name} — {slowest.Ms:F1} ms");
            builder.AppendLine($"Sections >= 100 ms: {slowCount}");
        }

        builder.AppendLine();
        builder.AppendLine("MEASURED OPERATIONS");
        builder.Append(PerformanceDiagnostics.GetReport());
        _performanceDiagnosticsResults!.Text = builder.ToString();
        _performanceDiagnosticsStatus.Text = ordered.Count == 0
            ? "Navigation benchmark completed without measurable sections."
            : $"Navigation benchmark complete. Slowest section: {ordered[0].Name} at {ordered[0].Ms:F1} ms.";
    }

    private async Task RunFullPerformanceProfileAsync()
    {
        if (!PerformanceDiagnostics.Enabled)
        {
            _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then run the full performance profile.";
            return;
        }

        var buttons = GetCurrentNavigationButtons();
        if (buttons.Count == 0)
        {
            _performanceDiagnosticsStatus!.Text = "The full profile could not run because the navigation panel has not produced any tagged buttons yet.";
            return;
        }

        const int navigationCycles = 10;
        const int quizRefreshPasses = 3;
        var originalIndex = MainTabs.SelectedIndex;
        var navigationSamples = new List<double>();
        var quizRefreshSamples = new List<double>();
        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        _performanceDiagnosticsStatus!.Text = $"Running full profile: {navigationCycles} navigation cycles + {quizRefreshPasses} quiz refresh passes...";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            for (var cycle = 0; cycle < navigationCycles; cycle++)
            {
                foreach (var button in buttons)
                {
                    var stopwatch = Stopwatch.StartNew();
                    await NavigateAndWaitAsync(button);
                    stopwatch.Stop();
                    navigationSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            if (_quizWorkflowInitialized && _quizBankGrid is not null)
            {
                for (var pass = 0; pass < quizRefreshPasses; pass++)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    var stopwatch = Stopwatch.StartNew();
                    RefreshQuizBank();
                    stopwatch.Stop();
                    quizRefreshSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                }
            }
        }
        finally
        {
            if (originalIndex >= 0)
            {
                MainTabs.SelectedIndex = originalIndex;
                ApplyNavigationSelection(originalIndex);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
        }

        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        var managedAfter = GC.GetTotalMemory(forceFullCollection: false);
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: false);
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        var sortedNavigation = navigationSamples.OrderBy(value => value).ToList();
        var navigationAverage = navigationSamples.Count == 0 ? 0 : navigationSamples.Average();
        var navigationP95 = Percentile(sortedNavigation, 0.95);
        var navigationMax = navigationSamples.Count == 0 ? 0 : navigationSamples.Max();
        var quizAverage = quizRefreshSamples.Count == 0 ? 0 : quizRefreshSamples.Average();
        var quizMax = quizRefreshSamples.Count == 0 ? 0 : quizRefreshSamples.Max();

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("FACTBURST FULL PERFORMANCE PROFILE");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("NAVIGATION STRESS TEST");
        builder.AppendLine($"Sections           : {buttons.Count}");
        builder.AppendLine($"Cycles             : {navigationCycles}");
        builder.AppendLine($"Samples            : {navigationSamples.Count}");
        builder.AppendLine($"Average            : {navigationAverage:F1} ms");
        builder.AppendLine($"P95                : {navigationP95:F1} ms");
        builder.AppendLine($"Maximum            : {navigationMax:F1} ms");
        builder.AppendLine($"Samples >= 50 ms   : {navigationSamples.Count(value => value >= 50)}");
        builder.AppendLine($"Samples >= 100 ms  : {navigationSamples.Count(value => value >= 100)}");
        builder.AppendLine();
        builder.AppendLine("QUIZ DATABASE REFRESH");
        builder.AppendLine($"Passes             : {quizRefreshSamples.Count}");
        builder.AppendLine($"Average            : {(quizRefreshSamples.Count == 0 ? "SKIPPED" : $"{quizAverage:F1} ms")}");
        builder.AppendLine($"Maximum            : {(quizRefreshSamples.Count == 0 ? "SKIPPED" : $"{quizMax:F1} ms")}");
        builder.AppendLine($"Current rows       : {_quizBankGrid?.Items.Count.ToString("N0") ?? "0"}");
        builder.AppendLine();
        builder.AppendLine("MEMORY / GARBAGE COLLECTION");
        builder.AppendLine($"Working set before : {workingSetBefore / 1024d / 1024d:F1} MB");
        builder.AppendLine($"Working set after  : {workingSetAfter / 1024d / 1024d:F1} MB");
        builder.AppendLine($"Working set delta  : {(workingSetAfter - workingSetBefore) / 1024d / 1024d:+0.0;-0.0;0.0} MB");
        builder.AppendLine($"Managed heap before: {managedBefore / 1024d / 1024d:F1} MB");
        builder.AppendLine($"Managed heap after : {managedAfter / 1024d / 1024d:F1} MB");
        builder.AppendLine($"Managed heap delta : {(managedAfter - managedBefore) / 1024d / 1024d:+0.0;-0.0;0.0} MB");
        builder.AppendLine($"Allocated delta    : {(allocatedAfter - allocatedBefore) / 1024d / 1024d:F1} MB");
        builder.AppendLine($"Gen 0 collections  : {gen0After - gen0Before}");
        builder.AppendLine($"Gen 1 collections  : {gen1After - gen1Before}");
        builder.AppendLine($"Gen 2 collections  : {gen2After - gen2Before}");
        builder.AppendLine();
        builder.AppendLine("UI OPERATIONS REACHING 50 MS+");
        builder.Append(PerformanceDiagnostics.GetSlowOperationReport(50));
        builder.AppendLine();
        builder.AppendLine("ALL MEASURED OPERATIONS");
        builder.Append(PerformanceDiagnostics.GetReport());
        builder.AppendLine();
        builder.AppendLine("STARTUP NOTE");
        builder.AppendLine("Startup timings are only present if diagnostics were enabled when this process started. Use Profile next startup for a clean launch profile.");

        _performanceDiagnosticsResults!.Text = builder.ToString();
        _performanceDiagnosticsStatus.Text =
            $"Full profile complete. Navigation average {navigationAverage:F1} ms, P95 {navigationP95:F1} ms, max {navigationMax:F1} ms.";
    }

    private List<Button> GetCurrentNavigationButtons()
    {
        // The real navigation buttons are owned by the named navigation panel. The
        // sidebar can be hidden while Settings is open, so IsVisible must not be used
        // as a discovery filter. Hidden controls can still raise their Click events.
        ApplyNavigationSections();

        var panelButtons = PrimaryNavigationPanel.Children
            .OfType<Button>()
            .Where(button => int.TryParse(button.Tag?.ToString(), out _))
            .OrderBy(button => int.Parse(button.Tag!.ToString()!))
            .ToList();

        if (panelButtons.Count > 0)
        {
            _indexedNavigationButtons = panelButtons;
            return panelButtons;
        }

        if (Content is not DependencyObject root)
            return new List<Button>();

        var buttons = FindVisualChildren<Button>(root)
            .Where(button => int.TryParse(button.Tag?.ToString(), out _))
            .OrderBy(button => int.Parse(button.Tag!.ToString()!))
            .ToList();

        _indexedNavigationButtons = buttons;
        return buttons;
    }

    private async Task NavigateAndWaitAsync(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static string GetNavigationBenchmarkName(Button button, int index)
    {
        var text = button.Content?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? $"Section {index}" : text.Replace("\r", " ").Replace("\n", " ");
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;

        var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
        index = Math.Clamp(index, 0, sortedValues.Count - 1);
        return sortedValues[index];
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
            ? "Performance diagnostics are ON. Run Scan app, Benchmark navigation, or the Full performance profile."
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