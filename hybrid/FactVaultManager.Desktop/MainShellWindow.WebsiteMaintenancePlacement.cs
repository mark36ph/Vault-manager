using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    public void InitializeWebsiteMaintenancePlacement()
    {
        // The legacy Website Users enhancer used this field as a signal to inject
        // a Maintenance button into the Users header. Keep a non-visual placeholder
        // so that control is never added there; the real controls live in Settings → Website.
        _websiteMaintenanceButton ??= new Button();
        InitializeWebsiteSettingsAdministrationPage();
    }
}
