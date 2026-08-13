using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ApplyChromePolish()
    {
        StyleSidebarChrome();
        StyleTopCommandBar();
    }

    private void StyleSidebarChrome()
    {
        MainTabs.Background = MakeBrush("#111111");
        MainTabs.Padding = new Thickness(0);

        const string navStyle = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type TabItem}">
                <Setter Property="Width" Value="206" />
                <Setter Property="Height" Value="44" />
                <Setter Property="Margin" Value="0,0,10,4" />
                <Setter Property="Padding" Value="12,0" />
                <Setter Property="FontSize" Value="13" />
                <Setter Property="FontWeight" Value="Normal" />
                <Setter Property="Foreground" Value="#D7D7D7" />
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="BorderBrush" Value="Transparent" />
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                <Setter Property="VerticalContentAlignment" Value="Center" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type TabItem}">
                            <Grid Margin="{TemplateBinding Margin}">
                                <Border x:Name="Surface"
                                        Background="{TemplateBinding Background}"
                                        BorderBrush="{TemplateBinding BorderBrush}"
                                        BorderThickness="1"
                                        CornerRadius="4" />
                                <Border x:Name="Indicator"
                                        Width="3"
                                        Height="24"
                                        HorizontalAlignment="Left"
                                        VerticalAlignment="Center"
                                        Background="Transparent"
                                        CornerRadius="2" />
                                <ContentPresenter ContentSource="Header"
                                                  Margin="{TemplateBinding Padding}"
                                                  HorizontalAlignment="Stretch"
                                                  VerticalAlignment="Center" />
                            </Grid>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Surface" Property="Background" Value="#232323" />
                                </Trigger>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter TargetName="Surface" Property="Background" Value="#242424" />
                                    <Setter TargetName="Indicator" Property="Background" Value="#60A5FA" />
                                    <Setter Property="Foreground" Value="#FFFFFF" />
                                    <Setter Property="FontWeight" Value="SemiBold" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """;

        var style = (Style)XamlReader.Parse(navStyle);
        var items = MainTabs.Items.OfType<TabItem>().ToArray();
        var labels = new[]
        {
            ("\uE80F", "Dashboard"),
            ("\uE8A5", "Projects"),
            ("\uE768", "Production"),
            ("\uE8B7", "Media Library"),
            ("\uE7B3", "Asset Review"),
            ("\uE713", "Settings"),
        };

        for (var index = 0; index < items.Length && index < labels.Length; index++)
        {
            items[index].Style = style;
            items[index].Header = MakeNavHeader(labels[index].Item1, labels[index].Item2);
        }
    }

    private static FrameworkElement MakeNavHeader(string glyph, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private void StyleTopCommandBar()
    {
        if (Content is not Grid shellGrid)
        {
            return;
        }

        var header = shellGrid.Children.OfType<Border>().FirstOrDefault(border => Grid.GetRow(border) == 0);
        if (header?.Child is not Grid headerGrid)
        {
            return;
        }

        header.Background = MakeBrush("#181818");
        header.BorderBrush = MakeBrush("#303030");

        var titleStack = headerGrid.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 0);
        if (titleStack is not null)
        {
            var title = titleStack.Children.OfType<TextBlock>().FirstOrDefault();
            if (title is not null)
            {
                title.FontFamily = new FontFamily("Segoe UI Variable Display");
                title.FontSize = 20;
                title.FontWeight = FontWeights.SemiBold;
            }
            HeaderStatusText.FontSize = 11.5;
            HeaderStatusText.Foreground = MakeBrush("#9B9B9B");
        }

        var commands = headerGrid.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 1);
        if (commands is null)
        {
            return;
        }

        var buttons = commands.Children.OfType<Button>().ToArray();
        if (buttons.Length > 0)
        {
            StyleCommandButton(buttons[0], "\uE895", "Updates", false);
        }
        if (buttons.Length > 1)
        {
            StyleCommandButton(buttons[1], "\uE768", "Production", true);
        }
    }

    private static void StyleCommandButton(Button button, string glyph, string label, bool primary)
    {
        button.Height = 34;
        button.MinWidth = 0;
        button.Padding = new Thickness(10, 0, 11, 0);
        button.Margin = primary ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        button.FontFamily = new FontFamily("Segoe UI");
        button.FontSize = 12;
        button.FontWeight = FontWeights.Normal;
        button.Background = MakeBrush(primary ? "#2563EB" : "#232323");
        button.BorderBrush = MakeBrush(primary ? "#3B82F6" : "#3A3A3A");

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        button.Content = content;
    }
}
