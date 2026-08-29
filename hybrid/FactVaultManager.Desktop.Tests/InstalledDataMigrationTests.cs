using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledDataMigrationTests
{
    [Fact]
    public void MigratesLegacyDatabaseSettingsProjectsAndStoredPaths()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationData = Path.Combine(appDataRoot, "data");
        Directory.CreateDirectory(destinationData);
        CreateDatabase(Path.Combine(destinationData, "factvault.db"));
        File.WriteAllText(Path.Combine(destinationData, "settings.json"), "{}");

        var legacyRoot = Path.Combine(sandbox.Path, "Vault-manager");
        var legacyData = Path.Combine(legacyRoot, "data");
        var legacyProjects = Path.Combine(legacyRoot, "quiz-projects");
        var legacyProject = Path.Combine(legacyProjects, "Completed", "Space Quiz 001");
        Directory.CreateDirectory(legacyProject);
        File.WriteAllText(Path.Combine(legacyProject, "project.json"), "legacy quiz project");
        var finalVideo = Path.Combine(legacyProject, "final.mp4");
        File.WriteAllBytes(finalVideo, [1, 2, 3, 4, 5]);

        var managedImage = Path.Combine(legacyData, "quiz", "question-images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(managedImage)!);
        File.WriteAllBytes(managedImage, [6, 7, 8]);

        Directory.CreateDirectory(legacyData);
        CreateDatabase(
            Path.Combine(legacyData, "factvault.db"),
            projectTitle: "Legacy Space Quiz",
            historyProjectFolder: legacyProject,
            imagePath: managedImage);
        File.WriteAllText(
            Path.Combine(legacyData, "settings.json"),
            $$"""
            {
              "general": {
                "projects_folder": "{{JsonEscape(legacyProjects)}}",
                "theme": "dark"
              },
              "ai": {
                "api_key": "legacy-protected-key"
              }
            }
            """);

        var result = InstalledDataMigration.Run(appDataRoot, [legacyRoot]);

        Assert.True(result.Completed);
        Assert.True(result.DataCopied);
        Assert.True(result.ProjectsCopied);
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-data-migration-v2.json")));

        var migratedSettings = JsonNode.Parse(File.ReadAllText(Path.Combine(destinationData, "settings.json")))!;
        Assert.Equal(
            Path.GetFullPath(Path.Combine(appDataRoot, "projects")),
            Path.GetFullPath(migratedSettings["general"]!["projects_folder"]!.GetValue<string>()));
        Assert.Equal("legacy-protected-key", migratedSettings["ai"]!["api_key"]!.GetValue<string>());

        var migratedProject = Path.Combine(appDataRoot, "projects", "Completed", "Space Quiz 001");
        Assert.True(File.Exists(Path.Combine(migratedProject, "project.json")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(Path.Combine(migratedProject, "final.mp4")));

        using var connection = Open(Path.Combine(destinationData, "factvault.db"));
        Assert.Equal("Legacy Space Quiz", ScalarText(connection, "SELECT title FROM projects LIMIT 1"));
        Assert.Equal(
            Path.GetFullPath(migratedProject),
            Path.GetFullPath(ScalarText(connection, "SELECT project_folder FROM quiz_history LIMIT 1")));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(appDataRoot, "data", "quiz", "question-images", "logo.png")),
            Path.GetFullPath(ScalarText(connection, "SELECT image_path FROM quiz_questions LIMIT 1")));

        Assert.True(File.Exists(Path.Combine(legacyData, "factvault.db")));
        Assert.True(File.Exists(finalVideo));
        Assert.True(Directory.Exists(Path.Combine(appDataRoot, "migration-backup")));
    }

    [Fact]
    public void DoesNotOverwriteInstalledDatabaseThatAlreadyHasUserData()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationData = Path.Combine(appDataRoot, "data");
        Directory.CreateDirectory(destinationData);
        CreateDatabase(
            Path.Combine(destinationData, "factvault.db"),
            projectTitle: "Installed Project");

        var legacyRoot = Path.Combine(sandbox.Path, "Vault-manager");
        var legacyData = Path.Combine(legacyRoot, "data");
        Directory.CreateDirectory(legacyData);
        CreateDatabase(
            Path.Combine(legacyData, "factvault.db"),
            projectTitle: "Legacy Project");

        var result = InstalledDataMigration.Run(appDataRoot, [legacyRoot]);

        Assert.False(result.Completed);
        Assert.False(result.DataCopied);
        using var connection = Open(Path.Combine(destinationData, "factvault.db"));
        Assert.Equal("Installed Project", ScalarText(connection, "SELECT title FROM projects LIMIT 1"));
        Assert.False(File.Exists(Path.Combine(appDataRoot, "installed-data-migration-v2.json")));
    }

    [Fact]
    public void MigrationMarkerMakesSuccessfulMigrationOneTimeOnly()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var legacyRoot = Path.Combine(sandbox.Path, "Vault-manager");
        var legacyData = Path.Combine(legacyRoot, "data");
        Directory.CreateDirectory(legacyData);
        CreateDatabase(
            Path.Combine(legacyData, "factvault.db"),
            projectTitle: "Original Legacy Project");

        var first = InstalledDataMigration.Run(appDataRoot, [legacyRoot]);
        Assert.True(first.Completed);

        using (var source = Open(Path.Combine(legacyData, "factvault.db")))
        {
            using var command = source.CreateCommand();
            command.CommandText = "UPDATE projects SET title='Changed Later'";
            command.ExecuteNonQuery();
        }

        var second = InstalledDataMigration.Run(appDataRoot, [legacyRoot]);
        Assert.True(second.Completed);
        Assert.False(second.DataCopied);

        using var installed = Open(Path.Combine(appDataRoot, "data", "factvault.db"));
        Assert.Equal("Original Legacy Project", ScalarText(installed, "SELECT title FROM projects LIMIT 1"));
    }

    private static void CreateDatabase(
        string path,
        string? projectTitle = null,
        string? historyProjectFolder = null,
        string? imagePath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open(path);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL
            );
            CREATE TABLE quiz_history (
                id INTEGER PRIMARY KEY,
                project_folder TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE quiz_questions (
                id INTEGER PRIMARY KEY,
                image_path TEXT NOT NULL DEFAULT ''
            );
            """;
        schema.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(projectTitle))
        {
            using var project = connection.CreateCommand();
            project.CommandText = "INSERT INTO projects(id, title) VALUES(1, $title)";
            project.Parameters.AddWithValue("$title", projectTitle);
            project.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(historyProjectFolder))
        {
            using var history = connection.CreateCommand();
            history.CommandText = "INSERT INTO quiz_history(id, project_folder) VALUES(1, $path)";
            history.Parameters.AddWithValue("$path", historyProjectFolder);
            history.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            using var image = connection.CreateCommand();
            image.CommandText = "INSERT INTO quiz_questions(id, image_path) VALUES(1, $path)";
            image.Parameters.AddWithValue("$path", imagePath);
            image.ExecuteNonQuery();
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

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
