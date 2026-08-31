using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class DatabaseBackupServiceTests
{
    [Fact]
    public void Backup_CreatesReadableIntegrityCheckedSQLiteCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "factvault.db");
        var backups = Path.Combine(root, "backups");
        var when = new DateTimeOffset(2026, 8, 31, 19, 30, 15, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(root);
            using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE facts(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO facts(value) VALUES('protected');";
                command.ExecuteNonQuery();
            }

            var service = new DatabaseBackupService();
            var result = service.Backup(source, backups, when);

            Assert.True(File.Exists(result.BackupPath));
            Assert.True(result.Bytes > 0);
            Assert.Equal("factvault-2026-08-31-193015.db", Path.GetFileName(result.BackupPath));
            Assert.True(service.HasBackupForDate(backups, new DateOnly(2026, 8, 31)));

            using (var backup = new SqliteConnection($"Data Source={result.BackupPath};Mode=ReadOnly;Pooling=False"))
            {
                backup.Open();
                using var value = backup.CreateCommand();
                value.CommandText = "SELECT value FROM facts LIMIT 1";
                Assert.Equal("protected", Convert.ToString(value.ExecuteScalar()));
                using var integrity = backup.CreateCommand();
                integrity.CommandText = "PRAGMA integrity_check;";
                Assert.Equal("ok", Convert.ToString(integrity.ExecuteScalar()));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HasBackupForDate_ReturnsFalse_WhenNoBackupExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "FactVaultManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Assert.False(new DatabaseBackupService().HasBackupForDate(root, new DateOnly(2026, 8, 31)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultBackupLocation_IsOnZDrive()
    {
        Assert.StartsWith(@"Z:\", DatabaseBackupService.DefaultBackupDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
