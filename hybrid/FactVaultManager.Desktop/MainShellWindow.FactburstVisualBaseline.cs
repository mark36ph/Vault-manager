using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly Brush FactburstWindowBrush = CreateBrush(243, 243, 243);
    private static readonly Brush FactburstPaneBrush = CreateBrush(247, 247, 247);
    private static readonly Brush FactburstTextBrush = CreateBrush(31, 31, 31);
    private static readonly Brush FactburstMutedBrush = CreateBrush(102, 112, 133);
    private static readonly Brush FactburstBorderBrush = CreateBrush(225, 225, 225);

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

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
