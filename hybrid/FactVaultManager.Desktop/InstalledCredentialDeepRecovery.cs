using System.Diagnostics;

namespace FactVaultManager.Desktop;

/// <summary>
/// Finds legacy settings files that live deeper inside the installed application's
/// LocalAppData tree (for example, older Velopack version directories) and feeds them
/// through the normal credential recovery path.
/// </summary>
internal static class InstalledCredentialDeepRecovery
{
    public static void Run()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactVaultManager");
        try
        {
            var destination = Path.Combine(appDataRoot, "data", "settings.json");
            if (!Directory.Exists(appDataRoot))
                return;

            var candidates = new List<string>();
            foreach (var path in EnumerateSettingsFiles(appDataRoot))
            {
                if (!string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(destination),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    candidates.Add(path);
                }
            }

            if (candidates.Count > 0)
                _ = InstalledCredentialRecovery.Run(appDataRoot, candidates);
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            Debug.WriteLine($"Deep installed credential recovery could not complete: {error}");
        }
    }

    private static IEnumerable<string> EnumerateSettingsFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "settings.json", SearchOption.AllDirectories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Could not enumerate installed settings files: {error.Message}");
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }
}
