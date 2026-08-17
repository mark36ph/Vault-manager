namespace FactVaultManager.Desktop;

public sealed record QuizFinalizedProject(string ProjectFolder, string QuizJson);

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

        var fileName = Path.GetFileName(path);
        if (fileName.Length == 0 || !char.IsDigit(fileName[0]))
            return path;

        var destination = Path.Combine(Path.GetDirectoryName(path)!, $"still_{fileName}");
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(path, destination);
        return destination;
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
