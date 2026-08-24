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
        var portableRoot = Path.Combine(Path.GetFullPath(newFolder), "Resolve", "Portable");
        return RebaseTree(portableRoot, oldFolder, newFolder);
    }

    public static int RebaseTree(string folderToScan, string oldFolder, string newFolder)
    {
        oldFolder = Path.GetFullPath(oldFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        newFolder = Path.GetFullPath(newFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        folderToScan = Path.GetFullPath(folderToScan);
        if (!Directory.Exists(folderToScan))
            return 0;

        var replacements = BuildReplacements(oldFolder, newFolder);
        var changedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(folderToScan, "*", SearchOption.AllDirectories))
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
        var oldXmlUri = System.Security.SecurityElement.Escape(oldUri) ?? oldUri;
        var newXmlUri = System.Security.SecurityElement.Escape(newUri) ?? newUri;

        return new (string, string)[]
        {
            (oldJson, newJson),
            (oldXmlUri, newXmlUri),
            (oldUri, newUri),
            (oldForward, newForward),
            (oldFolder, newFolder),
        };
    }
}
