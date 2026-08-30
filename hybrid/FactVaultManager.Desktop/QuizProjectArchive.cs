namespace FactVaultManager.Desktop;

public sealed record QuizProjectArchiveResult(string DestinationFolder, bool SourceDeleted, string Warning);

public static class QuizProjectArchive
{
    public static string CopyAndVerify(string sourceFolder, string projectsRoot, string archiveRoot)
    {
        var source = ProjectPathSecurity.EnsureContained(projectsRoot, sourceFolder);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The quiz project folder was not found: {source}");
        if (string.IsNullOrWhiteSpace(archiveRoot))
            throw new InvalidOperationException("Choose a NAS archive folder in Settings → General first.");

        var archive = Path.GetFullPath(archiveRoot.Trim());
        var projects = Path.GetFullPath(projectsRoot);
        if (IsWithin(projects, archive) || IsWithin(archive, projects))
            throw new InvalidOperationException("The NAS archive folder must be separate from the Projects folder.");

        Directory.CreateDirectory(archive);
        var relative = Path.GetRelativePath(projects, source);
        var requestedDestination = ProjectPathSecurity.EnsureContained(archive, Path.Combine(archive, relative));
        return CopyAndVerifyToRequestedDestination(source, archive, requestedDestination);
    }

    public static string CopyAndVerifyToQuizArchive(string sourceFolder, string quizArchiveRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            throw new ArgumentException("The quiz project folder is blank.", nameof(sourceFolder));
        if (string.IsNullOrWhiteSpace(quizArchiveRoot))
            throw new ArgumentException("The quiz archive folder is blank.", nameof(quizArchiveRoot));

        var source = Path.GetFullPath(sourceFolder.Trim());
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The quiz project folder was not found: {source}");

        var archive = Path.GetFullPath(quizArchiveRoot.Trim());
        if (IsWithin(source, archive) || IsWithin(archive, source))
            throw new InvalidOperationException("The quiz archive folder must be separate from the source project folder.");

        var sourceName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName) || sourceName is "." or "..")
            throw new InvalidOperationException("The quiz project folder name could not be determined safely.");

        Directory.CreateDirectory(archive);
        var requestedDestination = ProjectPathSecurity.EnsureContained(archive, Path.Combine(archive, sourceName));
        return CopyAndVerifyToRequestedDestination(source, archive, requestedDestination);
    }

    public static bool AreDirectoriesEquivalent(string leftFolder, string rightFolder)
    {
        if (string.IsNullOrWhiteSpace(leftFolder) || string.IsNullOrWhiteSpace(rightFolder))
            return false;

        var left = Path.GetFullPath(leftFolder.Trim());
        var right = Path.GetFullPath(rightFolder.Trim());
        if (!Directory.Exists(left) || !Directory.Exists(right))
            return false;

        var leftFiles = BuildFileMap(left);
        var rightFiles = BuildFileMap(right);
        return leftFiles.Count == rightFiles.Count &&
               leftFiles.All(pair => rightFiles.TryGetValue(pair.Key, out var length) && length == pair.Value);
    }

    public static void DeleteSource(string sourceFolder)
    {
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(sourceFolder, recursive: true);
    }

    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) DeleteSource(folder);
        }
        catch
        {
        }
    }

    private static string CopyAndVerifyToRequestedDestination(string source, string archiveRoot, string requestedDestination)
    {
        var destination = AllocateDestination(requestedDestination);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var temporary = ProjectPathSecurity.EnsureContained(
            archiveRoot,
            destination + ".archiving-" + Guid.NewGuid().ToString("N")[..8]);
        var destinationCreated = false;
        try
        {
            CopyDirectory(source, temporary);
            VerifyCopy(source, temporary);
            Directory.Move(temporary, destination);
            destinationCreated = true;
            NativeResolvePortablePathRebaser.RebaseTree(destination, source, destination);
            return destination;
        }
        catch
        {
            TryDelete(temporary);
            if (destinationCreated) TryDelete(destination);
            throw;
        }
    }

    private static string AllocateDestination(string requestedDestination)
    {
        if (!Directory.Exists(requestedDestination) && !File.Exists(requestedDestination))
            return requestedDestination;

        for (var index = 1; index <= 9_999; index++)
        {
            var candidate = requestedDestination + $" - archived-copy {index:000}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }

        throw new IOException($"Could not allocate an archive folder for '{requestedDestination}'.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void VerifyCopy(string source, string destination)
    {
        var sourceFiles = BuildFileMap(source);
        var destinationFiles = BuildFileMap(destination);
        if (sourceFiles.Count != destinationFiles.Count ||
            sourceFiles.Any(pair => !destinationFiles.TryGetValue(pair.Key, out var length) || length != pair.Value))
            throw new IOException("The NAS copy could not be verified, so the local project was not removed.");
    }

    private static Dictionary<string, long> BuildFileMap(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => new FileInfo(path).Length,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }
}

public sealed partial class DesktopDataService
{
    public QuizProjectArchiveResult ArchiveQuizProject(int historyId)
    {
        var history = GetQuizHistory().FirstOrDefault(item => item.Id == historyId)
            ?? throw new InvalidOperationException("The quiz no longer exists in Quiz History.");
        var settings = LoadSettings();
        var projectsRoot = GetProjectsRoot();
        var destination = QuizProjectArchive.CopyAndVerify(
            history.ProjectFolder,
            projectsRoot,
            settings.NasArchiveFolder);

        try
        {
            if (!UpdateQuizHistoryProjectFolder(history.Id, destination))
                throw new InvalidOperationException("Quiz History could not be updated to the NAS location.");
        }
        catch
        {
            QuizProjectArchive.TryDelete(destination);
            throw;
        }

        try
        {
            QuizProjectArchive.DeleteSource(history.ProjectFolder);
            return new QuizProjectArchiveResult(destination, true, "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuizProjectArchiveResult(
                destination,
                false,
                "The NAS copy is complete and Quiz History was updated, but the local folder could not be removed: " + error.Message);
        }
    }
}
