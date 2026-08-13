using System.Windows;

namespace FactVaultManager.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new ProductionWindow());
    }
}
