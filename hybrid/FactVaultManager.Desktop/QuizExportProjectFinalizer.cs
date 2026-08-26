using System.Security.Cryptography;
using System.Text;

namespace FactVaultManager.Desktop;

public sealed record QuizFinalizedProject(string ProjectFolder, string QuizJson);

public static class QuizExportFolderNaming
{
    public static string BaseName(string title, bool vertical)
    {
        var name = (title ?? "").Trim();
        if (name.Length == 0)
            throw new ArgumentException("Quiz title is required.", nameof(title));
        return vertical ? $"{name} - Short" : name;
    }
}

public static class QuizExportProjectFinalizer
{
    public static QuizFinalizedProject Prepare(QuizVideoBuildResult build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var sourceRoot = Path.GetFullPath(build.ProjectFolder);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Quiz export folder was not found: {sourceRoot}");

        var parent = Directory.GetParent(sourceRoot)?.FullName
            ?? throw new InvalidOperationException("Quiz export folder does not have a parent folder.");
        var baseName = Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destinationRoot = NextUniqueFolder(parent, baseName);
        Directory.Move(sourceRoot, destinationRoot);

        var normalizedImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in build.Timeline.Tracks.SelectMany(track => track.Clips))
        {
            if (string.IsNullOrWhiteSpace(clip.Source))
                continue;

            var movedSource = RebaseContainedPath(sourceRoot, destinationRoot, clip.Source!);
            if (clip.Kind == NativeTimelineClipKind.Image && IsContained(destinationRoot, movedSource))
            {
                if (!normalizedImages.TryGetValue(movedSource, out var normalized))
                {
                    normalized = NormalizeStillFileName(movedSource);
                    normalizedImages[movedSource] = normalized;
                }
                movedSource = normalized;
            }
            clip.Source = movedSource;
        }

        var quizJson = RebaseContainedPath(sourceRoot, destinationRoot, build.QuizJson);
        build.Timeline.Validate();
        new NativeProjectTimelineStore(destinationRoot).Save(build.Timeline);
        return new QuizFinalizedProject(destinationRoot, quizJson);
    }

    private static string NextUniqueFolder(string parent, string baseName)
    {
        for (var index = 1; index <= 9_999; index++)
        {
            var candidate = Path.Combine(parent, $"{baseName} - {index:000}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }
        throw new IOException($"Could not allocate a unique quiz export folder for '{baseName}'.");
    }

    private static string NormalizeStillFileName(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Quiz still image was not found after finalizing the export folder.", path);

        var extension = Path.GetExtension(path);
        var token = AlphabeticToken(path);
        var destination = Path.Combine(Path.GetDirectoryName(path)!, $"quizcard_{token}{extension}");
        if (string.Equals(path, destination, StringComparison.OrdinalIgnoreCase))
            return path;
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(path, destination);
        return destination;
    }

    private static string AlphabeticToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var characters = new char[12];
        for (var index = 0; index < characters.Length; index++)
            characters[index] = (char)('a' + (hash[index] % 26));
        return new string(characters);
    }

    private static string RebaseContainedPath(string oldRoot, string newRoot, string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsContained(oldRoot, full))
            return full;
        var relative = Path.GetRelativePath(oldRoot, full);
        return Path.GetFullPath(Path.Combine(newRoot, relative));
    }

    private static bool IsContained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }
}
