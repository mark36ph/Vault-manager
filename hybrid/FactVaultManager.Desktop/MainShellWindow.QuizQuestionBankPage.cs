using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizQuestionBankPageHookRegistered = RegisterQuizQuestionBankPageHook();
    private bool _quizQuestionBankPageInitialized;
    private int _quizQuestionBankPageAttempts;
    private int _quizQuestionBankTabIndex = -1;

    private static bool RegisterQuizQuestionBankPageHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainShellWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizQuestionBankWindow_Loaded));
        return true;
    }

    private static void QuizQuestionBankWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainShellWindow window)
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(window.InitializeQuizQuestionBankPage));
    }

    private void InitializeQuizQuestionBankPage()
    {
        if (_quizQuestionBankPageInitialized)
            return;

        if (MainTabs is null || _quizBankGrid is null || _quizTabIndex < 0)
        {
            RetryQuizQuestionBankPageInitialization();
            return;
        }

        var workspace = FindQuizQuestionBankWorkspace(_quizBankGrid);
        if (workspace is null)
        {
            RetryQuizQuestionBankPageInitialization();
            return;
        }

        var bankCard = workspace.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 0);
        var draftCard = workspace.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        if (bankCard is null || draftCard is null)
        {
            RetryQuizQuestionBankPageInitialization();
            return;
        }

        _quizQuestionBankPageInitialized = true;

        workspace.Children.Remove(bankCard);
        Grid.SetColumn(draftCard, 0);
        workspace.ColumnDefinitions.Clear();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UpdateQuizBuilderCopy();
        MakeQuizBuilderScrollable();
        RenameQuestionBankCardHeading(bankCard);
        ConfigureStandaloneQuestionBank(bankCard);
        if (_quizBankTabs is not null)
        {
            EnsureQuizCategoriesTab(_quizBankTabs);
            EnsureQuizDuplicatesTab(_quizBankTabs);
            StyleStandaloneQuestionBankTabs(_quizBankTabs);
        }

        var questionBankTab = new TabItem { Content = BuildStandaloneQuestionBankPage(bankCard) };
        if (FindResource("HiddenPageTabStyle") is Style hiddenStyle)
            questionBankTab.Style = hiddenStyle;
        MainTabs.Items.Add(questionBankTab);
        _quizQuestionBankTabIndex = MainTabs.Items.Count - 1;
        AddQuestionBankNavigationButton(_quizQuestionBankTabIndex);
        ApplyNavigationSections();
        ApplyNavigationSelection(MainTabs.SelectedIndex);
    }

    private void RetryQuizQuestionBankPageInitialization()
    {
        if (_quizQuestionBankPageAttempts++ >= 40)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(InitializeQuizQuestionBankPage));
    }

    private static Grid? FindQuizQuestionBankWorkspace(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is Grid grid &&
                grid.ColumnDefinitions.Count == 3 &&
                grid.Children.OfType<Border>().Any(child => Grid.GetColumn(child) == 0) &&
                grid.Children.OfType<Border>().Any(child => Grid.GetColumn(child) == 2))
            {
                return grid;
            }

            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void MakeQuizBuilderScrollable()
    {
        if (_quizTabIndex < 0 || _quizTabIndex >= MainTabs.Items.Count || MainTabs.Items[_quizTabIndex] is not TabItem quizTab)
            return;
        if (quizTab.Content is ScrollViewer)
            return;
        if (quizTab.Content is not FrameworkElement content)
            return;

        quizTab.Content = null;
        quizTab.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };
    }

    private FrameworkElement BuildStandaloneQuestionBankPage(Border bankCard)
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Questions",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 204, 255),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55,
            },
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Search, review, and manage the reusable question bank.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        header.Children.Add(heading);

        var backToQuizzes = new Button
        {
            Content = "Quiz Builder",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleStandaloneQuestionBankButton(backToQuizzes, Color.FromRgb(0, 204, 255));
        backToQuizzes.Click += (_, _) =>
        {
            MainTabs.SelectedIndex = _quizTabIndex;
            ApplyNavigationSelection(_quizTabIndex);
        };
        Grid.SetColumn(backToQuizzes, 1);
        header.Children.Add(backToQuizzes);
        root.Children.Add(header);

        bankCard.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetColumn(bankCard, 0);
        Grid.SetRow(bankCard, 1);
        root.Children.Add(bankCard);

        return new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(7, 13, 57), 0),
                    new(Color.FromRgb(18, 34, 115), 0.58),
                    new(Color.FromRgb(80, 30, 145), 1),
                },
                new Point(0, 0),
                new Point(1, 1)),
            Child = root,
        };
    }

    private static void StyleStandaloneQuestionBankButton(Button button, Color accent)
    {
        button.Background = new SolidColorBrush(Color.FromRgb(13, 18, 78));
        button.BorderBrush = new SolidColorBrush(accent);
        button.BorderThickness = new Thickness(2);
        button.Foreground = Brushes.White;
        button.FontWeight = FontWeights.SemiBold;
        button.Padding = new Thickness(12, 6, 12, 6);
        button.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = accent,
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = 0.4,
        };
    }

    private static void StyleStandaloneQuestionBankTabs(TabControl tabs)
    {
        tabs.Background = new SolidColorBrush(Color.FromRgb(8, 14, 62));
        tabs.BorderThickness = new Thickness(0);

        var tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38d));
        tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(15, 0, 15, 0)));
        tabStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        tabStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(35, 62, 145))));
        tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(22, 55, 120))));
        tabStyle.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 86, 170))));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        tabStyle.Triggers.Add(selectedTrigger);

        foreach (var tab in tabs.Items.OfType<TabItem>())
            tab.Style = tabStyle;
    }

    private void AddQuestionBankNavigationButton(int tabIndex)
    {
        if (Content is not DependencyObject root)
            return;

        var quizButton = FindVisualChildren<Button>(root)
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), _quizTabIndex.ToString(), StringComparison.Ordinal));
        if (quizButton?.Parent is not StackPanel navigation)
            return;

        var existing = navigation.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), tabIndex.ToString(), StringComparison.Ordinal));
        if (existing is not null)
            return;

        var questionBankButton = new Button
        {
            Content = "☷   Questions",
            Tag = tabIndex.ToString(),
        };
        if (FindResource("NavButtonStyle") is Style navStyle)
            questionBankButton.Style = navStyle;
        questionBankButton.Click += Navigate_Click;

        var quizIndex = navigation.Children.IndexOf(quizButton);
        navigation.Children.Insert(Math.Min(navigation.Children.Count, quizIndex + 1), questionBankButton);
    }

    private void UpdateQuizBuilderCopy()
    {
        if (_quizTabIndex < 0 || _quizTabIndex >= MainTabs.Items.Count || MainTabs.Items[_quizTabIndex] is not TabItem quizTab)
            return;
        if (quizTab.Content is not DependencyObject quizRoot)
            return;

        var subtitle = FindVisualChildren<TextBlock>(quizRoot)
            .FirstOrDefault(text => string.Equals(
                text.Text,
                "Build a reusable question bank, pick random questions, and prepare quiz videos for Resolve.",
                StringComparison.Ordinal));
        if (subtitle is not null)
            subtitle.Text = "Pick random questions from your reusable bank, set the timing, and prepare quiz videos for Resolve.";
    }

    private static void RenameQuestionBankCardHeading(Border bankCard)
    {
        var heading = FindVisualChildren<TextBlock>(bankCard)
            .FirstOrDefault(text => string.Equals(text.Text, "Question Bank", StringComparison.Ordinal));
        if (heading is not null)
            heading.Text = "All Questions";
    }
}
