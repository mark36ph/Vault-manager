using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace FactVaultManager.Desktop;

internal static class PerformanceDiagnostics
{
    private const int MaxSamplesPerOperation = 200;
    private static readonly ConcurrentDictionary<string, OperationStats> Stats = new(StringComparer.Ordinal);
    private static readonly object FileLock = new();
    private static readonly string DiagnosticsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FactburstQuizManager",
        "Diagnostics");
    private static readonly string StartupProfileRequestPath = Path.Combine(
        DiagnosticsDirectory,
        "profile-next-startup.request");

    public static bool Enabled { get; private set; } = string.Equals(
        Environment.GetEnvironmentVariable("FACTBURST_PERF_DIAGNOSTICS"),
        "1",
        StringComparison.Ordinal) || StartupProfileRequested();

    public static void SetEnabled(bool enabled) => Enabled = enabled;

    public static void RequestStartupProfile()
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            File.WriteAllText(StartupProfileRequestPath, DateTime.Now.ToString("O"));
        }
        catch
        {
            // A diagnostics request must never interfere with the application.
        }
    }

    public static bool StartupProfileRequested()
    {
        try
        {
            return File.Exists(StartupProfileRequestPath);
        }
        catch
        {
            return false;
        }
    }

    public static void ClearStartupProfileRequest()
    {
        try
        {
            if (File.Exists(StartupProfileRequestPath))
                File.Delete(StartupProfileRequestPath);
        }
        catch
        {
            // Diagnostics must never interfere with application startup.
        }
    }

    public static void Reset()
    {
        Stats.Clear();
    }

    public static IDisposable Measure(string operation)
    {
        if (!Enabled)
            return NoopScope.Instance;

        return new Scope(operation);
    }

    public static void Record(string operation, TimeSpan elapsed)
    {
        if (!Enabled)
            return;

        var stats = Stats.GetOrAdd(operation, _ => new OperationStats());
        stats.Record(elapsed.TotalMilliseconds);
    }

    public static string GetReport()
    {
        var rows = Stats
            .Select(pair => new
            {
                Operation = pair.Key,
                pair.Value.Count,
                TotalMs = pair.Value.TotalMs,
                MaxMs = pair.Value.MaxMs,
                AverageMs = pair.Value.AverageMs
            })
            .OrderByDescending(row => row.TotalMs)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Factburst UI performance diagnostics");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("Operation | Calls | Total ms | Avg ms | Max ms");
        builder.AppendLine("--- | ---: | ---: | ---: | ---:");

        foreach (var row in rows)
            builder.AppendLine($"{row.Operation} | {row.Count} | {row.TotalMs:F1} | {row.AverageMs:F1} | {row.MaxMs:F1}");

        return builder.ToString();
    }

    public static string GetSlowOperationReport(double minimumMaxMilliseconds)
    {
        var rows = Stats
            .Select(pair => new
            {
                Operation = pair.Key,
                pair.Value.Count,
                TotalMs = pair.Value.TotalMs,
                MaxMs = pair.Value.MaxMs,
                AverageMs = pair.Value.AverageMs
            })
            .Where(row => row.MaxMs >= minimumMaxMilliseconds)
            .OrderByDescending(row => row.MaxMs)
            .ToList();

        if (rows.Count == 0)
            return "No measured operation reached the threshold.\r\n";

        var builder = new StringBuilder();
        builder.AppendLine("Operation | Calls | Avg ms | Max ms");
        builder.AppendLine("--- | ---: | ---: | ---:");
        foreach (var row in rows)
            builder.AppendLine($"{row.Operation} | {row.Count} | {row.AverageMs:F1} | {row.MaxMs:F1}");
        return builder.ToString();
    }

    public static string? WriteReport()
    {
        if (!Enabled)
            return null;

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            var path = Path.Combine(DiagnosticsDirectory, $"ui-performance-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            lock (FileLock)
                File.WriteAllText(path, GetReport());
            return path;
        }
        catch
        {
            // Diagnostics must never interfere with application shutdown.
            return null;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _operation;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _disposed;

        public Scope(string operation) => _operation = operation;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _stopwatch.Stop();
            Record(_operation, _stopwatch.Elapsed);
        }
    }

    private sealed class OperationStats
    {
        private int _count;
        private double _totalMs;
        private double _maxMs;

        public int Count => Math.Min(Volatile.Read(ref _count), MaxSamplesPerOperation);
        public double TotalMs => Volatile.Read(ref _totalMs);
        public double MaxMs => Volatile.Read(ref _maxMs);
        public double AverageMs => Count == 0 ? 0 : TotalMs / Count;

        public void Record(double milliseconds)
        {
            var count = Interlocked.Increment(ref _count);
            if (count > MaxSamplesPerOperation)
                return;

            lock (this)
            {
                _totalMs += milliseconds;
                if (milliseconds > _maxMs)
                    _maxMs = milliseconds;
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}