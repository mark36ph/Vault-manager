using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public static class AppInteractionPolish
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            handledEventsToo: true);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(Convert.ToString(button.Content)?.Trim(), "Create Resolve Quiz", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Window.GetWindow(button) is MainShellWindow owner)
            QuizResolveProgressCoordinator.Begin(owner);
    }
}

public static class MessageBox
{
    public static MessageBoxResult Show(string messageBoxText) =>
        ShowCore(null, messageBoxText, "Content Vault Manager", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        ShowCore(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(null, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(null, messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(null, messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        ShowCore(null, messageBoxText, caption, button, icon, defaultResult, options);

    public static MessageBoxResult Show(Window owner, string messageBoxText) =>
        ShowCore(owner, messageBoxText, "Content Vault Manager", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
        ShowCore(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(owner, messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, options);

    private static MessageBoxResult ShowCore(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult,
        MessageBoxOptions options)
    {
        message ??= "";
        caption = string.IsNullOrWhiteSpace(caption) ? "Content Vault Manager" : caption.Trim();

        var exportActive = QuizResolveProgressCoordinator.IsActive;
        var finalQuizExportDialog = string.Equals(caption, "Quiz Resolve Export", StringComparison.OrdinalIgnoreCase);
        var quizPreflightDialog = string.Equals(caption, "Quiz Preflight", StringComparison.OrdinalIgnoreCase);

        if (exportActive)
        {
            if (finalQuizExportDialog)
            {
                if (icon == MessageBoxImage.Error)
                    QuizResolveProgressCoordinator.Cancel();
                else
                {
                    QuizResolveProgressCoordinator.Complete();
                    Thread.Sleep(90);
                }
            }
            else
            {
                QuizResolveProgressCoordinator.Pause();
            }
        }

        MessageBoxResult result;
        try
        {
            var resolvedOwner = ResolveOwner(owner);
            var dialog = new CleanMessageDialog(caption, message, buttons, icon, defaultResult);
            if (resolvedOwner is not null && resolvedOwner.IsLoaded && resolvedOwner.IsVisible)
            {
                dialog.Owner = resolvedOwner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            dialog.ShowDialog();
            result = dialog.Result;
        }
        catch
        {
            result = owner is not null
                ? System.Windows.MessageBox.Show(owner, message, caption, buttons, icon, defaultResult, options)
                : System.Windows.MessageBox.Show(message, caption, buttons, icon, defaultResult, options);
        }

        if (exportActive && !finalQuizExportDialog)
        {
            if (quizPreflightDialog && result != MessageBoxResult.Yes)
                QuizResolveProgressCoordinator.Cancel();
            else
                QuizResolveProgressCoordinator.Resume();
        }

        return result;
    }

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner is not null)
            return owner;

        try
        {
            var application = Application.Current;
            if (application is null)
                return null;

            return application.Windows
                       .OfType<Window>()
                       .FirstOrDefault(window => window.IsActive) ?? application.MainWindow;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class CleanMessageDialog : Window
{
    private readonly MessageBoxResult _safeCloseResult;
    private readonly MessageBoxResult _defaultResult;

    public CleanMessageDialog(
        string caption,
        string message,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult)
    {
        Title = caption;
        Width = 620;
        MaxWidth = 760;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _safeCloseResult = SafeCloseResult(buttons);
        _defaultResult = defaultResult == MessageBoxResult.None ? DefaultResult(buttons) : defaultResult;
        Closing += OnClosing;

        Content = BuildContent(caption, message, buttons, icon);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private FrameworkElement BuildContent(string caption, string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var shell = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(24, 21, 24, 20),
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Opacity = 0.22,
                Color = Colors.Black,
            },
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Child = root;

        var title = new TextBlock
        {
            Text = caption,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        root.Children.Add(title);

        var body = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        if (icon != MessageBoxImage.None)
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        }
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var textColumn = 0;
        if (icon != MessageBoxImage.None)
        {
            body.Children.Add(BuildIcon(icon));
            textColumn = 2;
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.Content = new TextBlock
        {
            Text = message,
            FontSize = 14,
            LineHeight = 21,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 610,
        };
        Grid.SetColumn(scroll, textColumn);
        body.Children.Add(scroll);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var button in BuildButtons(buttons))
            actions.Children.Add(button);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        return shell;
    }

    private FrameworkElement BuildIcon(MessageBoxImage icon)
    {
        var (glyph, background, foreground) = icon switch
        {
            MessageBoxImage.Error => ("×", Color.FromRgb(254, 243, 242), Color.FromRgb(180, 35, 24)),
            MessageBoxImage.Warning => ("!", Color.FromRgb(255, 250, 235), Color.FromRgb(181, 71, 8)),
            MessageBoxImage.Question => ("?", Color.FromRgb(244, 243, 255), Color.FromRgb(89, 37, 220)),
            _ => ("i", Color.FromRgb(239, 248, 255), Color.FromRgb(23, 92, 211)),
        };

        return new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(background),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = glyph == "i" ? 22 : 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = glyph == "i" ? new Thickness(0, -2, 0, 0) : new Thickness(0),
            },
        };
    }

    private IEnumerable<Button> BuildButtons(MessageBoxButton buttons)
    {
        return buttons switch
        {
            MessageBoxButton.OKCancel =>
            [
                BuildButton("Cancel", MessageBoxResult.Cancel, primary: false),
                BuildButton("OK", MessageBoxResult.OK, primary: true),
            ],
            MessageBoxButton.YesNo =>
            [
                BuildButton("No", MessageBoxResult.No, primary: false),
                BuildButton("Yes", MessageBoxResult.Yes, primary: true),
            ],
            MessageBoxButton.YesNoCancel =>
            [
                BuildButton("Cancel", MessageBoxResult.Cancel, primary: false),
                BuildButton("No", MessageBoxResult.No, primary: false),
                BuildButton("Yes", MessageBoxResult.Yes, primary: true),
            ],
            _ => [BuildButton("OK", MessageBoxResult.OK, primary: true)],
        };
    }

    private Button BuildButton(string label, MessageBoxResult result, bool primary)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 92,
            Height = 36,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(primary ? Color.FromRgb(15, 108, 189) : Color.FromRgb(255, 255, 255)),
            Foreground = new SolidColorBrush(primary ? Colors.White : Color.FromRgb(52, 64, 84)),
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(15, 108, 189) : Color.FromRgb(208, 213, 221)),
            BorderThickness = new Thickness(1),
            IsDefault = result == _defaultResult,
            IsCancel = result == MessageBoxResult.Cancel,
        };
        button.Click += (_, _) =>
        {
            Result = result;
            Close();
        };
        return button;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Result == MessageBoxResult.None)
            Result = _safeCloseResult;
    }

    private static MessageBoxResult DefaultResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
        _ => MessageBoxResult.OK,
    };

    private static MessageBoxResult SafeCloseResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.None,
    };
}

public static class QuizResolveProgressEstimator
{
    public const int InitialPercent = 5;
    public const int RunningCapPercent = 94;

    public static int Estimate(TimeSpan elapsed)
    {
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        var range = RunningCapPercent - InitialPercent;
        var estimate = InitialPercent + (range * (1 - Math.Exp(-seconds / 45.0)));
        return Math.Clamp((int)Math.Round(estimate), InitialPercent, RunningCapPercent);
    }

    public static string StageFor(int percent) => Math.Clamp(percent, 0, 100) switch
    {
        < 25 => "Preparing quiz export…",
        < 55 => "Processing quiz media…",
        < 80 => "Rendering and packaging…",
        < 95 => "Finalizing Resolve package…",
        _ => "Quiz export complete",
    };
}

internal static class QuizResolveProgressCoordinator
{
    private static readonly object Sync = new();
    private static ProgressSession? _session;

    public static bool IsActive
    {
        get
        {
            lock (Sync)
                return _session is not null;
        }
    }

    public static void Begin(Window owner)
    {
        lock (Sync)
        {
            if (_session is not null)
                return;

            _session = new ProgressSession(owner);
            _session.Start();
        }
    }

    public static void Pause()
    {
        lock (Sync)
            _session?.Pause();
    }

    public static void Resume()
    {
        lock (Sync)
            _session?.Resume();
    }

    public static void Complete() => End(complete: true);

    public static void Cancel() => End(complete: false);

    private static void End(bool complete)
    {
        ProgressSession? session;
        lock (Sync)
        {
            session = _session;
            _session = null;
        }

        session?.Stop(complete);
    }

    private sealed class ProgressSession
    {
        private readonly IntPtr _ownerHandle;
        private readonly double _left;
        private readonly double _top;
        private readonly Thread _thread;
        private volatile bool _paused;
        private volatile bool _stopRequested;
        private volatile bool _completeRequested;
        private Dispatcher? _dispatcher;
        private QuizResolveProgressWindow? _window;

        public ProgressSession(Window owner)
        {
            try
            {
                _ownerHandle = new WindowInteropHelper(owner).Handle;
            }
            catch
            {
                _ownerHandle = IntPtr.Zero;
            }

            const double width = 520;
            const double height = 250;
            _left = owner.Left + Math.Max(0, (owner.ActualWidth - width) / 2);
            _top = owner.Top + Math.Max(0, (owner.ActualHeight - height) / 2);
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "Quiz Resolve Progress UI",
            };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public void Start() => _thread.Start();

        public void Pause()
        {
            _paused = true;
            Dispatch(() => _window?.Hide());
        }

        public void Resume()
        {
            if (_stopRequested)
                return;

            _paused = false;
            Dispatch(() =>
            {
                if (_window is null)
                    return;
                _window.Show();
                _window.Activate();
            });
        }

        public void Stop(bool complete)
        {
            _completeRequested = complete;
            _stopRequested = true;
            Dispatch(() => FinishWindow(complete));
        }

        private void ThreadMain()
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new QuizResolveProgressWindow(_ownerHandle, _left, _top);
                _window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                _window.Show();

                if (_paused)
                    _window.Hide();

                if (_stopRequested)
                {
                    FinishWindow(_completeRequested);
                    if (_window.IsVisible)
                        Dispatcher.Run();
                    return;
                }

                Dispatcher.Run();
            }
            catch
            {
            }
        }

        private void FinishWindow(bool complete)
        {
            if (_window is null)
                return;

            if (!complete)
            {
                _window.Close();
                return;
            }

            _window.SetComplete();
            var timer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(80),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _window?.Close();
            };
            timer.Start();
        }

        private void Dispatch(Action action)
        {
            var dispatcher = _dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;
            dispatcher.BeginInvoke(action, DispatcherPriority.Send);
        }
    }
}

internal sealed class QuizResolveProgressWindow : Window
{
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _percentText;
    private readonly TextBlock _stageText;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public QuizResolveProgressWindow(IntPtr ownerHandle, double left, double top)
    {
        Title = "Create Resolve Quiz";
        Width = 520;
        Height = 250;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = double.IsFinite(left) && double.IsFinite(top)
            ? WindowStartupLocation.Manual
            : WindowStartupLocation.CenterScreen;
        if (WindowStartupLocation == WindowStartupLocation.Manual)
        {
            Left = left;
            Top = top;
        }

        SourceInitialized += (_, _) =>
        {
            if (ownerHandle != IntPtr.Zero)
                new WindowInteropHelper(this).Owner = ownerHandle;
        };

        var shell = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(25, 22, 25, 22),
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Opacity = 0.22,
                Color = Colors.Black,
            },
        };
        Content = shell;

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Creating Resolve quiz",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "The percentage is estimated and continues updating while cards and media are being rendered.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 18),
        });

        var progressHeader = new Grid();
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.Children.Add(progressHeader);

        _stageText = new TextBlock
        {
            Text = QuizResolveProgressEstimator.StageFor(QuizResolveProgressEstimator.InitialPercent),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)),
        };
        progressHeader.Children.Add(_stageText);

        _percentText = new TextBlock
        {
            Text = $"{QuizResolveProgressEstimator.InitialPercent}%",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
        };
        Grid.SetColumn(_percentText, 1);
        progressHeader.Children.Add(_percentText);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = QuizResolveProgressEstimator.InitialPercent,
            Height = 10,
            Margin = new Thickness(0, 9, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(234, 236, 240)),
            Foreground = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderThickness = new Thickness(0),
        };
        stack.Children.Add(_progressBar);

        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _timer.Tick += (_, _) => UpdateEstimatedProgress();
        _timer.Start();
    }

    public void SetComplete()
    {
        _timer.Stop();
        SetProgress(100);
    }

    private void UpdateEstimatedProgress() => SetProgress(QuizResolveProgressEstimator.Estimate(DateTime.UtcNow - _startedUtc));

    private void SetProgress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        _progressBar.Value = percent;
        _percentText.Text = $"{percent}%";
        _stageText.Text = QuizResolveProgressEstimator.StageFor(percent);
    }
}
