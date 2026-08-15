namespace FactVaultManager.Desktop;

internal static class ProjectPathSecurity
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string ValidateSegment(string? value, string displayName)
    {
        var segment = (value ?? "").Trim();
        if (segment.Length == 0)
            throw new ArgumentException($"{displayName} is required.");
        if (segment is "." or ".." || Path.IsPathRooted(segment))
            throw new ArgumentException($"{displayName} contains an unsafe path.");
        if (segment.IndexOf(Path.DirectorySeparatorChar) >= 0 || segment.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException($"{displayName} cannot contain path separators.");
        if (segment.Any(char.IsControl) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"{displayName} contains characters that are not valid in a Windows folder name.");
        if (segment.EndsWith(' ') || segment.EndsWith('.'))
            throw new ArgumentException($"{displayName} cannot end with a space or period.");
        if (segment.Length > 120)
            throw new ArgumentException($"{displayName} must be 120 characters or fewer.");

        var deviceStem = segment.Split('.', 2)[0];
        if (ReservedWindowsNames.Contains(deviceStem))
            throw new ArgumentException($"{displayName} uses a reserved Windows device name.");
        return segment;
    }

    public static string CombineContained(string root, params string[] segments)
    {
        var rootFull = NormalizeRoot(root);
        var candidate = rootFull;
        foreach (var segment in segments)
            candidate = Path.Combine(candidate, segment);
        return EnsureContained(rootFull, candidate);
    }

    public static string ResolveContained(string root, string storedPath)
    {
        var rootFull = NormalizeRoot(root);
        var candidate = Path.IsPathRooted(storedPath)
            ? Path.GetFullPath(storedPath)
            : Path.GetFullPath(Path.Combine(rootFull, storedPath));
        return EnsureContained(rootFull, candidate);
    }

    public static string EnsureContained(string root, string candidate)
    {
        var rootFull = NormalizeRoot(root);
        var candidateFull = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(rootFull, candidateFull);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Project path resolves outside the configured Projects folder.");
        }
        return candidateFull;
    }

    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Projects folder is required.", nameof(root));
        return Path.GetFullPath(root.Trim());
    }
}
