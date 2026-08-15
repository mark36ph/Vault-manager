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
        foreach (var source in CandidateDataDirectories(appDataRoot))
        {
            if (!File.Exists(Path.Combine(source, "factvault.db")))
            {
                continue;
            }

            CopyDirectory(source, destination);
            return;
        }
    }

    private static IEnumerable<string> CandidateDataDirectories(string appDataRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CandidateRoots(appDataRoot))
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            var data = Path.Combine(fullRoot, "data");
            if (seen.Add(data))
            {
                yield return data;
            }
        }

        // Velopack has used version/current folders under the application root.
        // Check them directly in case a previous build briefly resolved data there.
        if (Directory.Exists(appDataRoot))
        {
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(appDataRoot, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                directories = Array.Empty<string>();
            }

            foreach (var directory in directories)
            {
                var data = Path.Combine(directory, "data");
                if (seen.Add(data))
                {
                    yield return data;
                }
            }
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

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in NamedDocumentRoots(documents))
            yield return root;

        // MyDocuments can point at OneDrive even when the original checkout is in
        // C:\Users\<user>\Documents, so always check the physical profile path too.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var profileDocuments = Path.Combine(userProfile, "Documents");
        foreach (var root in NamedDocumentRoots(profileDocuments))
            yield return root;
    }

    private static IEnumerable<string> NamedDocumentRoots(string documents)
    {
        if (string.IsNullOrWhiteSpace(documents))
            yield break;

        yield return Path.Combine(documents, "FactVaultManager");
        yield return Path.Combine(documents, "Fact Vault Manager");
        yield return Path.Combine(documents, "Vault-manager");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target, overwrite: false);
            }
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
