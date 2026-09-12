using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledDataMigrationGuardTests
{
    [Fact]
    public void AllowsMigrationWhenInstalledDataDirectoryDoesNotExist()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");

        Assert.True(InstalledDataMigrationGuard.ShouldRun(appDataRoot));
    }

    [Fact]
    public void AllowsMigrationWhenInstalledDataDirectoryIsEmpty()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        Directory.CreateDirectory(Path.Combine(appDataRoot, "data"));

        Assert.True(InstalledDataMigrationGuard.ShouldRun(appDataRoot));
    }

    [Fact]
    public void AllowsMigrationWhenInstalledDatabaseIsMissingEvenIfSettingsExist()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var data = Path.Combine(appDataRoot, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"ai\":{\"api_key\":\"protected-placeholder\"}}");

        Assert.True(InstalledDataMigrationGuard.ShouldRun(appDataRoot));
    }

    [Fact]
    public void AllowsMigrationWhenInstalledDatabaseExistsButIsEmpty()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var data = Path.Combine(appDataRoot, "data");
        var database = Path.Combine(data, "factvault.db");
        Directory.CreateDirectory(data);

        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE projects (id INTEGER PRIMARY KEY, title TEXT NOT NULL DEFAULT '')";
        command.ExecuteNonQuery();

        Assert.True(InstalledDataMigrationGuard.ShouldRun(appDataRoot));
    }

    [Fact]
    public void BlocksMigrationWhenInstalledDatabaseContainsUserData()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var data = Path.Combine(appDataRoot, "data");
        var database = Path.Combine(data, "factvault.db");
        Directory.CreateDirectory(data);

        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE projects (id INTEGER PRIMARY KEY, title TEXT NOT NULL DEFAULT ''); INSERT INTO projects(title) VALUES('Existing project')";
        command.ExecuteNonQuery();

        Assert.False(InstalledDataMigrationGuard.ShouldRun(appDataRoot));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
