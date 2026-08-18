using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FactVaultManager.Desktop;

public static class QuizSharedAssetCache
{
    public const string AssetVersion = "factburst-neon-20260818-v1";
    private static readonly object Sync = new();

    public static string RootFolder
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
                local = Path.GetTempPath();
            return Path.Combine(local, "FactVaultManager", "QuizSharedAssets", AssetVersion);
        }
    }

    public static string BackgroundPath(int width, int height, double frameRate)
    {
        var fps = Math.Max(1.0, frameRate).ToString("0.###", CultureInfo.InvariantCulture);
        return Path.Combine(RootFolder, "Backgrounds", $"neon_starburst_{width}x{height}_{fps}fps.mp4");
    }

    public static string OpeningCountdownPath(
        int value,
        int width,
        int height,
        bool vertical,
        string? logoPath,
        string styleSignature)
    {
        if (value is < 1 or > 9)
            throw new ArgumentOutOfRangeException(nameof(value));

        var identity = string.Join("|",
            AssetVersion,
            width,
            height,
            vertical,
            FingerprintFile(logoPath),
            styleSignature ?? string.Empty);
        var key = ShortHash(identity);
        return Path.Combine(RootFolder, "OpeningCountdown", key, $"start_{value}.png");
    }

    public static string GetOrCreate(string cachePath, Action<string> generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(generator);
        cachePath = Path.GetFullPath(cachePath);

        lock (Sync)
        {
            if (IsValid(cachePath))
                return cachePath;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var extension = Path.GetExtension(cachePath);
            var temporary = Path.Combine(
                Path.GetDirectoryName(cachePath)!,
                $"{Path.GetFileNameWithoutExtension(cachePath)}.{Guid.NewGuid():N}{extension}");
            try
            {
                generator(temporary);
                if (!IsValid(temporary))
                    throw new InvalidOperationException($"Shared quiz asset generation did not create a valid file: {temporary}");
                File.Move(temporary, cachePath, overwrite: true);
                return cachePath;
            }
            finally
            {
                TryDelete(temporary);
            }
        }
    }

    public static void CopyToProject(string cachePath, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cachePath = Path.GetFullPath(cachePath);
        destination = Path.GetFullPath(destination);
        if (!IsValid(cachePath))
            throw new FileNotFoundException("Shared quiz asset cache file is missing or empty.", cachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(cachePath, destination, overwrite: true);
    }

    public static bool IsValid(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;

    public static string FingerprintFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "none";
        using var stream = File.OpenRead(Path.GetFullPath(path));
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()[..16];
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
