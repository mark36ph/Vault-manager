using System.Windows;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly Brush FactburstWindowBrush = new SolidColorBrush(Color.FromRgb(243, 243, 243));
    private static readonly Brush FactburstPaneBrush = new SolidColorBrush(Color.FromRgb(247, 247, 247));
    private static readonly Brush FactburstTextBrush = new SolidColorBrush(Color.FromRgb(31, 31, 31));
    private static readonly Brush FactburstMutedBrush = new SolidColorBrush(Color.FromRgb(102, 112, 133));
    private static readonly Brush FactburstBorderBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225));

    private void InitializeFactburstVisualBaseline()
    {
        Background = FactburstWindowBrush;
        Foreground = FactburstTextBrush;
        FontFamily = new FontFamily("Segoe UI Variable Text");
        FontSize = 13;
        Width = 1440;
        Height = 900;
        MinWidth = 1120;
        MinHeight = 720;

        HeaderStatusText.Foreground = FactburstMutedBrush;

        if (PrimaryNavigationPanel.Parent is Border navigationBorder)
        {
            navigationBorder.Background = FactburstPaneBrush;
            navigationBorder.BorderBrush = FactburstBorderBrush;
        }

        if (MainTabs is not null)
        {
            MainTabs.Background = FactburstWindowBrush;
            MainTabs.BorderBrush = Brushes.Transparent;
        }
    }
}
