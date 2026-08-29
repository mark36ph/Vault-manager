using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledDatabaseRecoveryTests
{
    [Fact]
    public void RecoversEmptyInstalledDatabaseAndRebasesLibraryAndYouTubeHistoryPaths()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationData = Path.Combine(appDataRoot, "data");
        var destinationProjects = Path.Combine(appDataRoot, "projects");
        var destinationDatabase = Path.Combine(destinationData, "factvault.db");
        Directory.CreateDirectory(destinationData);
        Directory.CreateDirectory(destinationProjects);
        CreateDatabase(destinationDatabase);

        var legacyRoot = Path.Combine(sandbox.Path, "Vault-manager");
        var sourceData = Path.Combine(legacyRoot, "data");
        var sourceProjects = Path.Combine(legacyRoot, "quiz-projects");
        var sourceProject = Path.Combine(sourceProjects, "Completed", "Space Quiz 001");
        var destinationProject = Path.Combine(destinationProjects, "Completed", "Space Quiz 001");
        Directory.CreateDirectory(sourceProject);
        Directory.CreateDirectory(destinationProject);
        File.WriteAllText(Path.Combine(sourceProject, "project.json"), "legacy project");
        File.WriteAllText(Path.Combine(destinationProject, "project.json"), "installed project");

        var sourceImage = Path.Combine(sourceData, "quiz", "question-images", "logo.png");
        var destinationImage = Path.Combine(destinationData, "quiz", "question-images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceImage)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationImage)!);
        File.WriteAllBytes(sourceImage, [1, 2, 3]);
        File.WriteAllBytes(destinationImage, [1, 2, 3]);

        var sourceDatabase = Path.Combine(sourceData, "factvault.db");
        CreateDatabase(
            sourceDatabase,
            projectFolder: sourceProject,
            historyProjectFolder: sourceProject,
            imagePath: sourceImage,
            withQuestionLibraryRow: true,
            withUploadJournalRow: true);
        File.WriteAllText(Path.Combine(sourceData, "youtube-manager-cache.json"), "{\"cache\":true}");

        File.WriteAllText(
            Path.Combine(appDataRoot, "installed-data-migration-v2.json"),
            $$"""
            {
              "source_data": "{{JsonEscape(sourceData)}}",
              "source_projects": "{{JsonEscape(sourceProjects)}}",
              "destination_projects": "{{JsonEscape(destinationProjects)}}"
            }
            """);

        var result = InstalledDatabaseRecovery.Run(appDataRoot, [sourceDatabase]);

        Assert.True(result.Completed);
        Assert.True(result.DatabaseRecovered);
        Assert.True(result.SourcePrimaryRows >= 4);
        Assert.True(result.DestinationPrimaryRows >= result.SourcePrimaryRows);
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-database-recovery-v1.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(appDataRoot, "database-recovery-backup"), "*.db"));
        Assert.True(File.Exists(Path.Combine(destinationData, "youtube-manager-cache.json")));

        using var installed = Open(destinationDatabase);
        Assert.Equal("Legacy Project", ScalarText(installed, "SELECT title FROM projects LIMIT 1"));
        Assert.Equal(
            Path.Combine("Completed", "Space Quiz 001"),
            ScalarText(installed, "SELECT folder FROM projects LIMIT 1"));
        Assert.Equal(
            Path.GetFullPath(destinationProject),
            Path.GetFullPath(ScalarText(installed, "SELECT project_folder FROM quiz_history LIMIT 1")));
        Assert.Equal(
            Path.GetFullPath(destinationImage),
            Path.GetFullPath(ScalarText(installed, "SELECT image_path FROM quiz_questions LIMIT 1")));
        Assert.Equal(1L, ScalarLong(installed, "SELECT COUNT(*) FROM social_upload_journal"));
        Assert.Equal(1L, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history_questions"));

        using var legacy = Open(sourceDatabase);
        Assert.Equal(
            Path.GetFullPath(sourceProject),
            Path.GetFullPath(ScalarText(legacy, "SELECT project_folder FROM quiz_history LIMIT 1")));
        Assert.Equal(
            Path.GetFullPath(sourceImage),
            Path.GetFullPath(ScalarText(legacy, "SELECT image_path FROM quiz_questions LIMIT 1")));
    }

    [Fact]
    public void DoesNotReplaceInstalledDatabaseThatAlreadyContainsUserData()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationDatabase = Path.Combine(appDataRoot, "data", "factvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationDatabase)!);
        CreateDatabase(destinationDatabase, projectTitle: "Installed Project");

        var sourceDatabase = Path.Combine(sandbox.Path, "legacy", "data", "factvault.db");
        CreateDatabase(
            sourceDatabase,
            projectTitle: "Legacy Project",
            withQuestionLibraryRow: true,
            withUploadJournalRow: true);

        var result = InstalledDatabaseRecovery.Run(appDataRoot, [sourceDatabase]);

        Assert.False(result.Completed);
        Assert.False(result.DatabaseRecovered);
        using var installed = Open(destinationDatabase);
        Assert.Equal("Installed Project", ScalarText(installed, "SELECT title FROM projects LIMIT 1"));
        Assert.False(File.Exists(Path.Combine(appDataRoot, "installed-database-recovery-v1.json")));
    }

    [Fact]
    public void CompletionMarkerPreventsASecondRecoveryPass()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destinationDatabase = Path.Combine(appDataRoot, "data", "factvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationDatabase)!);
        CreateDatabase(destinationDatabase);

        var sourceDatabase = Path.Combine(sandbox.Path, "legacy", "data", "factvault.db");
        CreateDatabase(sourceDatabase, projectTitle: "Recovered Project");

        var first = InstalledDatabaseRecovery.Run(appDataRoot, [sourceDatabase]);
        Assert.True(first.DatabaseRecovered);

        using (var source = Open(sourceDatabase))
        {
            using var insert = source.CreateCommand();
            insert.CommandText = "INSERT INTO projects(id, title, folder) VALUES(2, 'Added Later', '')";
            insert.ExecuteNonQuery();
        }

        var second = InstalledDatabaseRecovery.Run(appDataRoot, [sourceDatabase]);
        Assert.True(second.Completed);
        Assert.False(second.DatabaseRecovered);
        using var installed = Open(destinationDatabase);
        Assert.Equal(1L, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
    }

    private static void CreateDatabase(
        string path,
        string? projectTitle = null,
        string? projectFolder = null,
        string? historyProjectFolder = null,
        string? imagePath = null,
        bool withQuestionLibraryRow = false,
        bool withUploadJournalRow = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open(path);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL,
                folder TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE quiz_questions (
                id INTEGER PRIMARY KEY,
                question TEXT NOT NULL DEFAULT '',
                image_path TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE quiz_history (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                project_folder TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE quiz_history_questions (
                history_id INTEGER NOT NULL,
                position INTEGER NOT NULL,
                question_id INTEGER NOT NULL,
                question TEXT NOT NULL DEFAULT '',
                category TEXT NOT NULL DEFAULT '',
                difficulty TEXT NOT NULL DEFAULT '',
                PRIMARY KEY(history_id, position)
            );
            CREATE TABLE social_upload_journal (
                history_id INTEGER NOT NULL,
                platform TEXT NOT NULL,
                PRIMARY KEY(history_id, platform)
            );
            """;
        schema.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(projectTitle) || !string.IsNullOrWhiteSpace(projectFolder))
        {
            using var project = connection.CreateCommand();
            project.CommandText = "INSERT INTO projects(id, title, folder) VALUES(1, $title, $folder)";
            project.Parameters.AddWithValue("$title", projectTitle ?? "Legacy Project");
            project.Parameters.AddWithValue("$folder", projectFolder ?? "");
            project.ExecuteNonQuery();
        }

        if (!string.IsNullOrWhiteSpace(historyProjectFolder))
        {
            using var history = connection.CreateCommand();
            history.CommandText = "INSERT INTO quiz_history(id, title, project_folder) VALUES(1, 'Space Quiz', $folder)";
            history.Parameters.AddWithValue("$folder", historyProjectFolder);
            history.ExecuteNonQuery();

            using var question = connection.CreateCommand();
            question.CommandText = "INSERT INTO quiz_history_questions(history_id, position, question_id, question) VALUES(1, 1, 1, 'Question')";
            question.ExecuteNonQuery();
        }

        if (withQuestionLibraryRow || !string.IsNullOrWhiteSpace(imagePath))
        {
            using var question = connection.CreateCommand();
            question.CommandText = "INSERT INTO quiz_questions(id, question, image_path) VALUES(1, 'Library Question', $imagePath)";
            question.Parameters.AddWithValue("$imagePath", imagePath ?? "");
            question.ExecuteNonQuery();
        }

        if (withUploadJournalRow)
        {
            using var journal = connection.CreateCommand();
            journal.CommandText = "INSERT INTO social_upload_journal(history_id, platform) VALUES(1, 'YouTube')";
            journal.ExecuteNonQuery();
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

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
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
                "FactVaultManager.DatabaseRecovery.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
