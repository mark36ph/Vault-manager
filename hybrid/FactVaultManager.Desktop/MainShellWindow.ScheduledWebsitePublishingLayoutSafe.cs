using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _scheduledWebsitePublishingLayoutSafeInitialized;
    private int _scheduledWebsitePublishingLayoutSafeAttempts;

    public void InitializeScheduledWebsitePublishingLayoutSafeForApp()
    {
        if (_scheduledWebsitePublishingLayoutSafeInitialized) return;
        _scheduledWebsitePublishingLayoutSafeInitialized = true;

        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingLayoutSafeButton));
        MainTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingLayoutSafeButton));
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(EnsureScheduledWebsitePublishingLayoutSafeButton));
    }

    private void EnsureScheduledWebsitePublishingLayoutSafeButton()
    {
        if (_scheduledWebsitePublishingButton?.Parent is not null) return;
        if (_scheduledReadinessStatusText?.Parent is not Grid pageRoot)
        {
            RetryScheduledWebsitePublishingLayoutSafeButton();
            return;
        }

        var button = new Button
        {
            Content = "Prepare website",
            Tag = ScheduledWebsitePublishingButtonTag,
            MinWidth = 142,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "Copy accessible scheduled quizzes to Cloudflare now. They stay hidden until their scheduled release time. Unavailable project folders are skipped safely and can be retried later.",
        };
        StyleQuizHistoryButton(button, Color.FromRgb(73, 190, 255));
        button.Click += async (_, _) => await PrepareScheduledWebsiteQuizzesAsync(button);

        _scheduledReadinessStatusText.Margin = new Thickness(0, 10, 164, 0);
        Grid.SetRow(button, 3);
        pageRoot.Children.Add(button);
        _scheduledWebsitePublishingButton = button;
    }

    private void RetryScheduledWebsitePublishingLayoutSafeButton()
    {
        if (++_scheduledWebsitePublishingLayoutSafeAttempts >= 40) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EnsureScheduledWebsitePublishingLayoutSafeButton();
        };
        timer.Start();
    }
}
