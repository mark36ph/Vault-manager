namespace FactVaultManager.Desktop;

public sealed record ProjectsFolderAvailability(
    bool Ready,
    string Path,
    string Message);

public static class ProjectsFolderConfigurationGuard
{
    public const string MissingProjectsFolderError = "Set the Projects Folder in Settings first.";

    public static bool IsMissingProjectsFolderException(Exception? error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException &&
                string.Equals(current.Message, MissingProjectsFolderError, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static ProjectsFolderAvailability Check(string? configuredPath)
    {
        var value = (configuredPath ?? "").Trim();
        if (value.Length == 0)
        {
            return new ProjectsFolderAvailability(
                false,
                "",
                "Set the Projects Folder in Settings before using Autopilot.");
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ProjectsFolderAvailability(
                false,
                value,
                "The saved Projects Folder path is invalid. Choose the folder again in Settings.");
        }

        if (!Directory.Exists(fullPath))
        {
            return new ProjectsFolderAvailability(
                false,
                fullPath,
                $"The Projects Folder is unavailable: {fullPath}");
        }

        return new ProjectsFolderAvailability(true, fullPath, "");
    }
}
