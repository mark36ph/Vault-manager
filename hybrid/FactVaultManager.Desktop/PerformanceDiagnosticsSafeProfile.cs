using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private async Task RunSafeNavigationHotspotProfileAsync()
    {
        if (!PerformanceDiagnostics.Enabled)
        {
            _performanceDiagnosticsStatus!.Text = "Enable diagnostics first, then run the safe navigation profile.";
            return;
        }

        var allButtons = GetCurrentFactburstNavigationButtons();
        if (allButtons.Count == 0)
        {
            _performanceDiagnosticsStatus!.Text = "The current Factburst sidebar is not ready yet. Wait for startup to finish and try again.";
            return;
        }

        var readyButtons = new List<Button>();
        var skipped = new List<string>();
        foreach (var button in allButtons)
        {
            var key = GetFactburstNavigationKey(button);
            if (IsNavigationReadyForDiagnostics(key))
                readyButtons.Add(button);
            else
                skipped.Add(key);
        }

        if (readyButtons.Count == 0)
        {
            _performanceDiagnosticsStatus!.Text = $"No sidebar sections are ready to profile yet. Waiting pages: {string.Join(", ", skipped)}.";
            return;
        }

        const int cycles = 5;
        var originalIndex = MainTabs.SelectedIndex;
        var samples = readyButtons.ToDictionary(button => button, _ => new List<double>());
        var names = readyButtons.ToDictionary(button => button, GetFactburstNavigationKey);

        _performanceDiagnosticsStatus!.Text =
            $"Safe navigation profile: {readyButtons.Count} ready sections, {cycles} cycles. Unready sections will be skipped instead of showing pop-ups.";
        await WaitForStableLayoutAsync();

        try
        {
            foreach (var button in readyButtons)
                await NavigateAndWaitForStableLayoutAsync(button);

            for (var cycle = 0; cycle < cycles; cycle++)
            {
                foreach (var button in readyButtons)
                {
                    var name = names[button];
                    var stopwatch = Stopwatch.StartNew();
                    using (PerformanceDiagnostics.Measure($"Diagnostics.Navigation.{name}"))
                        await NavigateAndWaitForStableLayoutAsync(button);
                    stopwatch.Stop();
                    samples[button].Add(stopwatch.Elapsed.TotalMilliseconds);
                }

                _performanceDiagnosticsStatus!.Text =
                    $"Safe navigation profile: completed cycle {cycle + 1} of {cycles}...";
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
        }
        finally
        {
            if (originalIndex >= 0)
            {
                MainTabs.SelectedIndex = originalIndex;
                ApplyNavigationSelection(originalIndex);
                await WaitForStableLayoutAsync();
            }
        }

        var rows = readyButtons.Select((button, position) =>
        {
            var values = samples[button];
            var sorted = values.OrderBy(value => value).ToList();
            return new NavigationHotspotRow(
                names[button], position, values.Count == 0 ? 0 : values.Average(),
                Percentile(sorted, 0.95), values.Count == 0 ? 0 : values.Max(),
                values.Count(value => value >= 50), values.Count(value => value >= 100));
        }).OrderByDescending(row => row.MaxMs).ToList();

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("FACTBURST SAFE NAVIGATION DIAGNOSTIC");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine("Safety mode       : enabled");
        builder.AppendLine("Rule              : never click a legacy route until its target page is ready");
        builder.AppendLine($"Ready sections    : {readyButtons.Count}");
        builder.AppendLine($"Skipped sections  : {skipped.Count}");
        builder.AppendLine($"Measured cycles   : {cycles}");
        builder.AppendLine();
        builder.AppendLine("READY SECTIONS");
        builder.AppendLine("Section | Position | Avg ms | P95 ms | Max ms | >=50 | >=100 | Status");
        builder.AppendLine("--- | ---: | ---: | ---: | ---: | ---: | ---: | ---");
        foreach (var row in rows)
        {
            var status = row.MaxMs >= 500 ? "CRITICAL" : row.MaxMs >= 100 ? "SLOW" : "OK";
            builder.AppendLine($"{row.Name} | {row.Index} | {row.AverageMs:F1} | {row.P95Ms:F1} | {row.MaxMs:F1} | {row.Samples50} | {row.Samples100} | {status}");
        }

        if (skipped.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("SKIPPED — PAGE NOT READY");
            foreach (var name in skipped.Distinct(StringComparer.OrdinalIgnoreCase))
                builder.AppendLine($"{name} | not ready | no modal was shown");
        }

        if (rows.Count > 0)
        {
            var hotspot = rows[0];
            builder.AppendLine();
            builder.AppendLine($"HOTSPOT: {hotspot.Name} — maximum {hotspot.MaxMs:F1} ms");
            builder.AppendLine(hotspot.MaxMs >= 500
                ? "Critical: this section is a strong visible-freeze candidate."
                : hotspot.MaxMs >= 100
                    ? "Slow: inspect this section's page construction, data binding, and synchronous work."
                    : "Healthy: no measured section exceeded 100 ms in this run.");
        }

        builder.AppendLine();
        builder.AppendLine("MEASURED OPERATIONS");
        builder.Append(PerformanceDiagnostics.GetReport());
        _performanceDiagnosticsResults!.Text = builder.ToString();
        _performanceDiagnosticsStatus.Text = rows.Count == 0
            ? "Safe navigation diagnostic completed, but no measurable sections were available."
            : $"Safe navigation diagnostic complete. Slowest ready section: {rows[0].Name} at {rows[0].MaxMs:F1} ms.";
    }

    private string GetFactburstNavigationKey(Button button) =>
        _autopilotNavButtons.FirstOrDefault(pair => ReferenceEquals(pair.Value, button)).Key ??
        Convert.ToString(button.Content)?.Trim() ?? "Unknown";

    private bool IsNavigationReadyForDiagnostics(string key)
    {
        return key switch
        {
            "Autopilot" => _autopilotHomeTabIndex >= 0,
            "Create" => LegacyNavigationRouteReady("Quizzes"),
            "Performance" => LegacyNavigationRouteReady("YouTube Manager"),
            "Library" => LegacyNavigationRouteReady("Quiz History"),
            "Settings" => LegacyNavigationRouteReady("Settings"),
            _ => true,
        };
    }

    private bool LegacyNavigationRouteReady(string fragment) =>
        FindLegacyNavigationButton(fragment) is { Tag: not null } route &&
        int.TryParse(route.Tag?.ToString(), out _);

    private bool AreAllCurrentNavigationRoutesReadyForDiagnostics() =>
        GetCurrentFactburstNavigationButtons()
            .Select(GetFactburstNavigationKey)
            .All(IsNavigationReadyForDiagnostics);

    private async Task RunSafeFullPerformanceProfileAsync()
    {
        await RunSafeNavigationHotspotProfileAsync();

        if (!_quizWorkflowInitialized || _quizBankGrid is null)
            return;

        const int refreshPasses = 3;
        var samples = new List<double>();
        _performanceDiagnosticsStatus!.Text = "Safe full profile: measuring quiz database refreshes...";
        for (var pass = 0; pass < refreshPasses; pass++)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var stopwatch = Stopwatch.StartNew();
            using (PerformanceDiagnostics.Measure("Diagnostics.QuizDatabaseRefresh"))
                RefreshQuizBank();
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }

        var existing = _performanceDiagnosticsResults?.Text ?? string.Empty;
        var average = samples.Average();
        var maximum = samples.Max();
        _performanceDiagnosticsResults!.Text = existing +
            "\r\n\r\nQUIZ DATABASE REFRESH (SAFE PROFILE)\r\n" +
            $"Passes             : {samples.Count}\r\n" +
            $"Average            : {average:F1} ms\r\n" +
            $"Maximum            : {maximum:F1} ms\r\n" +
            $"Current rows       : {_quizBankGrid.Items.Count:N0}\r\n";
    }
}
