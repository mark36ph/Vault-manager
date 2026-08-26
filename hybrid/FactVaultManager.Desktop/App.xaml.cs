namespace FactVaultManager.Desktop;

public partial class App : System.Windows.Application
{
    private bool _thumbnailRegenerationActionsInitialized;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_thumbnailRegenerationActionsInitialized || MainWindow is not MainShellWindow shell)
            return;

        shell.InitializeUploadManagerThumbnailRegenerationActions();
        _thumbnailRegenerationActionsInitialized = true;
    }
}
