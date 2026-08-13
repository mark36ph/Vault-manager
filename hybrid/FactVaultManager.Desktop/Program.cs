using System.Windows;
using Velopack;

namespace FactVaultManager.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var application = new Application();
        application.Run(new MainShellWindow());
    }
}
