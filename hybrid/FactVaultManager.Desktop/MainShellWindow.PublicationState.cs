using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string PublicationStateButtonTag = "unified-publication-state";
    private bool _publicationStateUiInitialized;
    private int _publicationStateUiAttempts;
    private Button? _publicationStateButton;

    public void InitializeUnifiedPublicationStateUi()
    {
        if (_publicationStateUiInitialized) return;
        _publicationStateUiInitialized = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsurePublicationStateButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsurePublicationStateButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsurePublicationStateButton));
    }

    private void EnsurePublicationStateButton()
    {
        if (_publicationStateButton?.Parent is not null) return;
        if (Content is not DependencyObject root)
        {
            RetryPublicationStateButton();
            return;
        }

        var existing = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                button.Tag?.ToString(), PublicationStateButtonTag, StringComparison.Ordinal));
        if (existing is not null)
        {
            _publicationStateButton = existing;
            return;
        }

        var uploadQueue = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(
                Convert.ToString(button.Content), "Upload Queue", StringComparison.Ordinal));
        if (uploadQueue?.Parent is not WrapPanel actions)
        {
            RetryPublicationStateButton();
            return;
        }

        var button = new Button
        {
            Content = "Publication State",
            Tag = PublicationStateButtonTag,
            MinWidth = 126,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Show the unified local publication state for the selected quiz and its promo.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(73, 190, 255));
        button.Click += (_, _) => ShowUnifiedPublicationState();

        var uploadQueueIndex = actions.Children.IndexOf(uploadQueue);
        actions.Children.Insert(uploadQueueIndex < 0 ? actions.Children.Count : uploadQueueIndex, button);
        _publicationStateButton = button;
    }

    private void RetryPublicationStateButton()
    {
        if (++_publicationStateUiAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsurePublicationStateButton();
        };
        timer.Start();
    }

    private void ShowUnifiedPublicationState()
    {
        if (_uploadManagerGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(this, "Select a quiz in Upload Manager first.",
                "Publication State", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = "Publication State",
            Owner = this,
            Width = 1050,
            Height = 560,
            MinWidth = 800,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = history.UploadTitleDisplay,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        });

        var summary = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(24, 39, 105)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(29, 48, 122)),
            BorderThickness = new Thickness(0),
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Content",
            Binding = new Binding(nameof(PublicationStateEntry.ContentKind)),
            Width = new DataGridLength(80),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Platform",
            Binding = new Binding(nameof(PublicationStateEntry.Platform)),
            Width = new DataGridLength(100),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "State",
            Binding = new Binding(nameof(PublicationStateEntry.Display)),
            Width = new DataGridLength(150),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Remote ID",
            Binding = new Binding(nameof(PublicationStateEntry.RemoteId)),
            Width = new DataGridLength(130),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Remote URL",
            Binding = new Binding(nameof(PublicationStateEntry.RemoteUrl)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Error",
            Binding = new Binding(nameof(PublicationStateEntry.LastError)),
            Width = new DataGridLength(220),
        });
        var table = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Child = grid,
        };
        Grid.SetRow(table, 2);
        root.Children.Add(table);

        void Refresh()
        {
            try
            {
                var store = _data.PublicationState;
                store.Reconcile(history.Id, history.ProjectFolder);
                var rows = store.List(history.Id).ToList();
                grid.ItemsSource = rows;
                var quizSummary = PublicationStateSummary.Display(rows, PublicationContentKind.Quiz);
                var promoSummary = PublicationStateSummary.Display(rows, PublicationContentKind.Promo);
                summary.Text = $"Quiz: {quizSummary}\nPromo: {promoSummary}";
            }
            catch (Exception error)
            {
                summary.Text = "Publication state could not be refreshed: " + error.Message;
            }
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var refresh = new Button { Content = "Refresh", MinWidth = 92, MinHeight = 34 };
        StyleQuizHistoryButton(refresh, Color.FromRgb(0, 204, 255));
        refresh.Click += (_, _) => Refresh();
        var close = new Button { Content = "Close", MinWidth = 82, MinHeight = 34, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => dialog.Close();
        actions.Children.Add(refresh);
        actions.Children.Add(close);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        dialog.Content = root;
        Refresh();
        dialog.ShowDialog();
    }
}
