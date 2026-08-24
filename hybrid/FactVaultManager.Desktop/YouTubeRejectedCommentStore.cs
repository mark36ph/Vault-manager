using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeRejectedCommentEntry(
    string AccountKey,
    YouTubeCommentItem Comment,
    DateTime RejectedAtUtc);

public sealed class YouTubeRejectedCommentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public YouTubeRejectedCommentStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        _path = Path.Combine(directory, "youtube-rejected-comments.json");
    }

    public IReadOnlyList<YouTubeCommentItem> List(string accountKey) =>
        LoadEntries()
            .Where(entry => string.Equals(entry.AccountKey, accountKey, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.RejectedAtUtc)
            .Select(entry => entry.Comment with { ModerationStatus = "rejected" })
            .ToList();

    public void Save(string accountKey, YouTubeCommentItem comment, DateTime rejectedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
            throw new ArgumentException("The YouTube account is missing.", nameof(accountKey));
        if (string.IsNullOrWhiteSpace(comment.Id))
            throw new ArgumentException("The YouTube comment ID is missing.", nameof(comment));

        var entries = LoadEntries()
            .Where(entry => !(string.Equals(entry.AccountKey, accountKey, StringComparison.Ordinal) &&
                              string.Equals(entry.Comment.Id, comment.Id, StringComparison.Ordinal)))
            .ToList();
        entries.Add(new YouTubeRejectedCommentEntry(
            accountKey,
            comment with { ModerationStatus = "rejected" },
            rejectedAtUtc.ToUniversalTime()));
        WriteEntries(entries);
    }

    public void Remove(string accountKey, string commentId)
    {
        var entries = LoadEntries();
        var remaining = entries
            .Where(entry => !(string.Equals(entry.AccountKey, accountKey, StringComparison.Ordinal) &&
                              string.Equals(entry.Comment.Id, commentId, StringComparison.Ordinal)))
            .ToList();
        if (remaining.Count != entries.Count)
            WriteEntries(remaining);
    }

    private List<YouTubeRejectedCommentEntry> LoadEntries()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<YouTubeRejectedCommentEntry>>(
                File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    private void WriteEntries(IReadOnlyList<YouTubeRejectedCommentEntry> entries)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
