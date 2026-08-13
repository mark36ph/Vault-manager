using System;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool NativeProjectsCompatibilityRegistered = RegisterNativeProjectsCompatibility();

    private static bool RegisterNativeProjectsCompatibility()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not MainShellWindow window) return;
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.SystemIdle,
                    new Action(() => window.RestoreNativeProjectFieldVisibility(0)));
            }));
        return true;
    }

    private void RestoreNativeProjectFieldVisibility(int attempt)
    {
        _ = NativeProjectsCompatibilityRegistered;
        if (!_nativeProjectsApplied)
        {
            if (attempt < 3)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.SystemIdle,
                    new Action(() => RestoreNativeProjectFieldVisibility(attempt + 1)));
            }
            return;
        }

        ProjectCategoryTextBox.Visibility = Visibility.Visible;
        ProjectStatusComboBox.Visibility = Visibility.Visible;
        ProjectPinnedCheckBox.Visibility = Visibility.Visible;
        ProjectScriptTextBox.Visibility = Visibility.Visible;
        ProjectDescriptionTextBox.Visibility = Visibility.Visible;
        ProjectPinnedCommentTextBox.Visibility = Visibility.Visible;
        ProjectTagsTextBox.Visibility = Visibility.Visible;
        ProjectNotesTextBox.Visibility = Visibility.Visible;
        ProjectSourcesTextBox.Visibility = Visibility.Visible;
    }
}
