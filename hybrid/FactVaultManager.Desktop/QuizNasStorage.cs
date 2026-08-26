using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public static partial class QuizExportStaging
{
    public static string CreateSessionRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager",
            "QuizStaging",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static QuizVideoBuildResult Publish(
        QuizVideoBuildResult build,
        string projectsRoot,
        string sessionRoot) =>
        Publish(build, projectsRoot, sessionRoot, renderFinalVideo: true);

    internal static QuizVideoBuildResult Publish(
        QuizVideoBuildResult build,
        string projectsRoot,
        string sessionRoot,
        bool renderFinalVideo)
    {
        ArgumentNullException.ThrowIfNull(build);
        var source = Path.GetFullPath(build.ProjectFolder);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The locally staged quiz export was not found: {source}");

        var safeSession = Path.GetFullPath(sessionRoot);
        EnsureContained(safeSession, source, "The staged quiz export is outside its local session folder.");
        var quizRoot = ProjectPathSecurity.CombineContained(projectsRoot, "Quizzes");
        Directory.CreateDirectory(quizRoot);
        var destination = NextDestination(quizRoot, Path.GetFileName(source));
        var temporary = ProjectPathSecurity.EnsureContained(
            projectsRoot,
            destination + ".uploading-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (renderFinalVideo)
            {
                var finalRenderTimeline = QuizFinalRenderTimeline.Prepare(build.Timeline);
                new NativeQuizFinalRenderCoordinator().Render(finalRenderTimeline, source);
            }
            CopyDirectory(source, temporary);
            NativeResolvePortablePathRebaser.RebaseTree(temporary, source, destination);
            Directory.Move(temporary, destination);
            var published = RebaseResult(build, source, destination);
            Directory.Delete(safeSession, recursive: true);
            return published;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static QuizVideoBuildResult RebaseResult(
        QuizVideoBuildResult build,
        string source,
        string destination)
    {
        foreach (var clip in build.Timeline.Tracks.SelectMany(track => track.Clips))
        {
            if (!string.IsNullOrWhiteSpace(clip.Source))
                clip.Source = RebasePath(clip.Source!, source, destination);
        }

        var package = build.ResolveExport.Package;
        var rebasedPackage = new NativePortableResolvePackageResult(
            RebasePath(package.PackageFolder, source, destination),
            package.Files.Select(path => RebasePath(path, source, destination)).ToList(),
            package.CopiedMedia.Select(path => RebasePath(path, source, destination)).ToList(),
            package.Warnings,
            RebasePath(package.TimelinePlan, source, destination),
            RebasePath(package.Manifest, source, destination),
            package.SourceMap.ToDictionary(
                pair => RebasePath(pair.Key, source, destination),
                pair => RebasePath(pair.Value, source, destination),
                StringComparer.OrdinalIgnoreCase));
        var resolve = new NativeResolveFreeExportResult(
            rebasedPackage,
            build.ResolveExport.FcpXml with
            {
                Path = RebasePath(build.ResolveExport.FcpXml.Path, source, destination),
            },
            build.ResolveExport.RemappedMedia,
            build.ResolveExport.ValidatedMedia.Select(path => RebasePath(path, source, destination)).ToList(),
            RebasePath(build.ResolveExport.ImportReadme, source, destination));
        return new QuizVideoBuildResult(
            destination,
            RebasePath(build.QuizJson, source, destination),
            build.Timeline,
            resolve);
    }

    private static string NextDestination(string parent, string sourceName)
    {
        var match = NumberedFolder().Match(sourceName);
        var baseName = match.Success ? match.Groups["name"].Value : sourceName;
        for (var index = 1; index <= 9_999; index++)
        {
            var candidate = Path.Combine(parent, $"{baseName} - {index:000}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }
        throw new IOException($"Could not allocate a quiz export folder for '{baseName}'.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string RebasePath(string value, string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value)) return value;
        var full = Path.GetFullPath(value);
        var relative = Path.GetRelativePath(oldRoot, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return full;
        return Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private static void EnsureContained(string root, string path, string message)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }

    [GeneratedRegex(@"^(?<name>.+) - \d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedFolder();
}

public static class QuizFolderCleanupQueue
{
    private static readonly object Gate = new();
    private static Task? _defaultWorker;

    public static string DefaultQueuePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FactVaultManager",
        "data",
        "quiz-folder-cleanup.json");

    public static void Enqueue(string queuePath, string allowedRoot, string folder)
    {
        var safe = SecureFolder(allowedRoot, folder);
        lock (Gate)
        {
            var entries = Load(queuePath);
            if (!entries.Contains(safe, StringComparer.OrdinalIgnoreCase)) entries.Add(safe);
            Save(queuePath, entries);
        }
    }

    public static void Remove(string queuePath, string folder)
    {
        lock (Gate)
        {
            var entries = Load(queuePath);
            entries.RemoveAll(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase));
            Save(queuePath, entries);
        }
    }

    public static void ProcessDefaultInBackground(string allowedRoot)
    {
        lock (Gate)
        {
            if (_defaultWorker is { IsCompleted: false }) return;
            _defaultWorker = Task.Run(() => ProcessPending(DefaultQueuePath, allowedRoot));
        }
    }

    public static int ProcessPending(string queuePath, string allowedRoot)
    {
        List<string> entries;
        lock (Gate) entries = Load(queuePath);
        var completed = 0;
        foreach (var entry in entries)
        {
            string safe;
            try { safe = SecureFolder(allowedRoot, entry); }
            catch
            {
                Remove(queuePath, entry);
                continue;
            }

            try
            {
                if (Directory.Exists(safe))
                {
                    ClearReadOnlyAttributes(safe);
                    Directory.Delete(safe, recursive: true);
                }
                Remove(queuePath, entry);
                completed++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Keep the entry so cleanup resumes on the next app launch.
            }
        }
        return completed;
    }

    private static string SecureFolder(string root, string folder)
    {
        var safe = ProjectPathSecurity.EnsureContained(root, folder);
        if (string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(safe).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The projects root cannot be queued for deletion.");
        return safe;
    }

    private static List<string> Load(string queuePath)
    {
        if (!File.Exists(queuePath)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(queuePath)) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static void Save(string queuePath, IReadOnlyList<string> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(queuePath))!);
        if (entries.Count == 0)
        {
            if (File.Exists(queuePath)) File.Delete(queuePath);
            return;
        }
        var temporary = queuePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
        File.Move(temporary, queuePath, overwrite: true);
    }

    private static void ClearReadOnlyAttributes(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);
        File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
    }
}
