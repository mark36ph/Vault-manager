using System.Diagnostics;

namespace FactVaultManager.Desktop;

public static class InstalledRenamedTrackerSettingsRecovery
{
    private const string TrackerFileName = "factburst-link-tracker.json";
    private const int MaxSearchDepth = 5;
    private const int MaxSearchDirectories = 2_000;

    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");

        try
        {
            _ = Run(appDataRoot);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            // This is a best-effort final recovery pass for development folders that
            // were renamed after migration. It must never prevent normal startup.
            Debug.WriteLine($"Renamed development tracker recovery could not complete: {error}");
        }
    }

    internal static InstalledTrackerSettingsRecoveryResult Run(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        return InstalledTrackerSettingsRecovery.Run(
            appDataRoot,
            Array.Empty<string>(),
            CandidateTrackerPaths(appDataRoot));
    }

    internal static IEnumerable<string> CandidateTrackerPaths(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var developmentRoot = ReadDevelopmentRoot(appDataRoot);
        if (developmentRoot.Length == 0)
            yield break;

        string normalizedDevelopmentRoot;
        try { normalizedDevelopmentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(developmentRoot)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { yield break; }

        var parent = Path.GetDirectoryName(normalizedDevelopmentRoot) ?? "";
        var originalName = Path.GetFileName(normalizedDevelopmentRoot);
        if (parent.Length == 0 || originalName.Length == 0 || !Directory.Exists(parent))
            yield break;

        string[] siblings;
        try { siblings = Directory.GetDirectories(parent, "*", SearchOption.TopDirectoryOnly); }
        catch { siblings = Array.Empty<string>(); }

        var seen = new HashSet<string>(PathComparer());
        foreach (var sibling in siblings.OrderByDescending(SafeLastWriteUtc))
        {
            var siblingName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sibling));
            if (!LooksLikeRenamedSibling(originalName, siblingName))
                continue;

            foreach (var knownPath in new[]
                     {
                         Path.Combine(sibling, "data", TrackerFileName),
                         Path.Combine(sibling, TrackerFileName),
                         Path.Combine(sibling, "hybrid", "FactVaultManager.Desktop", "data", TrackerFileName),
                     })
            {
                if (File.Exists(knownPath) && seen.Add(Path.GetFullPath(knownPath)))
                    yield return knownPath;
            }

            foreach (var found in SearchTrackerFiles(sibling))
            {
                var full = Path.GetFullPath(found);
                if (seen.Add(full))
                    yield return full;
            }
        }
    }

    private static bool LooksLikeRenamedSibling(string originalName, string candidateName)
    {
        if (!candidateName.StartsWith(originalName, StringComparison.OrdinalIgnoreCase) ||
            candidateName.Length <= originalName.Length)
        {
            return false;
        }

        return candidateName[originalName.Length] is '-' or '_' or ' ' or '.' or '(';
    }

    private static IEnumerable<string> SearchTrackerFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        var queue = new Queue<(string Directory, int Depth)>();
        var visited = new HashSet<string>(PathComparer());
        queue.Enqueue((Path.GetFullPath(root), 0));
        var directoriesVisited = 0;

        while (queue.Count > 0 && directoriesVisited < MaxSearchDirectories)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;
            directoriesVisited++;

            string[] files;
            try { files = Directory.GetFiles(directory, TrackerFileName, SearchOption.TopDirectoryOnly); }
            catch { files = Array.Empty<string>(); }

            foreach (var file in files.OrderByDescending(SafeLastWriteUtc))
                yield return file;

            if (depth >= MaxSearchDepth)
                continue;

            string[] children;
            try { children = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly); }
            catch { children = Array.Empty<string>(); }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ShouldSkipDirectory(name))
                    continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool ShouldSkipDirectory(string name) =>
        name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(".venv", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("venv", StringComparison.OrdinalIgnoreCase);

    private static string ReadDevelopmentRoot(string appDataRoot)
    {
        var marker = Path.Combine(Path.GetFullPath(appDataRoot), "development-root.txt");
        if (!File.Exists(marker))
            return "";

        try { return File.ReadAllText(marker).Trim(); }
        catch { return ""; }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
