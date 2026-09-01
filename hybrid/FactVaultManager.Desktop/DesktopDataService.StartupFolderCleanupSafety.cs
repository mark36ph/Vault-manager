using System.Diagnostics;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    private const string ProjectsFolderRequiredMessage = "Set the Projects Folder in Settings first.";

    /// <summary>
    /// Runs optional startup folder maintenance without allowing an unconfigured Projects Folder
    /// to terminate the desktop shell during its Loaded event. Operations that genuinely require
    /// a Projects Folder still use the strict GetProjectsRoot() guard.
    /// </summary>
    public void ResumeQuizFolderCleanupSafely()
    {
        try
        {
            ResumeQuizFolderCleanup();
        }
        catch (InvalidOperationException error) when (
            string.Equals(error.Message, ProjectsFolderRequiredMessage, StringComparison.Ordinal))
        {
            Debug.WriteLine("Startup quiz folder cleanup skipped: Projects Folder is not configured yet.");
        }
    }
}
