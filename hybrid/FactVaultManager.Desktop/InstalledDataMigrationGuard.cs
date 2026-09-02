namespace FactVaultManager.Desktop;

internal static class InstalledDataMigrationGuard
{
    public static bool ShouldRun(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var dataDirectory = Path.Combine(Path.GetFullPath(appDataRoot), "data");
        if (!Directory.Exists(dataDirectory))
            return true;

        // Once the installed data directory exists, it belongs to the installed copy.
        // In particular, settings.json can contain credentials even when the SQLite
        // database has no user records yet. Never let bootstrap migration replace it
        // with an older source directory.
        return !Directory.EnumerateFileSystemEntries(dataDirectory).Any();
    }
}
