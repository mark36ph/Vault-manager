using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _newFactToolbarConfigured;

    private void ConfigureNewFactToolbar()
    {
        if (_newFactToolbarConfigured)
            return;

        NewProjectTitleTextBox.Visibility = Visibility.Collapsed;

        if (NewProjectTitleTextBox.Parent is not Panel toolbar)
            return;

        var button = toolbar.Children
            .OfType<Button>()
            .FirstOrDefault(item =>
            {
                var text = item.Content?.ToString() ?? "";
                return text.Contains("New project", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("New Fact", StringComparison.OrdinalIgnoreCase);
            });

        if (button is null)
            return;

        button.Click -= CreateProject_Click;
        button.Click -= NewFactToolbar_Click;
        button.Click += NewFactToolbar_Click;
        button.Content = "+  New Fact";
        button.Margin = new Thickness(0);
        _newFactToolbarConfigured = true;
    }

    private void NewFactToolbar_Click(object sender, RoutedEventArgs e)
    {
        ShowNewFactWorkspace();
    }
}
