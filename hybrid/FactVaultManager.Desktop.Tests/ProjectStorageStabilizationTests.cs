using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class ProjectStorageStabilizationTests
{
    [Fact]
    public void CreateProject_UsesStableIdFolder_AndStatusChangesDoNotMoveIt()
    {
        using var fixture = new StorageFixture();

        var created = fixture.Service.CreateProject("Space Quiz", "Space", "In Progress");
        var originalFolder = fixture.Service.ResolveProjectFolder(created);
        var marker = Path.Combine(originalFolder, "keep-me.txt");
        File.WriteAllText(marker, "stable");

        Assert.Equal("000001-Space Quiz", created.Folder);
        Assert.Equal(Path.Combine(fixture.ProjectsRoot, "000001-Space Quiz"), originalFolder);

        var scheduled = fixture.Service.ChangeStatus(created, "Scheduled");
        var published = fixture.Service.ChangeStatus(scheduled, "Published");

        Assert.Equal("Published", published.Status);
        Assert.Equal(created.Folder, published.Folder);
        Assert.Equal(originalFolder, fixture.Service.ResolveProjectFolder(published));
        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(Path.Combine(fixture.ProjectsRoot, "Scheduled", "Space Quiz")));
        Assert.False(Directory.Exists(Path.Combine(fixture.ProjectsRoot, "Published", "Space Quiz")));
    }

    [Fact]
    public void ChangeStatus_PreservesExistingLegacyStatusFolder()
    {
        using var fixture = new StorageFixture();
        var legacyRelative = Path.Combine("In Progress", "Legacy Quiz");
        var legacyFolder = Path.Combine(fixture.ProjectsRoot, legacyRelative);
        Directory.CreateDirectory(legacyFolder);
        var marker = Path.Combine(legacyFolder, "legacy-project.txt");
        File.WriteAllText(marker, "do not move");
        fixture.InsertProject(42, "Legacy Quiz", "History", "In Progress", legacyRelative);

        var project = fixture.Service.GetProjects().Single(item => item.Id == 42);
        var changed = fixture.Service.ChangeStatus(project, "Published");

        Assert.Equal("Published", changed.Status);
        Assert.Equal(legacyRelative, changed.Folder);
        Assert.Equal(legacyFolder, fixture.Service.ResolveProjectFolder(changed));
        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(Path.Combine(fixture.ProjectsRoot, "Published", "Legacy Quiz")));
    }

    private sealed class StorageFixture : IDisposable
    {
        private readonly string _root;

        public StorageFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "FactVaultManager-ProjectStorage-" + Guid.NewGuid().ToString("N"));
            ProjectsRoot = Path.Combine(_root, "projects");
            var dataRoot = Path.Combine(_root, "appdata");
            var dataDirectory = Path.Combine(dataRoot, "data");
            Directory.CreateDirectory(ProjectsRoot);
            Directory.CreateDirectory(dataDirectory);

            var settings = new JsonObject
            {
                ["general"] = new JsonObject
                {
                    ["projects_folder"] = ProjectsRoot,
                },
            };
            File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), settings.ToJsonString());

            var databasePath = Path.Combine(dataDirectory, "factvault.db");
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE projects (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        title TEXT NOT NULL,
                        category TEXT NOT NULL,
                        status TEXT NOT NULL,
                        folder TEXT NOT NULL DEFAULT '',
                        created TEXT NOT NULL DEFAULT '',
                        script TEXT NOT NULL DEFAULT '',
                        on_screen_text TEXT NOT NULL DEFAULT '',
                        visual_plan TEXT NOT NULL DEFAULT '',
                        description TEXT NOT NULL DEFAULT '',
                        pinned_comment TEXT NOT NULL DEFAULT '',
                        notes TEXT NOT NULL DEFAULT '',
                        tags TEXT NOT NULL DEFAULT '',
                        sources TEXT NOT NULL DEFAULT '',
                        pinned INTEGER NOT NULL DEFAULT 0,
                        updated TEXT NOT NULL DEFAULT ''
                    );
                    """;
                command.ExecuteNonQuery();
            }

            Service = new DesktopDataService(_root, dataRoot);
        }

        public string ProjectsRoot { get; }
        public DesktopDataService Service { get; }

        public void InsertProject(int id, string title, string category, string status, string folder)
        {
            using var connection = new SqliteConnection($"Data Source={Service.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO projects(id, title, category, status, folder, created, updated)
                VALUES($id, $title, $category, $status, $folder, '2026-08-31 12:00', '2026-08-31 12:00')
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$category", category);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$folder", folder);
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
