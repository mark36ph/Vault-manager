using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizOnlyCleanupInitialized;

    public void InitializeQuizOnlyCleanup()
    {
        if (_quizOnlyCleanupInitialized)
            return;

        _quizOnlyCleanupInitialized = true;
        RemoveLegacyStockProviderConfiguration();
        RemoveLegacyFactVideoSettingsSurfaces();
        RemoveLegacyFactVideoApiConnections();
    }

    private void RemoveLegacyStockProviderConfiguration()
    {
        // Factburst is quiz-only. Do not retain or re-use the old fact-video stock-provider secrets.
        PexelsKeyPasswordBox.Password = "";
        PixabayKeyPasswordBox.Password = "";

        try
        {
            var node = _data.LoadSettingsDocument();
            if (node.Remove("images"))
                _data.SaveSettingsDocument(node);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remove legacy image-provider settings: {error.Message}");
        }
    }

    private void RemoveLegacyFactVideoSettingsSurfaces()
    {
        if (_settingsNavButtons.TryGetValue("images", out var imageSettingsButton))
        {
            if (imageSettingsButton.Parent is Panel parent)
                parent.Children.Remove(imageSettingsButton);
            _settingsNavButtons.Remove("images");
        }

        _settingsPages.Remove("images");
    }

    private void RemoveLegacyFactVideoApiConnections()
    {
        _apiConnectionTests.Remove("pixabay");
        _apiConnectionTests.Remove("pexels");
        _apiConnectionStatuses.Remove("pixabay");
        _apiConnectionStatuses.Remove("pexels");

        if (!_settingsPages.TryGetValue("connections", out var connectionsPage) ||
            connectionsPage is not ScrollViewer scrollViewer ||
            scrollViewer.Content is not StackPanel page)
        {
            return;
        }

        RemoveSettingsSection(page, "Image providers");

        foreach (var text in FindVisualChildren<TextBlock>(page))
        {
            if (text.Text.Contains("production AI tasks", StringComparison.OrdinalIgnoreCase))
                text.Text = "Used for quiz question generation and related Factburst quiz text tasks.";
        }

        var saveButton = FindVisualChildren<Button>(page)
            .FirstOrDefault(button =>
            {
                var label = button.Content?.ToString() ?? "";
                return label.StartsWith("Save API", StringComparison.OrdinalIgnoreCase);
            });
        if (saveButton is not null)
            saveButton.Click += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RemoveLegacyStockProviderConfiguration));
    }

    private static void RemoveSettingsSection(StackPanel page, string title)
    {
        var section = page.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                border.Child is StackPanel stack &&
                stack.Children.OfType<TextBlock>().FirstOrDefault()?.Text.Equals(title, StringComparison.OrdinalIgnoreCase) == true);
        if (section is not null)
            page.Children.Remove(section);
    }
}
