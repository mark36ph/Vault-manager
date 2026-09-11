using System.Text.Json;

namespace FactVaultManager.Desktop;

/// <summary>
/// Single runtime source for application version and build metadata.
/// The values themselves live in the repository root version.json.
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<Manifest> Current = new(LoadManifest);

    public static string Version => Current.Value.Version;
    public static int BuildNumber => Current.Value.Build;

    private static Manifest LoadManifest()
    {
        var path = FindVersionManifest();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (!root.TryGetProperty("latest_version", out var versionProperty))
            throw new InvalidOperationException($"version.json is missing 'latest_version': {path}");

        if (!root.TryGetProperty("build", out var buildProperty) || !buildProperty.TryGetInt32(out var build))
            throw new InvalidOperationException($"version.json is missing a valid 'build': {path}");

        var version = versionProperty.GetString();
        if (string.IsNullOrWhiteSpace(version) || !System.Version.TryParse(version, out _))
            throw new InvalidOperationException($"version.json contains an invalid 'latest_version': {path}");

        if (build < 1)
            throw new InvalidOperationException($"version.json contains an invalid 'build': {path}");

        return new Manifest(version, build);
    }

    private static string FindVersionManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "version.json");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Factburst Quiz Manager could not find version.json. The version manifest must be included with the application.");
    }

    private sealed record Manifest(string Version, int Build);
}
