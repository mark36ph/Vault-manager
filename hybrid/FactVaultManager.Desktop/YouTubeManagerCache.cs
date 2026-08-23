using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed class YouTubeManagerCacheSnapshot
{
    public string AccountKey { get; init; } = "";
    public DateTime RefreshedAtUtc { get; init; }
    public List<YouTubePlaylistItem> Playlists { get; init; } = [];
    public Dictionary<string, List<YouTubePlaylistVideo>> PlaylistVideos { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed class YouTubeManagerCacheStore
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public YouTubeManagerCacheStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        _path = Path.Combine(directory, "youtube-manager-cache.json");
    }

    public static string CreateAccountKey(string clientId, string refreshToken)
    {
        var identity = (clientId ?? "").Trim() + "\n" + (refreshToken ?? "").Trim();
        if (identity.Trim().Length == 0)
            return "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public YouTubeManagerCacheSnapshot? Load(string accountKey)
    {
        if (accountKey.Length == 0 || !File.Exists(_path))
            return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<YouTubeManagerCacheSnapshot>(
                File.ReadAllText(_path),
                JsonOptions);
            if (snapshot is null ||
                !string.Equals(snapshot.AccountKey, accountKey, StringComparison.Ordinal) ||
                snapshot.RefreshedAtUtc == default)
                return null;
            return snapshot;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsFresh(YouTubeManagerCacheSnapshot snapshot, DateTime utcNow)
    {
        var age = utcNow - snapshot.RefreshedAtUtc;
        return age >= TimeSpan.Zero && age <= FreshFor;
    }

    public void Save(YouTubeManagerCacheSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
