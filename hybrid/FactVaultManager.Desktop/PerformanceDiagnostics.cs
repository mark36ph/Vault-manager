using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace FactVaultManager.Desktop;

internal static class PerformanceDiagnostics
{
    private const int MaxSamplesPerOperation = 200;
    private static readonly ConcurrentDictionary<string, OperationStats> Stats = new(StringComparer.Ordinal);
    private static readonly object FileLock = new();

    public static bool Enabled { get; } = string.Equals(
        Environment.GetEnvironmentVariable("FACTBURST_PERF_DIAGNOSTICS"),
        "1",
        StringComparison.Ordinal);

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
                AverageMs = pair.Value.Count == 0 ? 0 : pair.Value.TotalMs / pair.Value.Count
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

    public static void WriteReport()
    {
        if (!Enabled)
            return;

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FactburstQuizManager",
                "Diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"ui-performance-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            lock (FileLock)
                File.WriteAllText(path, GetReport());
        }
        catch
        {
            // Diagnostics must never interfere with application shutdown.
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

        public int Count => Volatile.Read(ref _count);
        public double TotalMs => Volatile.Read(ref _totalMs);
        public double MaxMs => Volatile.Read(ref _maxMs);

        public void Record(double milliseconds)
        {
            var count = Interlocked.Increment(ref _count);
            if (count > MaxSamplesPerOperation)
                return;

            Interlocked.Exchange(ref _totalMs, TotalMs + milliseconds);
            while (true)
            {
                var current = MaxMs;
                if (milliseconds <= current || Interlocked.CompareExchange(ref _maxMs, milliseconds, current) == current)
                    break;
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}