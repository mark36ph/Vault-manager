using Velopack;
using Velopack.Sources;

namespace FactVaultManager.Desktop;

public sealed class AppUpdateService
{
    private const string RepositoryUrl = "https://github.com/mark36ph/Vault-manager";
    private readonly UpdateManager _manager = new(
        new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)
    );

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "development";

    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }
        return await _manager.CheckForUpdatesAsync();
    }

    public async Task InstallAsync(UpdateInfo update, Action<int>? progress = null)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException("Updates can only be installed from an installed FactVaultManager build.");
        }
        await _manager.DownloadUpdatesAsync(update, progress);
        _manager.ApplyUpdatesAndRestart(update);
    }
}
