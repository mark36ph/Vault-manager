namespace FactVaultManager.Desktop;

internal static class InstalledDataMigrationMarkerRepair
{
    private const string MigrationMarkerName = "installed-data-migration-v2.json";

    public static void ClearIfStale(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var root = Path.GetFullPath(appDataRoot);
        var markerPath = Path.Combine(root, MigrationMarkerName);
        if (!File.Exists(markerPath))
            return;

        // A completion marker is only valid while the installed database still contains
        // the user's data. If the database is missing, empty or unreadable, remove only
        // the stale marker so the normal migration path can look for an existing user DB.
        // This never creates a database or invents replacement data.
        if (!InstalledDataMigrationGuard.ShouldRun(root))
            return;

        try
        {
            File.Delete(markerPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
