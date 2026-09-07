using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private void ShowDailyQuizScheduler()
    {
        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Connect the website in Settings → Website & Link Tracker first. The scheduler uses the same TRACKER_API_KEY.", "Daily Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new DailyQuizSchedulerWindow(tracker.BaseUrl, tracker.ApiKey)
        {
            Owner = this,
        };
        window.ShowDialog();
    }
}

internal sealed class DailyQuizSchedulerWindow : Window
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly DatePicker _datePicker;
    private readonly ComboBox _quizPicker;
    private readonly ListBox _scheduleList;
    private readonly TextBlock _status;
    private readonly Button _saveButton;
    private readonly Button _clearButton;
    private IReadOnlyList<FactburstDailyQuizAssignment> _assignments = [];

    public DailyQuizSchedulerWindow(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl;
        _apiKey = apiKey;
        Title = "Daily Quiz Scheduler";
        Width = 820;
        Height = 650;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Daily Quiz",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Choose which published quiz appears as the Daily Challenge on each date. You can schedule several days ahead and replace an assignment before it goes live.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 4, 0, 18),
        });
        Grid.SetRow(titleStack, 0);
        root.Children.Add(titleStack);

        var editor = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
        };
        var editorGrid = new Grid();
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.Child = editorGrid;
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);

        editorGrid.Children.Add(Label("Date", 0, 0));
        _datePicker = new DatePicker { SelectedDate = DateTime.Today, MinWidth = 140, Height = 34 };
        Grid.SetColumn(_datePicker, 1);
        editorGrid.Children.Add(_datePicker);

        editorGrid.Children.Add(Label("Quiz", 0, 1));
        _quizPicker = new ComboBox { MinWidth = 380, Height = 34, DisplayMemberPath = nameof(FactburstDailyQuizOption.Title) };
        Grid.SetRow(_quizPicker, 1);
        Grid.SetColumn(_quizPicker, 1);
        editorGrid.Children.Add(_quizPicker);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(buttons, 2);
        Grid.SetRowSpan(buttons, 2);
        editorGrid.Children.Add(buttons);
        _saveButton = new Button { Content = "Set Daily Quiz", MinWidth = 128, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        _saveButton.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(_saveButton);
        _clearButton = new Button { Content = "Clear Date", MinWidth = 100, Height = 34 };
        _clearButton.Click += async (_, _) => await ClearAsync();
        buttons.Children.Add(_clearButton);

        var listPanel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(listPanel, 2);
        root.Children.Add(listPanel);
        listPanel.Children.Add(new TextBlock
        {
            Text = "Scheduled days",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        listPanel.Children.Add(new TextBlock
        {
            Text = "Showing the next two weeks. Unassigned days continue to use the site's automatic rotation.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 2, 0, 8),
        });
        _scheduleList = new ListBox { BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1) };
        _scheduleList.SelectionChanged += (_, _) => SelectScheduledDate();
        listPanel.Children.Add(_scheduleList);

        _status = new TextBlock
        {
            Text = "Loading…",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "Loading quizzes and the Daily Quiz schedule…");
        try
        {
            var from = DateTime.Today.ToString("yyyy-MM-dd");
            var to = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd");
            using var client = new FactburstDailyQuizAdminClient();
            var data = await client.FetchAsync(_baseUrl, _apiKey, from, to);
            var quizzes = data.Quizzes
                .Where(q => string.Equals(q.Status, "published", StringComparison.OrdinalIgnoreCase))
                .OrderBy(q => q.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _quizPicker.ItemsSource = quizzes;
            _assignments = data.Schedule;
            RenderSchedule();
            _status.Text = $"Loaded {quizzes.Count} published quizzes and {_assignments.Count} scheduled day(s).";
        }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task SaveAsync()
    {
        if (_datePicker.SelectedDate is not DateTime date)
        {
            _status.Text = "Choose a date.";
            return;
        }
        if (_quizPicker.SelectedItem is not FactburstDailyQuizOption quiz || quiz.Id <= 0)
        {
            _status.Text = "Choose a published quiz.";
            return;
        }

        SetBusy(true, "Saving Daily Quiz assignment…");
        try
        {
            using var client = new FactburstDailyQuizAdminClient();
            await client.SetAsync(_baseUrl, _apiKey, date.ToString("yyyy-MM-dd"), quiz.Id);
            _status.Text = $"Daily Quiz set to “{quiz.Title}” for {date:dddd, MMMM d, yyyy}.";
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task ClearAsync()
    {
        if (_datePicker.SelectedDate is not DateTime date)
        {
            _status.Text = "Choose a date.";
            return;
        }

        SetBusy(true, "Clearing Daily Quiz assignment…");
        try
        {
            using var client = new FactburstDailyQuizAdminClient();
            await client.ClearAsync(_baseUrl, _apiKey, date.ToString("yyyy-MM-dd"));
            _status.Text = $"The manual Daily Quiz assignment for {date:dddd, MMMM d, yyyy} was cleared. Automatic rotation will be used instead.";
            await RefreshAsync();
        }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void RenderSchedule()
    {
        _scheduleList.Items.Clear();
        foreach (var item in _assignments.OrderBy(x => x.DayKey, StringComparer.Ordinal))
            _scheduleList.Items.Add(new DailyScheduleDisplay(item));
    }

    private void SelectScheduledDate()
    {
        if (_scheduleList.SelectedItem is not DailyScheduleDisplay item) return;
        if (DateTime.TryParseExact(item.Assignment.DayKey, "yyyy-MM-dd", out var date))
            _datePicker.SelectedDate = date;
        for (var i = 0; i < _quizPicker.Items.Count; i++)
        {
            if (_quizPicker.Items[i] is FactburstDailyQuizOption quiz && quiz.Id == item.Assignment.QuizId)
            {
                _quizPicker.SelectedIndex = i;
                break;
            }
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _saveButton.IsEnabled = !busy;
        _clearButton.IsEnabled = !busy;
        _datePicker.IsEnabled = !busy;
        _quizPicker.IsEnabled = !busy;
        _status.Text = status;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private static TextBlock Label(string text, int column, int row)
    {
        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(label, column);
        Grid.SetRow(label, row);
        return label;
    }

    private sealed record DailyScheduleDisplay(FactburstDailyQuizAssignment Assignment)
    {
        public override string ToString() => $"{Assignment.DayKey}   •   {Assignment.Title}   •   {Assignment.Category}";
    }
}
