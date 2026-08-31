using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FactVaultManager.Desktop;

public sealed record QuizProjectArchiveCapture(
    string QuizJson,
    byte[] Archive,
    string Sha256,
    long SourceBytes,
    long ArchiveBytes,
    int FileCount);

public sealed record QuizProjectArchiveRestoreResult(
    int RestoredFiles,
    int SkippedFiles,
    long RestoredBytes);

public static class QuizProjectDatabaseArchive
{
    private static readonly HashSet<string> DerivedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v", ".wmv",
    };

    public static QuizProjectArchiveCapture? TryCapture(string? projectFolder)
    {
        var folder = (projectFolder ?? "").Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
            return null;

        var quizJsonPath = Path.Combine(folder, "quiz.json");
        if (!File.Exists(quizJsonPath))
            return null;

        return Capture(folder);
    }

    public static QuizProjectArchiveCapture Capture(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("Project folder is required.", nameof(projectFolder));

        var root = Path.GetFullPath(projectFolder);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Quiz project folder was not found: {root}");

        var quizJsonPath = Path.Combine(root, "quiz.json");
        if (!File.Exists(quizJsonPath))
            throw new FileNotFoundException("quiz.json was not found for this quiz project.", quizJsonPath);

        var quizJson = File.ReadAllText(quizJsonPath, Encoding.UTF8);
        using var output = new MemoryStream();
        long sourceBytes = 0;
        var fileCount = 0;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldStore(file))
                    continue;

                var fullPath = Path.GetFullPath(file);
                var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
                if (!IsSafeRelativePath(relative))
                    throw new InvalidDataException($"Unsafe project file path cannot be archived: {relative}");

                var info = new FileInfo(fullPath);
                sourceBytes += info.Length;
                fileCount++;

                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var destination = entry.Open();
                input.CopyTo(destination);
            }
        }

        var bytes = output.ToArray();
        return new QuizProjectArchiveCapture(
            quizJson,
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            sourceBytes,
            bytes.LongLength,
            fileCount);
    }

    public static QuizProjectArchiveRestoreResult Restore(
        byte[] archiveBytes,
        string expectedSha256,
        string destinationFolder,
        bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        if (archiveBytes.Length == 0)
            throw new InvalidDataException("The database recovery package is empty.");
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("Destination folder is required.", nameof(destinationFolder));

        var actualHash = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, (expectedSha256 ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The database recovery package failed its SHA-256 integrity check.");

        var root = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(root);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var restored = 0;
        var skipped = 0;
        long restoredBytes = 0;

        using var memory = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (!IsSafeRelativePath(entry.FullName))
                throw new InvalidDataException($"Unsafe recovery package path was rejected: {entry.FullName}");

            var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(root, normalized));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Recovery package path escapes the project folder: {entry.FullName}");

            if (File.Exists(destination) && !overwriteExisting)
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + $".restore-{Guid.NewGuid():N}.tmp";
            try
            {
                using (var input = entry.Open())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);

                File.Move(temporary, destination, overwrite: true);
                if (entry.LastWriteTime != default)
                    File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
                restored++;
                restoredBytes += entry.Length;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        return new QuizProjectArchiveRestoreResult(restored, skipped, restoredBytes);
    }

    public static bool IsDerivedVideo(string path) =>
        DerivedVideoExtensions.Contains(Path.GetExtension(path ?? ""));

    private static bool ShouldStore(string path)
    {
        if (IsDerivedVideo(path))
            return false;

        var name = Path.GetFileName(path);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;

        var normalized = path.Replace('\\', '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }
}
