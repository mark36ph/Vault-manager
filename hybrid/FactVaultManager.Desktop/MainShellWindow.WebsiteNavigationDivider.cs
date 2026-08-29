using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string WebsiteNavigationDividerTag = "website-section-divider";
    private bool _websiteNavigationDividerInitialized;
    private DispatcherTimer? _websiteNavigationDividerTimer;

    public void InitializeWebsiteNavigationDivider()
    {
        if (_websiteNavigationDividerInitialized) return;
        _websiteNavigationDividerInitialized = true;
        _websiteNavigationDividerTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _websiteNavigationDividerTimer.Tick += (_, _) => EnsureWebsiteNavigationDivider();
        _websiteNavigationDividerTimer.Start();
        Closed += (_, _) => _websiteNavigationDividerTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteNavigationDivider));
    }

    private void EnsureWebsiteNavigationDivider()
    {
        if (_autopilotNavContainer is null || !_autopilotNavButtons.TryGetValue("Website", out var website))
            return;
        if (_autopilotNavContainer.Children.OfType<Border>()
            .Any(border => string.Equals(Convert.ToString(border.Tag), WebsiteNavigationDividerTag, StringComparison.Ordinal)))
        {
            _websiteNavigationDividerTimer?.Stop();
            return;
        }

        var index = _autopilotNavContainer.Children.IndexOf(website);
        if (index < 0) return;
        var divider = new Border
        {
            Tag = WebsiteNavigationDividerTag,
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            Margin = new Thickness(12, 12, 12, 12),
        };
        _autopilotNavContainer.Children.Insert(index, divider);
        _websiteNavigationDividerTimer?.Stop();
    }
}
