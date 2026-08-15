namespace FactVaultManager.Desktop;

internal static class TrustedMediaExecutableLocator
{
    public const string ExplicitDirectoryVariable = "FACTVAULT_MEDIA_TOOL_DIR";

    public static string Find(string name)
    {
        return Find(
            name,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable(ExplicitDirectoryVariable),
            DefaultTrustedRoots());
    }

    internal static string Find(
        string name,
        string? pathValue,
        string? explicitDirectory,
        IEnumerable<string> trustedRoots)
    {
        var fileName = ExecutableFileName(name);

        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var configured = explicitDirectory.Trim().Trim('"');
            if (!Path.IsPathRooted(configured))
                throw new NativeFfmpegTimelineException(
                    $"{ExplicitDirectoryVariable} must be an absolute directory path.");

            configured = Path.GetFullPath(configured);
            var explicitCandidate = Path.Combine(configured, fileName);
            if (!File.Exists(explicitCandidate))
                throw new NativeFfmpegTimelineException(
                    $"{fileName} was not found in the explicitly trusted directory: {configured}");
            return Path.GetFullPath(explicitCandidate);
        }

        var normalizedRoots = trustedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.Trim().Trim('"')))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var directDirectory in DirectTrustedDirectories(normalizedRoots))
        {
            var candidate = Path.Combine(directDirectory, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        foreach (var rawFolder in (pathValue ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string folder;
            try
            {
                folder = rawFolder.Trim().Trim('"');
                if (!Path.IsPathRooted(folder))
                    continue;
                folder = Path.GetFullPath(folder);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!normalizedRoots.Any(root => IsWithin(root, folder)))
                continue;

            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new NativeFfmpegTimelineException(
            $"{DisplayName(name)} was not found in a trusted location. " +
            $"Install it under the application, Windows, or Program Files, or set {ExplicitDirectoryVariable} to the trusted FFmpeg bin directory.");
    }

    private static IReadOnlyList<string> DefaultTrustedRoots()
    {
        var roots = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray();
    }

    private static IEnumerable<string> DirectTrustedDirectories(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            yield return Path.Combine(root, "ffmpeg", "bin");
            yield return Path.Combine(root, "tools", "ffmpeg", "bin");
            yield return Path.Combine(root, "tools", "ffmpeg");
        }
    }

    internal static bool IsWithin(string root, string candidate)
    {
        var rootFull = Path.GetFullPath(root);
        var candidateFull = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(rootFull, candidateFull);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string ExecutableFileName(string name)
    {
        var normalized = (name ?? "").Trim();
        if (!normalized.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("ffprobe", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new NativeFfmpegTimelineException("Only FFmpeg and FFprobe executable discovery is supported.");
        }

        if (OperatingSystem.IsWindows() && !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalized += ".exe";
        return normalized;
    }

    private static string DisplayName(string name) =>
        (name ?? "").Contains("probe", StringComparison.OrdinalIgnoreCase) ? "FFprobe" : "FFmpeg";
}
