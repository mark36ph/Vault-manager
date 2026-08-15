namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public string EnsureProjectFolder(DesktopProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var expected = ResolveProjectFolder(project);
        if (Directory.Exists(expected))
            return expected;

        var root = GetProjectsRoot();
        var matches = FindProjectFolderMatches(root, project.Title, expected);
        if (matches.Count > 1)
        {
            throw new IOException(
                $"Project folder is missing at '{expected}', and multiple folders named '{project.Title}' were found under '{root}'. " +
                "Move the correct project folder to the expected location before continuing.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        if (matches.Count == 1)
        {
            Directory.Move(matches[0], expected);
        }

        CreateStandardProjectFolders(expected);
        return expected;
    }

    private static IReadOnlyList<string> FindProjectFolderMatches(string root, string title, string expected)
    {
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        var expectedFullPath = Path.GetFullPath(expected);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(Path.Combine(root, title));
        foreach (var firstLevel in Directory.EnumerateDirectories(root))
        {
            AddCandidate(Path.Combine(firstLevel, title));
            foreach (var secondLevel in Directory.EnumerateDirectories(firstLevel))
                AddCandidate(Path.Combine(secondLevel, title));
        }

        return found.ToList();

        void AddCandidate(string candidate)
        {
            if (!Directory.Exists(candidate))
                return;

            var fullPath = Path.GetFullPath(candidate);
            if (!string.Equals(fullPath, expectedFullPath, StringComparison.OrdinalIgnoreCase))
                found.Add(fullPath);
        }
    }

    private static void CreateStandardProjectFolders(string folder)
    {
        foreach (var path in new[]
        {
            folder,
            Path.Combine(folder, "Assets", "Images"),
            Path.Combine(folder, "Assets", "Videos"),
            Path.Combine(folder, "Assets", "Music"),
            Path.Combine(folder, "Assets", "SFX"),
            Path.Combine(folder, "Assets", "Overlays"),
            Path.Combine(folder, "Assets", "Thumbnails"),
            Path.Combine(folder, "Export"),
            Path.Combine(folder, "Voice"),
        })
        {
            Directory.CreateDirectory(path);
        }
    }
}
