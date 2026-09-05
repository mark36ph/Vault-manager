using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _performanceDiagnosticsRecommendationsAttached;
    private bool _performanceDiagnosticsUpdatingResults;

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
        finally
        {
            _performanceDiagnosticsUpdatingResults = false;
        }
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
            findings.Add($"CRITICAL: navigation has a {maxNavigation:F0} ms spike. This is the clearest cause of visible freezing.");
            fixes.Add("Instrument and optimize the slow navigation path first: page construction, data binding, layout, and synchronous work triggered during navigation.");
        }
        else if (maxNavigation >= 100 || slow100 > 0)
        {
            findings.Add($"WARNING: navigation has intermittent stalls (maximum {maxNavigation:F0} ms; {slow100} samples at 100 ms+).");
            fixes.Add("Profile each navigation section and move any synchronous I/O, API calls, large collection work, or page initialization off the UI thread where safe.");
        }
        else if (p95 >= 50)
        {
            findings.Add($"WARNING: normal navigation is becoming sluggish (P95 {p95:F0} ms).");
            fixes.Add("Reduce work performed during navigation and avoid rebuilding controls or collections when switching sections.");
        }
        else
        {
            findings.Add("PASS: navigation does not currently show a broad performance problem.");
        }

        if (quizAverage >= 100)
        {
            findings.Add($"WARNING: quiz refresh is slow ({quizAverage:F0} ms average).");
            fixes.Add("Optimize RefreshQuizBank: reduce database work, avoid unnecessary rebinding, and update the grid only when the data actually changed.");
        }
        else
        {
            findings.Add("PASS: quiz database refresh is unlikely to be the main source of the slowdown.");
        }

        if (memoryDelta >= 100)
        {
            findings.Add($"WARNING: working set grew by {memoryDelta:F0} MB during the profile.");
            fixes.Add("Investigate navigation-created objects and retained UI/data references. Repeat the profile after forcing a quiet period; a persistent increase after GC is more significant than transient allocation during the stress test.");
        }
        else if (memoryDelta >= 50)
        {
            findings.Add($"WATCH: working set increased by {memoryDelta:F0} MB during the stress test.");
            fixes.Add("Keep an eye on retained UI/data objects, especially anything created on repeated navigation.");
        }

        if (managedDelta >= 20 || gen2 >= 2)
        {
            findings.Add($"WATCH: managed memory/GC activity is elevated (+{managedDelta:F1} MB managed heap; {gen2} Gen 2 collections).");
            fixes.Add("Look for repeated allocations and retained objects during navigation. Prefer reusing pages, collections, and view models instead of recreating them on every switch.");
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("ACTION PLAN");
        builder.AppendLine();
        builder.AppendLine("WHAT THE TEST SAYS");
        foreach (var finding in findings)
            builder.AppendLine("• " + finding);

        builder.AppendLine();
        builder.AppendLine("WHAT TO FIX FIRST");
        if (fixes.Count == 0)
            builder.AppendLine("• No clear performance defect crossed the diagnostic thresholds. Run Profile next startup to check launch-time stalls.");
        else
        {
            for (var index = 0; index < fixes.Count; index++)
                builder.AppendLine($"{index + 1}. {fixes[index]}");
        }

        builder.AppendLine();
        builder.AppendLine("NEXT TEST");
        if (maxNavigation >= 100)
            builder.AppendLine("Run the profile again after opening each navigation section once. The goal is to identify whether the same section repeatedly produces the long spike.");
        else
            builder.AppendLine("Use Profile next startup, restart the app, then run the profile again. This separates launch-time work from normal navigation work.");

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
        if (start < 0)
            return report;
        var next = report.IndexOf("\r\n\r\n", start + section.Length, StringComparison.Ordinal);
        return next < 0 ? report[start..] : report[start..next];
    }
}
