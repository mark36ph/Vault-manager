using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizHistoryUiCleanupRegistered;
    private Button? _quizHistoryMoreButton;

    public void InitializeQuizHistoryUiCleanup()
    {
        if (_quizHistoryUiCleanupRegistered)
            return;

        _quizHistoryUiCleanupRegistered = true;
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyQuizHistoryUiCleanup));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(ApplyQuizHistoryUiCleanup));
    }

    private void ApplyQuizHistoryUiCleanup()
    {
        if (_quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Border { Child: Grid root })
        {
            return;
        }

        var header = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 0);
        RemoveQuizHistoryButtonsByContent(header, "Update paths");

        var analyticsRefresh = header?.Children
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "Refresh", StringComparison.Ordinal) ||
                string.Equals(button.Content?.ToString(), "Refresh analytics", StringComparison.Ordinal));
        if (analyticsRefresh is not null)
        {
            analyticsRefresh.Content = "Refresh analytics";
            analyticsRefresh.MinWidth = 128;
            analyticsRefresh.ToolTip = "Refresh YouTube analytics for Quiz History";
        }

        var footer = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 3);
        var actions = footer?.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 1);
        if (actions is null)
            return;

        RemoveQuizHistoryButtonsByContent(actions, "Match archive", "Archive selected");

        var archive = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "Archive completed C", StringComparison.Ordinal) ||
                string.Equals(button.Content?.ToString(), "Archive C → Z", StringComparison.Ordinal));
        var delete = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Delete", StringComparison.Ordinal));

        if (_quizHistoryMoreButton is not null && actions.Children.Contains(_quizHistoryMoreButton))
        {
            if (archive is not null)
                archive.Visibility = Visibility.Collapsed;
            if (delete is not null)
                delete.Visibility = Visibility.Collapsed;
            return;
        }

        if (archive is not null)
        {
            archive.Content = "Archive C → Z";
            archive.Visibility = Visibility.Collapsed;
        }
        if (delete is not null)
            delete.Visibility = Visibility.Collapsed;

        var menuItemStyle = CreateQuizHistoryContextMenuItemStyle();
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Bottom,
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 78)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Template = CreateQuizHistoryContextMenuTemplate(),
            ItemsPanel = CreateQuizHistoryContextMenuItemsPanel(),
        };

        var publishItem = new MenuItem
        {
            Header = "Open Publish",
            ToolTip = "Reopen the selected quiz directly at its Publish step",
            Style = menuItemStyle,
        };
        publishItem.Click += (_, _) => ReopenSelectedQuizHistoryInBuilder("publish");
        menu.Items.Add(publishItem);

        var reopenItem = new MenuItem
        {
            Header = "Reopen in Quiz Builder",
            ToolTip = "Load the selected Quiz History entry back into the quiz workflow",
            Style = menuItemStyle,
        };
        reopenItem.Click += (_, _) => ReopenSelectedQuizHistoryInBuilder();
        menu.Items.Add(reopenItem);

        menu.Items.Add(CreateQuizHistoryContextMenuSeparator());

        var archiveItem = new MenuItem
        {
            Header = "Archive completed C → Z",
            ToolTip = "Archive completed physical C: quiz projects to Z: after upload and verification checks",
            IsEnabled = archive is not null,
            Style = menuItemStyle,
        };
        archiveItem.Click += async (_, _) =>
        {
            if (archive is not null)
                await ArchiveGroupedCompletedCQuizProjectsAsync(archive);
        };
        menu.Items.Add(archiveItem);

        menu.Items.Add(CreateQuizHistoryContextMenuSeparator());

        var deleteItem = new MenuItem
        {
            Header = "Delete selected quiz…",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 125, 135)),
            ToolTip = "Delete the selected Quiz History entry",
            Style = menuItemStyle,
        };
        deleteItem.Click += (_, _) => DeleteSelectedQuizHistory();
        menu.Items.Add(deleteItem);

        var more = new Button
        {
            Content = "More ▾",
            MinWidth = 82,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Reopen, publish, archive and delete actions",
            ContextMenu = menu,
        };
        StyleQuizHistoryButton(more, Color.FromRgb(105, 118, 255));
        more.Click += (_, _) =>
        {
            if (more.ContextMenu is null)
                return;
            more.ContextMenu.PlacementTarget = more;
            more.ContextMenu.IsOpen = true;
        };

        var deleteIndex = delete is null ? actions.Children.Count : actions.Children.IndexOf(delete);
        actions.Children.Insert(Math.Max(0, deleteIndex), more);
        _quizHistoryMoreButton = more;
    }

    private static ControlTemplate CreateQuizHistoryContextMenuTemplate()
    {
        var template = new ControlTemplate(typeof(ContextMenu));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 24, 78)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0, 204, 255)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(2));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static ItemsPanelTemplate CreateQuizHistoryContextMenuItemsPanel()
    {
        return new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
    }

    private static Style CreateQuizHistoryContextMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 24, 78))));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 18, 7)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 30d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        var template = new ControlTemplate(typeof(MenuItem));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.AppendChild(presenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        var highlighted = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true,
        };
        highlighted.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(28, 55, 132))));
        style.Triggers.Add(highlighted);

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5d));
        style.Triggers.Add(disabled);
        return style;
    }

    private static Separator CreateQuizHistoryContextMenuSeparator()
    {
        var template = new ControlTemplate(typeof(Separator));
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(92, 112, 190)));
        template.VisualTree = line;

        return new Separator
        {
            Height = 1,
            Margin = new Thickness(10, 5, 10, 5),
            Template = template,
            SnapsToDevicePixels = true,
        };
    }

    private static void RemoveQuizHistoryButtonsByContent(Panel? panel, params string[] contents)
    {
        if (panel is null || contents.Length == 0)
            return;

        var unwanted = contents.ToHashSet(StringComparer.Ordinal);
        var buttons = panel.Children
            .OfType<Button>()
            .Where(button => unwanted.Contains(button.Content?.ToString() ?? ""))
            .ToList();
        foreach (var button in buttons)
            panel.Children.Remove(button);
    }
}
