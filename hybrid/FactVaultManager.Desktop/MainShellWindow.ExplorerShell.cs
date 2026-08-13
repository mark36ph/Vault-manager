using System;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        FinalizeExplorerNavigation();
    }
}
