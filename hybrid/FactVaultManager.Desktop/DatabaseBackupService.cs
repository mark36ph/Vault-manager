using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop;

public sealed record DatabaseBackupResult(
    string BackupPath,
    long Bytes,
    DateTimeOffset CreatedAt);

public sealed class DatabaseBackupService
{
    public const string DefaultBackupDirectory = @"Z:\Factburst Quiz Manager\Database Backups";

    public DatabaseBackupResult Backup(string databasePath, string? backupDirectory = null, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        var sourcePath = Path.GetFullPath(databasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The FactVault database was not found.", sourcePath);

        var targetDirectory = string.IsNullOrWhiteSpace(backupDirectory)
            ? DefaultBackupDirectory
            : Path.GetFullPath(backupDirectory);
        EnsureTargetAvailable(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        var timestamp = now ?? DateTimeOffset.Now;
        var fileName = $"factvault-{timestamp:yyyy-MM-dd-HHmmss}.db";
        var finalPath = Path.Combine(targetDirectory, fileName);
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            var sourceBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
            };
            var destinationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
            };

            using (var source = new SqliteConnection(sourceBuilder.ToString()))
            using (var destination = new SqliteConnection(destinationBuilder.ToString()))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
                VerifyIntegrity(destination);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return new DatabaseBackupResult(finalPath, new FileInfo(finalPath).Length, timestamp);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public bool HasBackupForDate(string? backupDirectory, DateOnly date)
    {
        var targetDirectory = string.IsNullOrWhiteSpace(backupDirectory)
            ? DefaultBackupDirectory
            : Path.GetFullPath(backupDirectory);
        if (!Directory.Exists(targetDirectory))
            return false;

        var prefix = $"factvault-{date:yyyy-MM-dd}-";
        return Directory.EnumerateFiles(targetDirectory, "factvault-*.db", SearchOption.TopDirectoryOnly)
            .Any(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTargetAvailable(string? backupDirectory = null)
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(backupDirectory)
                ? DefaultBackupDirectory
                : Path.GetFullPath(backupDirectory);
            EnsureTargetAvailable(target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(command.ExecuteScalar()) ?? "";
        if (!string.Equals(result.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite integrity check failed for the new backup: {result}");
    }

    private static void EnsureTargetAvailable(string targetDirectory)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.GetPathRoot(targetDirectory);
        if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
            throw new DirectoryNotFoundException($"Backup drive is unavailable: {root}");
    }
}
