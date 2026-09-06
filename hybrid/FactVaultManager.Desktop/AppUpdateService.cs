using System.Diagnostics;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace FactVaultManager.Desktop;

public sealed class AppUpdateService
{
    private const string RepositoryUrl = "https://github.com/mark36ph/Vault-manager";
    private const string LatestVersionManifestUrl = "https://raw.githubusercontent.com/mark36ph/Vault-manager/main/version.json";
    public const string StableSetupDownloadUrl = RepositoryUrl + "/releases/latest/download/FactVaultManager-win-x64-stable-Setup.exe";

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
            throw new InvalidOperationException("Updates can only be installed from an installed Factburst Quiz Manager build.");
        }
        await _manager.DownloadUpdatesAsync(update, progress);
        _manager.ApplyUpdatesAndRestart(update);
    }

    public async Task<Version?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(
            LatestVersionManifestUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("latest_version", out var versionElement))
            return null;

        var versionText = versionElement.GetString();
        return Version.TryParse(versionText, out var version) ? version : null;
    }

    public async Task<bool> InstallLatestSetupIfNewerAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return false;

        var latest = await GetLatestVersionAsync(cancellationToken);
        if (latest is null || !Version.TryParse(CurrentVersion, out var current) || latest <= current)
            return false;

        await DownloadAndLaunchInstallerAsync(progress, cancellationToken);
        return true;
    }

    public async Task<string> BootstrapInstallAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
            throw new InvalidOperationException("Factburst Quiz Manager is already installed. Use the normal update check instead.");

        return await DownloadAndLaunchInstallerAsync(progress, cancellationToken);
    }

    private async Task<string> DownloadAndLaunchInstallerAsync(
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        var installerPath = Path.Combine(
            Path.GetTempPath(),
            $"FactburstQuizManager-Setup-{Guid.NewGuid():N}.exe");

        using var client = CreateHttpClient();
        using var response = await client.GetAsync(
            StableSetupDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            installerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            if (total is > 0)
                progress?.Invoke((int)Math.Clamp(downloaded * 100 / total.Value, 0, 100));
        }
        await output.FlushAsync(cancellationToken);

        if (new FileInfo(installerPath).Length < 1024 * 1024)
        {
            File.Delete(installerPath);
            throw new InvalidOperationException("The downloaded installer was incomplete. Try Updates again in a moment.");
        }

        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
        });
        return installerPath;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FactburstQuizManager-Updater/1.1");
        return client;
    }

    public async Task<string> RunAsync(Action<int>? progress = null)
    {
        if (!IsInstalled)
        {
            return "This copy is not installed yet. Use Updates to install the current Factburst Quiz Manager release.";
        }

        UpdateInfo? update = null;
        try
        {
            update = await CheckAsync();
        }
        catch
        {
            // Fall back to the stable installer path when an older installed release
            // has stale or incomplete Velopack update metadata.
        }

        if (update is not null)
        {
            await InstallAsync(update, progress);
            return "Update installed.";
        }

        if (await InstallLatestSetupIfNewerAsync(progress))
            return "Update installer started.";

        return $"Factburst Quiz Manager {CurrentVersion} is up to date.";
    }
}
