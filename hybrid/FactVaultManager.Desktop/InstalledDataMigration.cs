namespace FactVaultManager.Desktop;

public static class InstalledDataMigration
{
    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");
        var destination = Path.Combine(appDataRoot, "data");
        if (File.Exists(Path.Combine(destination, "factvault.db")))
        {
            return;
        }

        Directory.CreateDirectory(appDataRoot);
        foreach (var candidate in CandidateRoots(appDataRoot))
        {
            var source = Path.Combine(candidate, "data");
            if (!File.Exists(Path.Combine(source, "factvault.db")))
            {
                continue;
            }

            CopyDirectory(source, destination);
            return;
        }
    }

    private static IEnumerable<string> CandidateRoots(string appDataRoot)
    {
        var marker = Path.Combine(appDataRoot, "development-root.txt");
        if (File.Exists(marker))
        {
            var marked = File.ReadAllText(marker).Trim();
            if (!string.IsNullOrWhiteSpace(marked)) yield return marked;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(documents, "FactVaultManager");
        yield return Path.Combine(documents, "Fact Vault Manager");
        yield return Path.Combine(documents, "Vault-manager");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
