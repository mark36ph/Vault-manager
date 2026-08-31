namespace FactVaultManager.Desktop;

public sealed partial class DesktopDataService
{
    public PublicationStateStore PublicationState => new(_databasePath);
}
