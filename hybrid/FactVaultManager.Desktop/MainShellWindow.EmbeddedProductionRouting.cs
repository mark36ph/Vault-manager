namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void NavigateToEmbeddedProduction()
    {
        MainTabs.SelectedIndex = 2;
        ApplyNavigationSelection(2);
    }
}
