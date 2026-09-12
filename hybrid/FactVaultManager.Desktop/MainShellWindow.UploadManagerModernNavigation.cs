using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool UploadManagerModernNavigationRegistered = RegisterUploadManagerModernNavigation();
    private bool _uploadManagerModernNavigationAdded;
    private int _uploadManagerModernNavigationAttempts;

    private static bool RegisterUploadManagerModernNavigation()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainShellWindowLoadedForUploadManagerNavigation),
            handledEventsToo: true);
        return true;
    }

    private static void MainShellWindowLoadedForUploadManagerNavigation(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(window.EnsureUploadManagerModernNavigation));
        }
    }

    private void EnsureUploadManagerModernNavigation()
    {
        if (_uploadManagerModernNavigationAdded || Content is not DependencyObject root)
            return;

        var library = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(),
                AutopilotFirstNavTag + ":Library",
                StringComparison.Ordinal));

        if (library?.Parent is not StackPanel navigation)
        {
            if (++_uploadManagerModernNavigationAttempts < 60)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(EnsureUploadManagerModernNavigation));
            }
            return;
        }

        if (navigation.Children.OfType<Button>().Any(button => string.Equals(
                button.Tag?.ToString(),
                AutopilotFirstNavTag + ":Upload Manager",
                StringComparison.Ordinal)))
        {
            _uploadManagerModernNavigationAdded = true;
            return;
        }

        var button = new Button
        {
            Content = "⇧   Upload Manager",
            Tag = AutopilotFirstNavTag + ":Upload Manager",
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            button.Style = navStyle;

        button.Click += (_, _) =>
        {
            // The modern button is nested inside the compact navigation container,
            // so NavigateLegacy cannot find it by searching direct legacy children.
            // Initialize the page on demand, then select its actual hidden tab directly.
            InitializeUploadManagerPage();
            if (_uploadManagerTabIndex < 0 || MainTabs is null || _uploadManagerTabIndex >= MainTabs.Items.Count)
            {
                MessageBox.Show(this,
                    "The Upload Manager could not be initialized. Check the database status in Settings.",
                    "Factburst Autopilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MainTabs.SelectedIndex = _uploadManagerTabIndex;
            ApplyNavigationSelection(_uploadManagerTabIndex);
            SelectAutopilotNav("Upload Manager");
        };

        var libraryIndex = navigation.Children.IndexOf(library);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, libraryIndex + 1), button);
        _autopilotNavButtons["Upload Manager"] = button;
        _uploadManagerModernNavigationAdded = true;
    }
}
