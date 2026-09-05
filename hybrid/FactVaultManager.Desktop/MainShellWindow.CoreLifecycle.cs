using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly Brush NavSelectedBackground = new SolidColorBrush(Color.FromRgb(232, 240, 248));
    private static readonly Brush NavSelectedBorder = new SolidColorBrush(Color.FromRgb(15, 108, 189));
    private static readonly Brush NavTransparent = Brushes.Transparent;
    private bool _productBrandApplied;
    private List<Button>? _indexedNavigationButtons;

    protected override void OnInitialized(EventArgs e)
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        AllowsTransparency = false;
        WindowState = WindowState.Maximized;
        base.OnInitialized(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        MainTabs.Margin = new Thickness(0);
        MainTabs.Padding = new Thickness(0);
        MainTabs.BorderThickness = new Thickness(0);
        MainTabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;

        if (FindResource("HiddenPageTabStyle") is Style hiddenPageStyle)
        {
            foreach (var tab in MainTabs.Items.OfType<TabItem>())
                tab.Style = hiddenPageStyle;
        }

        InitializeQuizQuestionViewer();
        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ApplyProductBranding();
        InitializeQuizWorkflow();
        InitializeQuizQuestionBankPage();
        InitializeQuizHistoryPage();
        InitializeQuizNotesPage();
        InitializeUploadManagerPage();
        InitializeYouTubeAnalyticsPage();
        InitializeFacebookAnalyticsPage();
        InitializeInstagramManagerPage();
        InitializeQuizDraftEditor();
        InitializeQuizRotationWorkflow();
        InitializeQuizExportWorkflow();
        InitializeQuizWorkspaceNavigation();
        ApplyNavigationSections();
        Dispatcher.BeginInvoke(new Action(InitializeSettingsWorkflow));
    }

    private void ApplyProductBranding()
    {
        if (_productBrandApplied)
            return;

        _productBrandApplied = true;
        Title = "Factburst Quiz Manager";
        if (Content is not DependencyObject root)
            return;

        var brand = FindVisualChildren<TextBlock>(root)
            .FirstOrDefault(block => string.Equals(block.Text, "FactVaultManager", StringComparison.Ordinal));
        if (brand is not null)
            brand.Text = "Factburst Quiz Manager";
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index))
        {
            MainTabs.SelectedIndex = index;
            ApplyNavigationSelection(index);
        }
    }

    private void ApplyNavigationSelection(int selectedIndex)
    {
        if (Content is not DependencyObject root)
            return;

        if (_indexedNavigationButtons is null)
        {
            _indexedNavigationButtons = FindVisualChildren<Button>(root)
                .Where(button => int.TryParse(button.Tag?.ToString(), out _))
                .ToList();
        }

        foreach (var button in _indexedNavigationButtons)
        {
            if (!button.IsVisible || !int.TryParse(button.Tag?.ToString(), out var index))
                continue;

            var isSelected = index == selectedIndex;
            button.Background = isSelected ? NavSelectedBackground : NavTransparent;
            button.BorderBrush = isSelected ? NavSelectedBorder : NavTransparent;
            button.BorderThickness = new Thickness(3, 0, 0, 0);
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
