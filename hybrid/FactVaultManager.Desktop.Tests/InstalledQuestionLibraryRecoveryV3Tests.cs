using Microsoft.Data.Sqlite;

namespace FactVaultManager.Desktop.Tests;

public sealed class InstalledQuestionLibraryRecoveryV3Tests
{
    [Fact]
    public void RecoversQuestionsAndHistoryWithoutReplacingInstalledProjects()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        CreateDatabase(destination, projects: 10);

        var source = Path.Combine(sandbox.Path, "legacy", "factvault.db");
        CreateDatabase(source, projects: 3, questions: 25, history: 8, journalRows: 4);

        var result = InstalledQuestionLibraryRecoveryV3.Run(appDataRoot, [source]);

        Assert.True(result.Completed);
        Assert.True(result.DatabaseRecovered);
        Assert.Equal(25, result.SourceQuestions);
        Assert.Equal(25, result.DestinationQuestions);

        using var installed = Open(destination);
        Assert.Equal(10, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(25, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(8, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
        Assert.Equal(8, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history_questions"));
        Assert.Equal(4, ScalarLong(installed, "SELECT COUNT(*) FROM social_upload_journal"));

        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-question-library-recovery-v3.json")));
        Assert.True(File.Exists(Path.Combine(appDataRoot, "installed-question-library-recovery-v3-diagnostics.json")));
        Assert.True(Directory.Exists(Path.Combine(appDataRoot, "question-library-recovery-v3-backup")));
    }

    [Fact]
    public void ExistingQuestionLibraryIsNeverOverwritten()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        CreateDatabase(destination, projects: 10, questions: 2);

        var source = Path.Combine(sandbox.Path, "legacy", "factvault.db");
        CreateDatabase(source, projects: 3, questions: 25, history: 8);

        var result = InstalledQuestionLibraryRecoveryV3.Run(appDataRoot, [source]);

        Assert.True(result.Completed);
        Assert.False(result.DatabaseRecovered);
        using var installed = Open(destination);
        Assert.Equal(10, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(2, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(0, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
    }

    [Fact]
    public void ChoosesSourceByQuestionLibraryRatherThanProjectCount()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        CreateDatabase(destination, projects: 10);

        var projectHeavy = Path.Combine(sandbox.Path, "legacy-a", "factvault.db");
        CreateDatabase(projectHeavy, projects: 100, questions: 5, history: 1);
        var questionHeavy = Path.Combine(sandbox.Path, "legacy-b", "factvault.db");
        CreateDatabase(questionHeavy, projects: 1, questions: 40, history: 6);

        var result = InstalledQuestionLibraryRecoveryV3.Run(appDataRoot, [projectHeavy, questionHeavy]);

        Assert.True(result.DatabaseRecovered);
        Assert.Equal(Path.GetFullPath(questionHeavy), Path.GetFullPath(result.SourceDatabase));
        using var installed = Open(destination);
        Assert.Equal(10, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(40, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(6, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
    }

    [Fact]
    public void PreservesExistingHistoryWhenOnlyQuestionLibraryIsMissing()
    {
        using var sandbox = new TemporaryDirectory();
        var appDataRoot = Path.Combine(sandbox.Path, "installed");
        var destination = Path.Combine(appDataRoot, "data", "factvault.db");
        CreateDatabase(destination, projects: 10, history: 2, journalRows: 1);

        var source = Path.Combine(sandbox.Path, "legacy", "factvault.db");
        CreateDatabase(source, questions: 30, history: 8, journalRows: 4);

        var result = InstalledQuestionLibraryRecoveryV3.Run(appDataRoot, [source]);

        Assert.True(result.DatabaseRecovered);
        using var installed = Open(destination);
        Assert.Equal(10, ScalarLong(installed, "SELECT COUNT(*) FROM projects"));
        Assert.Equal(30, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_questions"));
        Assert.Equal(2, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history"));
        Assert.Equal(2, ScalarLong(installed, "SELECT COUNT(*) FROM quiz_history_questions"));
        Assert.Equal(1, ScalarLong(installed, "SELECT COUNT(*) FROM social_upload_journal"));
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
            CREATE TABLE projects (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                folder TEXT NOT NULL DEFAULT '');
            CREATE TABLE quiz_questions (
                id INTEGER PRIMARY KEY,
                question TEXT NOT NULL DEFAULT '',
                option_a TEXT NOT NULL DEFAULT 'A',
                option_b TEXT NOT NULL DEFAULT 'B',
                option_c TEXT NOT NULL DEFAULT 'C',
                option_d TEXT NOT NULL DEFAULT 'D',
                correct_index INTEGER NOT NULL DEFAULT 0,
                explanation TEXT NOT NULL DEFAULT '',
                category TEXT NOT NULL DEFAULT 'General Knowledge',
                difficulty TEXT NOT NULL DEFAULT 'medium',
                source TEXT NOT NULL DEFAULT 'Imported',
                fingerprint TEXT NOT NULL DEFAULT '',
                created TEXT NOT NULL DEFAULT '',
                times_used INTEGER NOT NULL DEFAULT 0,
                last_used TEXT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                image_path TEXT NOT NULL DEFAULT '');
            CREATE TABLE quiz_history (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                project_folder TEXT NOT NULL DEFAULT '');
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
            command.Parameters.AddWithValue("$title", $"Installed Project {i}");
            command.ExecuteNonQuery();
        }

        for (var i = 1; i <= questions; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO quiz_questions(
                    id, question, option_a, option_b, option_c, option_d,
                    correct_index, fingerprint, created)
                VALUES($id, $question, 'A', 'B', 'C', 'D', 0, $fingerprint, '2026-01-01')
                """;
            command.Parameters.AddWithValue("$id", i);
            command.Parameters.AddWithValue("$question", $"Question {i}");
            command.Parameters.AddWithValue("$fingerprint", $"fp-{i}");
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
            question.CommandText = """
                INSERT INTO quiz_history_questions(history_id, position, question_id, question)
                VALUES($id, 1, $id, 'Question')
                """;
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
                "FactVaultManager.QuestionLibraryRecoveryV3.Tests",
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
