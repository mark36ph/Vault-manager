using System.Text;

namespace FactVaultManager.Desktop;

public static class NativeResolvePortablePathRebaser
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fcpxml", ".json", ".txt",
    };

    public static int Rebase(string oldFolder, string newFolder)
    {
        oldFolder = Path.GetFullPath(oldFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        newFolder = Path.GetFullPath(newFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var portableRoot = Path.Combine(newFolder, "Resolve", "Portable");
        if (!Directory.Exists(portableRoot))
            return 0;

        var replacements = BuildReplacements(oldFolder, newFolder);
        var changedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(portableRoot, "*", SearchOption.AllDirectories))
        {
            if (!TextExtensions.Contains(Path.GetExtension(file)))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var updated = text;
            foreach (var (oldValue, newValue) in replacements)
                updated = updated.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(updated, text, StringComparison.Ordinal))
                continue;

            File.WriteAllText(file, updated, new UTF8Encoding(false));
            changedFiles++;
        }

        return changedFiles;
    }

    private static IReadOnlyList<(string OldValue, string NewValue)> BuildReplacements(
        string oldFolder,
        string newFolder)
    {
        var oldJson = oldFolder.Replace("\\", "\\\\", StringComparison.Ordinal);
        var newJson = newFolder.Replace("\\", "\\\\", StringComparison.Ordinal);
        var oldForward = oldFolder.Replace('\\', '/');
        var newForward = newFolder.Replace('\\', '/');
        var oldUri = new Uri(oldFolder + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
        var newUri = new Uri(newFolder + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');

        return new (string, string)[]
        {
            (oldJson, newJson),
            (oldUri, newUri),
            (oldForward, newForward),
            (oldFolder, newFolder),
        };
    }
}
