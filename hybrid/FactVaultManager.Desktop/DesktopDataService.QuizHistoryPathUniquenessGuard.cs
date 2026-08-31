namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public void EnsureQuizHistoryProjectFolderUniquenessGuard()
    {
        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER IF NOT EXISTS trg_quiz_history_unique_project_folder_update
            BEFORE UPDATE OF project_folder ON quiz_history
            WHEN trim(NEW.project_folder) <> ''
             AND EXISTS (
                SELECT 1
                FROM quiz_history other
                WHERE other.id <> NEW.id
                  AND lower(rtrim(other.project_folder, '\/')) = lower(rtrim(NEW.project_folder, '\/'))
             )
            BEGIN
                SELECT RAISE(ABORT, 'That project folder is already linked to another Quiz History row. Duplicate paths are not allowed.');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
