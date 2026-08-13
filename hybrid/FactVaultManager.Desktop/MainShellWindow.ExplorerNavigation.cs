using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void FinalizeExplorerNavigation()
    {
        MainTabs.Padding = new Thickness(0);
        MainTabs.Margin = new Thickness(0);

        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            tab.Style = BuildExplorerTabStyle();
            tab.HorizontalContentAlignment = HorizontalAlignment.Left;
            tab.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }

    private static Style BuildExplorerTabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 220.0));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38.0));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 2, 8, 2)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(230, 230, 230))));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 0));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(39, 39, 39))));
        template.Triggers.Add(hover);

        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(43, 43, 43))));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(96, 205, 255))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
