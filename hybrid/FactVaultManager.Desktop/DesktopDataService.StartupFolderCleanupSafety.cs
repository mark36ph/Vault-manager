using System.Diagnostics;

namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    /// <summary>
    /// Runs optional startup folder maintenance without allowing a missing, moved, or temporarily
    /// unavailable Projects Folder to terminate the desktop shell during its Loaded event.
    /// Operations that genuinely require a Projects Folder still use the strict GetProjectsRoot()
    /// guard and continue to surface a clear Settings error to the user.
    /// </summary>
    public void ResumeQuizFolderCleanupSafely()
    {
        try
        {
            ResumeQuizFolderCleanup();
        }
        catch (Exception error)
        {
            Debug.WriteLine("Startup quiz folder cleanup skipped: " + error);
        }
    }
}
