namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    internal DesktopDataService(string runtimeRoot, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _databasePath = Path.Combine(_dataRoot, "data", "factvault.db");
        _settingsPath = Path.Combine(_dataRoot, "data", "settings.json");
    }
}
