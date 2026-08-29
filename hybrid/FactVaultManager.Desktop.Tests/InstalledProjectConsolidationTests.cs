using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledProjectConsolidationTests
{
    [Fact]
    public void ConsolidatesLegacyProjectsAndRebasesStoredPaths()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var dataRoot = Path.Combine(appDataRoot, "data");
        var destinationProjects = Path.Combine(appDataRoot, "projects");
        Directory.CreateDirectory(dataRoot);

        var legacyRoot = Path.Combine(sandbox.Path, "Vault-manager");
        var legacyProjects = Path.Combine(legacyRoot, "quiz-projects");
        var legacyProject = Path.Combine(legacyProjects, "Completed", "Space Quiz 001");
        Directory.CreateDirectory(legacyProject);
        File.WriteAllBytes(Path.Combine(legacyProject, "final.mp4"), [1, 2, 3, 4, 5]);
        File.WriteAllText(
            Path.Combine(legacyProject, "project.json"),
            $$"""{ "project_folder": "{{JsonEscape(legacyProject)}}" }""");

        File.WriteAllText(
            Path.Combine(dataRoot, "settings.json"),
            $$"""
            {
              "general": {
                "projects_folder": "{{JsonEscape(legacyProjects)}}",
                "theme": "dark"
              }
            }
            """);
        CreateDatabase(
            Path.Combine(dataRoot, "factvault.db"),
            historyProjectFolder: legacyProject,
            projectFolder: legacyProject);

        File.WriteAllText(
            Path.Combine(appDataRoot, "installed-data-migration-v2.json"),
            $$"""
            {
              "version": 2,
              "source_projects": "{{JsonEscape(legacyProjects)}}",
              "destination_projects": "{{JsonEscape(destinationProjects)}}"
            }
            """);

        var result = InstalledProjectConsolidation.Run(appDataRoot);

        Assert.True(result.Completed);
        Assert.True(result.FilesCopied >= 2);
        Assert.Equal(Path.GetFullPath(legacyProjects), Path.GetFullPath(result.SourceProjectsDirectory));
        Assert.Equal(Path.GetFullPath(destinationProjects), Path.GetFullPath(result.DestinationProjectsDirectory));

        var installedProject = Path.Combine(destinationProjects, "Completed", "Space Quiz 001");
        Assert.True(File.Exists(Path.Combine(installedProject, "final.mp4")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(Path.Combine(installedProject, "final.mp4")));

        var settings = ReadObject(Path.Combine(dataRoot, "settings.json"));
        Assert.Equal(
            Path.GetFullPath(destinationProjects),
            Path.GetFullPath(settings["general"]!["projects_folder"]!.GetValue<string>()));
        Assert.Equal("dark", settings["general"]!["theme"]!.GetValue<string>());

        var projectJson = File.ReadAllText(Path.Combine(installedProject, "project.json"));
        Assert.DoesNotContain(legacyProjects, projectJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(destinationProjects.Replace("\\", "\\\\", StringComparison.Ordinal), projectJson, StringComparison.OrdinalIgnoreCase);

        using var connection = Open(Path.Combine(dataRoot, "factvault.db"));
        Assert.Equal(
            Path.GetFullPath(installedProject),
            Path.GetFullPath(ScalarText(connection, "SELECT project_folder FROM quiz_history LIMIT 1")));
        Assert.Equal(
            Path.Combine("Completed", "Space Quiz 001"),
            ScalarText(connection, "SELECT folder FROM projects LIMIT 1"));

        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-project-consolidation-v1.json")));
        Assert.True(File.Exists(Path.Combine(legacyProject, "final.mp4")));
    }

    [Fact]
    public void KeepsNewerInstalledConflictWhileCopyingMissingLegacyFiles()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var dataRoot = Path.Combine(appDataRoot, "data");
        var destinationProjects = Path.Combine(appDataRoot, "projects");
        var legacyProjects = Path.Combine(sandbox.Path, "Vault-manager", "quiz-projects");
        var legacyProject = Path.Combine(legacyProjects, "Completed", "Quiz 001");
        var installedProject = Path.Combine(destinationProjects, "Completed", "Quiz 001");
        Directory.CreateDirectory(legacyProject);
        Directory.CreateDirectory(installedProject);
        Directory.CreateDirectory(dataRoot);

        var legacyConflict = Path.Combine(legacyProject, "project.json");
        var installedConflict = Path.Combine(installedProject, "project.json");
        File.WriteAllText(legacyConflict, "legacy");
        File.WriteAllText(installedConflict, "installed-newer-content");
        File.SetLastWriteTimeUtc(legacyConflict, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(installedConflict, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(legacyProject, "missing.txt"), "copy me");

        File.WriteAllText(
            Path.Combine(dataRoot, "settings.json"),
            $$"""{ "general": { "projects_folder": "{{JsonEscape(legacyProjects)}}" } }""");
        CreateDatabase(Path.Combine(dataRoot, "factvault.db"));

        var result = InstalledProjectConsolidation.Run(appDataRoot);

        Assert.True(result.Completed);
        Assert.Equal("installed-newer-content", File.ReadAllText(installedConflict));
        Assert.Equal("copy me", File.ReadAllText(Path.Combine(installedProject, "missing.txt")));
    }

    [Fact]
    public void CompletionMarkerMakesConsolidationOneTimeOnly()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var dataRoot = Path.Combine(appDataRoot, "data");
        var legacyProjects = Path.Combine(sandbox.Path, "Vault-manager", "quiz-projects");
        Directory.CreateDirectory(legacyProjects);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(legacyProjects, "first.txt"), "first");
        File.WriteAllText(
            Path.Combine(dataRoot, "settings.json"),
            $$"""{ "general": { "projects_folder": "{{JsonEscape(legacyProjects)}}" } }""");
        CreateDatabase(Path.Combine(dataRoot, "factvault.db"));

        var first = InstalledProjectConsolidation.Run(appDataRoot);
        Assert.True(first.Completed);

        File.WriteAllText(Path.Combine(legacyProjects, "added-later.txt"), "later");
        var second = InstalledProjectConsolidation.Run(appDataRoot);

        Assert.True(second.Completed);
        Assert.Equal(0, second.FilesCopied);
        Assert.False(File.Exists(Path.Combine(appDataRoot, "projects", "added-later.txt")));
    }

    private static void CreateDatabase(
        string path,
        string? historyProjectFolder = null,
        string? projectFolder = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open(path);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY,
                folder TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE quiz_history (
                id INTEGER PRIMARY KEY,
                project_folder TEXT NOT NULL DEFAULT ''
            );
            """;
        schema.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(projectFolder))
        {
            using var project = connection.CreateCommand();
            project.CommandText = "INSERT INTO projects(id, folder) VALUES(1, $path)";
            project.Parameters.AddWithValue("$path", projectFolder);
            project.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(historyProjectFolder))
        {
            using var history = connection.CreateCommand();
            history.CommandText = "INSERT INTO quiz_history(id, project_folder) VALUES(1, $path)";
            history.Parameters.AddWithValue("$path", historyProjectFolder);
            history.ExecuteNonQuery();
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidOperationException("Expected a JSON object.");

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.ProjectConsolidation.Tests",
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
