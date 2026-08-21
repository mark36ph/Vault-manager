using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _quizNotesPageInitialized;
    private int _quizNotesTabIndex = -1;
    private TextBox? _quizNotesTextBox;
    private TextBlock? _quizNotesStatusText;

    private void InitializeQuizNotesPage()
    {
        if (_quizNotesPageInitialized || MainTabs is null)
            return;

        _quizNotesPageInitialized = true;
        var tab = new TabItem { Content = BuildQuizNotesPage() };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
            tab.Style = hiddenStyle;
        MainTabs.Items.Add(tab);
        _quizNotesTabIndex = MainTabs.Items.Count - 1;
        AddQuizNotesNavigationButton(_quizNotesTabIndex);
        LoadQuizNotes();
    }

    private FrameworkElement BuildQuizNotesPage()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Quiz Notes",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Keep quiz ideas, production plans, scheduling reminders, and anything else you want to remember.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        root.Children.Add(heading);

        _quizNotesTextBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(22, 38, 82)),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(16),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            MaxLength = QuizNotesStore.MaximumLength,
            ToolTip = "Your notes are stored with the app settings. Press Ctrl+S to save.",
        };
        _quizNotesTextBox.KeyDown += QuizNotesTextBox_KeyDown;
        Grid.SetRow(_quizNotesTextBox, 1);
        root.Children.Add(_quizNotesTextBox);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _quizNotesStatusText = new TextBlock
        {
            Text = "Ready",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(_quizNotesStatusText);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var reload = new Button { Content = "Reload", MinWidth = 90, MinHeight = 36 };
        StyleQuizHistoryButton(reload, Color.FromRgb(204, 70, 255));
        reload.Click += (_, _) => LoadQuizNotes();
        actions.Children.Add(reload);

        var save = new Button { Content = "Save notes", MinWidth = 112, MinHeight = 36, Margin = new Thickness(10, 0, 0, 0) };
        StyleQuizHistoryButton(save, Color.FromRgb(70, 235, 115));
        save.Click += (_, _) => SaveQuizNotes();
        actions.Children.Add(save);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private void AddQuizNotesNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;

        var historyButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizHistoryTabIndex.ToString(), StringComparison.Ordinal));
        if (historyButton?.Parent is not StackPanel navigation)
            return;

        var notesButton = new Button
        {
            Content = "✎   Quiz Notes",
            Tag = tabIndex.ToString(),
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            notesButton.Style = navStyle;
        notesButton.Click += Navigate_Click;
        var historyIndex = navigation.Children.IndexOf(historyButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, historyIndex + 1), notesButton);
    }

    private void QuizNotesTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        SaveQuizNotes();
        e.Handled = true;
    }

    private void LoadQuizNotes()
    {
        if (_quizNotesTextBox is null)
            return;

        _quizNotesTextBox.Text = _data.LoadQuizNotes();
        if (_quizNotesStatusText is not null)
            _quizNotesStatusText.Text = $"Loaded • {_quizNotesTextBox.Text.Length:N0} characters";
    }

    private void SaveQuizNotes()
    {
        if (_quizNotesTextBox is null)
            return;

        try
        {
            _data.SaveQuizNotes(_quizNotesTextBox.Text);
            if (_quizNotesStatusText is not null)
                _quizNotesStatusText.Text = $"Saved {DateTime.Now:dd-MM-yyyy HH:mm} • {_quizNotesTextBox.Text.Length:N0} characters";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Quiz Notes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
