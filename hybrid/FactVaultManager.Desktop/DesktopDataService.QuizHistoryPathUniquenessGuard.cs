namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public void EnsureQuizHistoryProjectFolderUniquenessGuard()
    {
        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quiz_history_project_folder_groups (
                history_id INTEGER PRIMARY KEY,
                group_key TEXT NOT NULL,
                FOREIGN KEY(history_id) REFERENCES quiz_history(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_quiz_history_project_folder_groups_key
                ON quiz_history_project_folder_groups(group_key);

            DROP TRIGGER IF EXISTS trg_quiz_history_unique_project_folder_update;

            CREATE TRIGGER trg_quiz_history_unique_project_folder_update
            BEFORE UPDATE OF project_folder ON quiz_history
            WHEN trim(NEW.project_folder) <> ''
             AND EXISTS (
                SELECT 1
                FROM quiz_history other
                WHERE other.id <> NEW.id
                  AND lower(rtrim(other.project_folder, '\/')) = lower(rtrim(NEW.project_folder, '\/'))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM quiz_history_project_folder_groups current_group
                      JOIN quiz_history_project_folder_groups other_group
                        ON other_group.group_key = current_group.group_key
                      WHERE current_group.history_id = NEW.id
                        AND other_group.history_id = other.id
                  )
             )
            BEGIN
                SELECT RAISE(ABORT, 'That project folder is already linked to another unrelated Quiz History row. Duplicate paths are not allowed unless both rows are registered to the same physical project group.');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
