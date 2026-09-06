namespace FactVaultManager.Desktop;

internal static class InstalledDataMigrationGuard
{
    public static bool ShouldRun(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var dataDirectory = Path.Combine(Path.GetFullPath(appDataRoot), "data");
        if (!Directory.Exists(dataDirectory))
            return true;

        // A partially-created installed data directory (for example settings.json
        // without factvault.db) must still be allowed to recover the database from
        // the legacy/source location. A real installed database is the ownership
        // boundary: never replace it during bootstrap migration.
        return !File.Exists(Path.Combine(dataDirectory, "factvault.db"));
    }
}
