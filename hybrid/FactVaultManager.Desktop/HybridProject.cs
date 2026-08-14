namespace FactVaultManager.Desktop;

public sealed record HybridProject(
    int Id,
    string Title,
    string Status,
    string Category,
    string Folder,
    bool FolderExists,
    bool CheckpointExists,
    bool TimelineExists)
{
    public string DisplayName => $"{Title}  •  {Status}";
}
