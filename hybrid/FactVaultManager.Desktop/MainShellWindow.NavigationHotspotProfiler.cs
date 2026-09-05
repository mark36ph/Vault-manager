using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    internal void EnsureNavigationHotspotProfilerButton()
    {
        TryAddNavigationHotspotProfilerButton();
    }

    private bool TryAddNavigationHotspotProfilerButton()
    {
        if (Content is not DependencyObject root)
            return false;

        var fullProfileButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Run full performance profile", StringComparison.Ordinal));
        if (fullProfileButton is null || fullProfileButton.Parent is not Panel parent)
            return false;

        if (parent.Children.OfType<Button>().Any(button => string.Equals(button.Content?.ToString(), "Profile navigation by section", StringComparison.Ordinal)))
            return true;

        var button = new Button
        {
            Content = "Profile navigation by section",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 230,
        };
        button.Click += async (_, _) => await RunNavigationHotspotProfileAsync();
        parent.Children.Insert(parent.Children.IndexOf(fullProfileButton) + 1, button);
        return true;
    }

    private async Task RunNavigationHotspotProfileAsync()
    {
        if (!PerformanceDiagnostics.Enabled)
        {
            _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then run the navigation hotspot profile.";
            return;
        }

        var buttons = GetCurrentNavigationButtons();
        if (buttons.Count == 0)
        {
            _performanceDiagnosticsStatus!.Text = "The navigation hotspot profile could not run because no tagged navigation buttons are available.";
            return;
        }

        const int cycles = 10;
        var originalIndex = MainTabs.SelectedIndex;
        var samples = buttons.ToDictionary(button => button, _ => new List<double>());
        _performanceDiagnosticsStatus!.Text = $"Profiling {buttons.Count} navigation sections for {cycles} cycles...";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            foreach (var button in buttons)
                await NavigateAndWaitAsync(button);

            for (var cycle = 0; cycle < cycles; cycle++)
            {
                foreach (var button in buttons)
                {
                    var stopwatch = Stopwatch.StartNew();
                    await NavigateAndWaitAsync(button);
                    stopwatch.Stop();
                    samples[button].Add(stopwatch.Elapsed.TotalMilliseconds);
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

        var rows = buttons.Select(button =>
        {
            var index = int.TryParse(button.Tag?.ToString(), out var parsed) ? parsed : 0;
            var values = samples[button];
            var sorted = values.OrderBy(value => value).ToList();
            return new NavigationHotspotRow(
                GetNavigationBenchmarkName(button, index), index, values.Average(),
                Percentile(sorted, 0.95), values.Max(),
                values.Count(value => value >= 50), values.Count(value => value >= 100));
        }).OrderByDescending(row => row.MaxMs).ToList();

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("FACTBURST NAVIGATION HOTSPOT PROFILE");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Sections           : {buttons.Count}");
        builder.AppendLine("Warm-up passes     : 1 per section");
        builder.AppendLine($"Measured cycles    : {cycles}");
        builder.AppendLine($"Measured samples   : {buttons.Count * cycles}");
        builder.AppendLine();
        builder.AppendLine("NAVIGATION HOTSPOTS (slowest maximum first)");
        builder.AppendLine("Section | Index | Avg ms | P95 ms | Max ms | >=50 | >=100 | Status");
        builder.AppendLine("--- | ---: | ---: | ---: | ---: | ---: | ---: | ---");
        foreach (var row in rows)
        {
            var status = row.MaxMs >= 500 ? "CRITICAL" : row.MaxMs >= 100 ? "SLOW" : "OK";
            builder.AppendLine($"{row.Name} | {row.Index} | {row.AverageMs:F1} | {row.P95Ms:F1} | {row.MaxMs:F1} | {row.Samples50} | {row.Samples100} | {status}");
        }

        if (rows.Count > 0)
        {
            var hotspot = rows[0];
            builder.AppendLine();
            builder.AppendLine($"HOTSPOT: {hotspot.Name} — maximum {hotspot.MaxMs:F1} ms");
            builder.AppendLine($"HOTSPOT P95: {hotspot.P95Ms:F1} ms; average: {hotspot.AverageMs:F1} ms");
            builder.AppendLine();
            builder.AppendLine("INTERPRETATION");
            builder.AppendLine(hotspot.MaxMs >= 500
                ? "This section contains a visible-freeze candidate. Inspect its page construction, data binding, synchronous work, and layout/rendering path first."
                : hotspot.MaxMs >= 100
                    ? "This section is the main navigation hotspot. Inspect its page construction, data binding, synchronous work, and layout/rendering path first."
                    : "No section exceeded the 100 ms hotspot threshold in this run.");
        }

        builder.AppendLine();
        builder.AppendLine("MEASURED OPERATIONS");
        builder.Append(PerformanceDiagnostics.GetReport());
        _performanceDiagnosticsResults!.Text = builder.ToString();
        _performanceDiagnosticsStatus.Text = rows.Count == 0
            ? "Navigation hotspot profile completed without measurable sections."
            : $"Navigation hotspot profile complete. Hotspot: {rows[0].Name} at {rows[0].MaxMs:F1} ms maximum.";
    }

    private sealed record NavigationHotspotRow(string Name, int Index, double AverageMs, double P95Ms, double MaxMs, int Samples50, int Samples100);
}
