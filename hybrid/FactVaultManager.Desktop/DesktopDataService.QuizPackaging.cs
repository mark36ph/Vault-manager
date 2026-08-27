namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public void UpdateQuizHistoryYouTubeTitle(int historyId, string title)
    {
        if (historyId <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyId));

        var normalized = (title ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("YouTube title is required.", nameof(title));
        if (normalized.Length > QuizPublishMetadataGenerator.MaxTitleLength)
            throw new ArgumentException(
                $"YouTube title must be {QuizPublishMetadataGenerator.MaxTitleLength} characters or fewer.",
                nameof(title));

        EnsureQuizHistorySchema();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE quiz_history SET youtube_title=$title WHERE id=$id";
        command.Parameters.AddWithValue("$title", normalized);
        command.Parameters.AddWithValue("$id", historyId);
        command.ExecuteNonQuery();
    }
}
