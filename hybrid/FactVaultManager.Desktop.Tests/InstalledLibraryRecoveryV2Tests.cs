using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledLibraryRecoveryV2Tests
{
    [Fact]
    public void RecoversWhenBuild55MarkerExistsButInstalledLibraryIsStillEmpty()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        CreateDatabase(destination, journalRows: 1);
        File.WriteAllText(
            Path.Combine(appDataRoot, "installed-database-recovery-v1.json"),
            "{\"version\":1,\"source_database\":\"wrong.db\"}");

        var source = Path.Combine(sandbox.Path, "legacy", "anything.sqlite");
        CreateDatabase(source, projects: 3, questions: 25, history: 8, journalRows: 4);

        var result = InstalledLibraryRecoveryV2.Run(appDataRoot, [source]);

        Assert.True(result.Completed);
        Assert.True(result.DatabaseRecovered);
        Assert.Equal(36, result.SourceLibraryRows);
        Assert.Equal(36, result.DestinationLibraryRows);
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-library-recovery-v2.json")));
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-library-recovery-v2-diagnostics.json")));

        using var installed = Open(destination);
        Assert.Equal(3, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(25, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(8, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
        Assert.Equal(4, ScalarLong(installed, "SELECT COUNT(*) FROM social_upload_journal"));
    }

    [Fact]
    public void ChoosesDatabaseWithTheRichestActualLibraryContent()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        CreateDatabase(destination);

        var journalOnly = Path.Combine(sandbox.Path, "legacy-a", "factvault.db");
        CreateDatabase(journalOnly, journalRows: 100);
        var questionsOnly = Path.Combine(sandbox.Path, "legacy-b", "factvault.db");
        CreateDatabase(questionsOnly, questions: 50);
        var complete = Path.Combine(sandbox.Path, "legacy-c", "library.db");
        CreateDatabase(complete, projects: 2, questions: 12, history: 5, journalRows: 2);

        var result = InstalledLibraryRecoveryV2.Run(
            appDataRoot,
            [journalOnly, questionsOnly, complete]);

        Assert.True(result.DatabaseRecovered);
        Assert.Equal(Path.GetFullPath(complete), Path.GetFullPath(result.SourceDatabase));
        using var installed = Open(destination);
        Assert.Equal(2, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(12, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(5, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
    }

    [Fact]
    public void NeverReplacesAnInstalledDatabaseThatAlreadyHasLibraryRows()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        CreateDatabase(destination, projects: 1);

        var source = Path.Combine(sandbox.Path, "legacy", "factvault.db");
        CreateDatabase(source, projects: 5, questions: 100, history: 20);

        var result = InstalledLibraryRecoveryV2.Run(appDataRoot, [source]);

        Assert.True(result.Completed);
        Assert.False(result.DatabaseRecovered);
        using var installed = Open(destination);
        Assert.Equal(1, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(0, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.False(File.Exists(Path.Combine(appDataRoot, "installed-library-recovery-v2.json")));
    }

    private static void CreateDatabase(
        string path,
        int projects = 0,
        int questions = 0,
        int history = 0,
        int journalRows = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open(path);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE projects (id INTEGER PRIMARY KEY, title TEXT NOT NULL DEFAULT '', folder TEXT NOT NULL DEFAULT '');
            CREATE TABLE quiz_questions (id INTEGER PRIMARY KEY, question TEXT NOT NULL DEFAULT '', image_path TEXT NOT NULL DEFAULT '');
            CREATE TABLE quiz_history (id INTEGER PRIMARY KEY, title TEXT NOT NULL DEFAULT '', project_folder TEXT NOT NULL DEFAULT '');
            CREATE TABLE quiz_history_questions (
                history_id INTEGER NOT NULL,
                position INTEGER NOT NULL,
                question_id INTEGER NOT NULL,
                question TEXT NOT NULL DEFAULT '',
                category TEXT NOT NULL DEFAULT '',
                difficulty TEXT NOT NULL DEFAULT '',
                PRIMARY KEY(history_id, position));
            CREATE TABLE social_upload_journal (
                history_id INTEGER NOT NULL,
                platform TEXT NOT NULL,
                PRIMARY KEY(history_id, platform));
            """;
        schema.ExecuteNonQuery();

        for (var i = 1; i <= projects; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO projects(id, title, folder) VALUES($id, $title, '')";
            command.Parameters.AddWithValue("$id", i);
            command.Parameters.AddWithValue("$title", $"Project {i}");
            command.ExecuteNonQuery();
        }

        for (var i = 1; i <= questions; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO quiz_questions(id, question, image_path) VALUES($id, $question, '')";
            command.Parameters.AddWithValue("$id", i);
            command.Parameters.AddWithValue("$question", $"Question {i}");
            command.ExecuteNonQuery();
        }

        for (var i = 1; i <= history; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO quiz_history(id, title, project_folder) VALUES($id, $title, '')";
            command.Parameters.AddWithValue("$id", i);
            command.Parameters.AddWithValue("$title", $"Quiz {i}");
            command.ExecuteNonQuery();

            using var question = connection.CreateCommand();
            question.CommandText = "INSERT INTO quiz_history_questions(history_id, position, question_id, question) VALUES($id, 1, $id, 'Question')";
            question.Parameters.AddWithValue("$id", i);
            question.ExecuteNonQuery();
        }

        for (var i = 1; i <= journalRows; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO social_upload_journal(history_id, platform) VALUES($id, $platform)";
            command.Parameters.AddWithValue("$id", i);
            command.Parameters.AddWithValue("$platform", $"YouTube-{i}");
            command.ExecuteNonQuery();
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FactVaultManager.LibraryRecoveryV2.Tests",
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
