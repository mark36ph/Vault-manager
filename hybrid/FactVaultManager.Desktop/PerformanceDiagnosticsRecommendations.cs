using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsRecommendationsAttached;
    private bool _performanceDiagnosticsUpdatingResults;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void InitializePerformanceRecommendations()
    {
        // The Performance Diagnostics page is created lazily. Attach when its
        // results TextBox is actually loaded instead of relying on main-window load.
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPerformanceDiagnosticsResultsLoaded),
            handledEventsToo: true);
    }

    private static void OnPerformanceDiagnosticsResultsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox &&
            textBox.Text.StartsWith("No performance profile has been run yet.", StringComparison.Ordinal) &&
            Window.GetWindow(textBox) is MainShellWindow window)
        {
            window.AttachPerformanceDiagnosticsRecommendations();
        }
    }

    private void AttachPerformanceDiagnosticsRecommendations()
    {
        if (_performanceDiagnosticsRecommendationsAttached || _performanceDiagnosticsResults is null)
            return;
        _performanceDiagnosticsRecommendationsAttached = true;
        _performanceDiagnosticsResults.TextChanged += (_, _) => AddPerformanceRecommendations();
        AddPerformanceRecommendations();
    }

    private void AddPerformanceRecommendations()
    {
        if (_performanceDiagnosticsUpdatingResults || _performanceDiagnosticsResults is null)
            return;
        var text = _performanceDiagnosticsResults.Text;
        if (!text.Contains("FACTBURST FULL PERFORMANCE PROFILE", StringComparison.Ordinal))
            return;
        var diagnosis = BuildPerformanceDiagnosis(text);
        if (string.IsNullOrWhiteSpace(diagnosis))
            return;
        var marker = "ACTION PLAN\r\n";
        var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex >= 0)
            text = text[..markerIndex];
        _performanceDiagnosticsUpdatingResults = true;
        try
        {
            _performanceDiagnosticsResults.Text = text.TrimEnd() + "\r\n\r\n" + diagnosis.TrimEnd() + "\r\n";
            _performanceDiagnosticsResults.CaretIndex = 0;
        }
        finally { _performanceDiagnosticsUpdatingResults = false; }
    }

    private static string BuildPerformanceDiagnosis(string report)
    {
        var maxNavigation = ReadMilliseconds(report, "Maximum");
        var p95 = ReadMilliseconds(report, "P95");
        var slow100 = ReadInt(report, "Samples >= 100 ms");
        var memoryDelta = ReadSignedMegabytes(report, "Working set delta");
        var managedDelta = ReadSignedMegabytes(report, "Managed heap delta");
        var gen2 = ReadInt(report, "Gen 2 collections");
        var quizAverage = ReadMilliseconds(report, "Average", "QUIZ DATABASE REFRESH");
        var findings = new List<string>();
        var fixes = new List<string>();

        if (maxNavigation >= 500)
        {
            findings.Add($"CRITICAL: navigation has a {maxNavigation:F0} ms spike — this is a real visible-freeze candidate.");
            fixes.Add("Fix navigation stalls first. The next code inspection should trace page construction, data binding, layout/rendering, and synchronous work triggered by the slow section.");
        }
        else if (maxNavigation >= 100 || slow100 > 0)
        {
            findings.Add($"WARNING: navigation has intermittent stalls (maximum {maxNavigation:F0} ms; {slow100} samples at 100 ms+).");
            fixes.Add("Trace the slow navigation sections and move safe I/O, API calls, large collection work, and page initialization off the UI thread.");
        }
        else if (p95 >= 50)
        {
            findings.Add($"WARNING: normal navigation is becoming sluggish (P95 {p95:F0} ms).");
            fixes.Add("Reduce work performed during navigation and stop rebuilding controls, collections, or view models unnecessarily.");
        }
        else findings.Add("PASS: navigation does not show a broad performance problem.");

        if (quizAverage >= 100)
        {
            findings.Add($"WARNING: quiz refresh is slow ({quizAverage:F0} ms average).");
            fixes.Add("Optimize RefreshQuizBank: reduce database work and avoid unnecessary grid rebinding.");
        }
        else findings.Add("PASS: quiz database refresh is unlikely to be the main slowdown.");

        if (memoryDelta >= 100)
        {
            findings.Add($"WARNING: working set grew by {memoryDelta:F0} MB during the stress test.");
            fixes.Add("Investigate objects retained by repeated navigation, especially UI controls, collections, images, and view models. Confirm retention after an idle/GC check before calling it a leak.");
        }
        else if (memoryDelta >= 50)
        {
            findings.Add($"WATCH: working set increased by {memoryDelta:F0} MB during the stress test.");
            fixes.Add("Check for retained UI/data objects created during repeated navigation.");
        }

        if (managedDelta >= 20 || gen2 >= 2)
        {
            findings.Add($"WATCH: managed memory/GC activity is elevated (+{managedDelta:F1} MB managed heap; {gen2} Gen 2 collections).");
            fixes.Add("Look for repeated allocations during navigation and reuse pages, collections, and view models where possible.");
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("ACTION PLAN");
        builder.AppendLine();
        builder.AppendLine("DIAGNOSIS");
        foreach (var finding in findings) builder.AppendLine("• " + finding);
        builder.AppendLine();
        builder.AppendLine("WHAT TO FIX FIRST");
        if (fixes.Count == 0) builder.AppendLine("• No defect crossed the current thresholds. Run Profile next startup to investigate launch-time work.");
        else for (var i = 0; i < fixes.Count; i++) builder.AppendLine($"{i + 1}. {fixes[i]}");
        builder.AppendLine();
        builder.AppendLine("NEXT TEST");
        builder.AppendLine(maxNavigation >= 100
            ? "Run the profile again after opening each navigation section once. If the same section spikes again, that section is the first code path to optimize."
            : "Use Profile next startup, restart the app, then run the profile again to separate launch-time work from normal navigation work.");
        return builder.ToString();
    }

    private static double ReadMilliseconds(string report, string label, string? section = null)
    {
        var source = section is null ? report : ExtractSection(report, section);
        var match = Regex.Match(source, $@"^{Regex.Escape(label)}\s*:\s*([0-9]+(?:\.[0-9]+)?)\s*ms", RegexOptions.Multiline);
        return match.Success && double.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static int ReadInt(string report, string label)
    {
        var match = Regex.Match(report, $@"^{Regex.Escape(label)}\s*:\s*([0-9]+)", RegexOptions.Multiline);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static double ReadSignedMegabytes(string report, string label)
    {
        var match = Regex.Match(report, $@"^{Regex.Escape(label)}\s*:\s*([+-]?[0-9]+(?:\.[0-9]+)?)\s*MB", RegexOptions.Multiline);
        return match.Success && double.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static string ExtractSection(string report, string section)
    {
        var start = report.IndexOf(section, StringComparison.Ordinal);
        if (start < 0) return report;
        var next = report.IndexOf("\r\n\r\n", start + section.Length, StringComparison.Ordinal);
        return next < 0 ? report[start..] : report[start..next];
    }
}
