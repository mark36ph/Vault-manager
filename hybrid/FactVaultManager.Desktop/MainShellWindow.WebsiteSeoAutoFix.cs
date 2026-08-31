using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _websiteSeoAutoFixInitialized;
    private bool _websiteSeoAutoFixSelectionHooked;
    private DispatcherTimer? _websiteSeoAutoFixTimer;
    private Button? _websiteSeoAuditFixButton;
    private WebsiteSeoAutoFixProposal? _websiteSeoAuditFixProposal;

    public void InitializeWebsiteSeoAutoFixButton()
    {
        if (_websiteSeoAutoFixInitialized) return;
        _websiteSeoAutoFixInitialized = true;

        _websiteSeoAutoFixTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _websiteSeoAutoFixTimer.Tick += (_, _) => EnsureWebsiteSeoAutoFixButton();
        _websiteSeoAutoFixTimer.Start();
        Closed += (_, _) => _websiteSeoAutoFixTimer?.Stop();

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EnsureWebsiteSeoAutoFixButton));
    }

    private void EnsureWebsiteSeoAutoFixButton()
    {
        if (_websiteSeoAuditFixButton?.Parent is not null)
        {
            HookWebsiteSeoAutoFixSelection();
            _websiteSeoAutoFixTimer?.Stop();
            return;
        }

        if (_websiteSeoAuditEditButton?.Parent is not StackPanel actions) return;

        _websiteSeoAuditFixButton = new Button
        {
            Content = "Fix SEO",
            MinWidth = 96,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false,
            ToolTip = "Select an SEO warning that can be corrected safely by search/social metadata changes.",
        };
        _websiteSeoAuditFixButton.Click += async (_, _) => await FixSelectedWebsiteSeoAsync();

        var editIndex = actions.Children.IndexOf(_websiteSeoAuditEditButton);
        actions.Children.Insert(editIndex >= 0 ? editIndex : actions.Children.Count, _websiteSeoAuditFixButton);
        HookWebsiteSeoAutoFixSelection();
        UpdateWebsiteSeoAutoFixState();
        _websiteSeoAutoFixTimer?.Stop();
    }

    private void HookWebsiteSeoAutoFixSelection()
    {
        if (_websiteSeoAutoFixSelectionHooked || _websiteSeoAuditGrid is null) return;
        _websiteSeoAutoFixSelectionHooked = true;
        _websiteSeoAuditGrid.SelectionChanged += (_, _) => UpdateWebsiteSeoAutoFixState();
        UpdateWebsiteSeoAutoFixState();
    }

    private void UpdateWebsiteSeoAutoFixState()
    {
        if (_websiteSeoAuditFixButton is null) return;

        var row = _websiteSeoAuditGrid?.SelectedItem as WebsiteSeoAuditRow;
        _websiteSeoAuditFixProposal = row is null
            ? null
            : FactburstWebsiteSeoAutoFix.Create(row, _websiteSeoAuditQuizzes);

        _websiteSeoAuditFixButton.IsEnabled = _websiteSeoAuditFixProposal?.CanApply == true;
        _websiteSeoAuditFixButton.ToolTip = row is null
            ? "Select an SEO warning to see whether it can be fixed automatically."
            : _websiteSeoAuditFixProposal?.CanApply == true
                ? "Generate a safe correction, preview the before/after SEO metadata, then apply it after approval."
                : _websiteSeoAuditFixProposal?.Summary ?? "This finding needs manual editing.";
    }

    private async Task FixSelectedWebsiteSeoAsync()
    {
        if (_websiteSeoAuditGrid?.SelectedItem is not WebsiteSeoAuditRow row) return;
        const string title = "Fix Website SEO";

        var proposal = FactburstWebsiteSeoAutoFix.Create(row, _websiteSeoAuditQuizzes);
        _websiteSeoAuditFixProposal = proposal;
        if (!proposal.CanApply)
        {
            MessageBox.Show(
                this,
                proposal.Summary + "\n\nUse Edit selected to make the required change manually.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            UpdateWebsiteSeoAutoFixState();
            return;
        }

        var tracker = FactburstTrackerSettingsStore.Load(_data.SettingsPath);
        if (!tracker.IsConfigured)
        {
            MessageBox.Show(this, "Configure Settings → Link Tracker first.", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WebsiteSeoAutoFixPreviewDialog(row, proposal) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (_websiteSeoAuditFixButton is not null) _websiteSeoAuditFixButton.IsEnabled = false;
            if (_websiteSeoAuditEditButton is not null) _websiteSeoAuditEditButton.IsEnabled = false;
            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = $"Applying SEO fix for {row.Quiz}…";

            using var client = new FactburstWebsiteSeoAdminClient();
            await client.UpdateAsync(tracker.BaseUrl, tracker.ApiKey, row.Slug, proposal.After);
            await RefreshWebsiteSeoAuditAsync(false);

            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = $"SEO fixed for {row.Quiz}. The full catalogue audit has been refreshed and the next finding is selected automatically.";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            if (_websiteSeoAuditStatusText is not null)
                _websiteSeoAuditStatusText.Text = "SEO fix failed: " + error.Message;
        }
        finally
        {
            UpdateWebsiteSeoAuditSelection();
            UpdateWebsiteSeoAutoFixState();
        }
    }
}

public sealed class WebsiteSeoAutoFixPreviewDialog : Window
{
    public WebsiteSeoAutoFixPreviewDialog(WebsiteSeoAuditRow row, WebsiteSeoAutoFixProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(proposal);

        Title = $"Fix SEO • {row.Quiz}";
        Width = 780;
        Height = 680;
        MinWidth = 650;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        scroll.Content = stack;
        root.Children.Add(scroll);

        stack.Children.Add(new TextBlock
        {
            Text = "Review SEO fix",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Nothing is saved until you press Apply fix. The quiz title, questions, category, published slug and URL are not changed.",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        });

        var findingCard = Card();
        var finding = new StackPanel();
        findingCard.Child = finding;
        finding.Children.Add(Label("WHY THIS QUIZ WAS FLAGGED"));
        finding.Children.Add(new TextBlock
        {
            Text = row.Issues,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            LineHeight = 20,
        });
        finding.Children.Add(new TextBlock
        {
            Text = proposal.Summary,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61)),
            Margin = new Thickness(0, 10, 0, 0),
        });
        stack.Children.Add(findingCard);

        stack.Children.Add(new TextBlock
        {
            Text = "Proposed changes",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
            Margin = new Thickness(2, 18, 2, 8),
        });

        foreach (var change in proposal.Changes)
            stack.Children.Add(ChangeCard(change));

        stack.Children.Add(new TextBlock
        {
            Text = "The app simulated these values against the complete website quiz inventory before enabling this fix. Applying them should clear this warning; the full SEO audit runs again immediately after saving.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(2, 12, 2, 0),
        });

        var buttons = new Grid { Background = Brushes.White };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            MinHeight = 36,
            Margin = new Thickness(0, 12, 8, 12),
            IsCancel = true,
        };
        Grid.SetColumn(cancel, 1);
        buttons.Children.Add(cancel);

        var apply = new Button
        {
            Content = "Apply fix",
            MinWidth = 108,
            MinHeight = 36,
            Margin = new Thickness(0, 12, 24, 12),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189)),
            Foreground = Brushes.White,
        };
        apply.Click += (_, _) => DialogResult = true;
        Grid.SetColumn(apply, 2);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
    }

    private static Border ChangeCard(WebsiteSeoAutoFixChange change)
    {
        var card = Card(new Thickness(0, 0, 0, 10));
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(Label(change.Field.ToUpperInvariant()));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var before = ValueColumn("CURRENT", change.Before, new SolidColorBrush(Color.FromRgb(102, 112, 133)));
        columns.Children.Add(before);
        var after = ValueColumn("PROPOSED", change.After, new SolidColorBrush(Color.FromRgb(21, 128, 61)));
        Grid.SetColumn(after, 2);
        columns.Children.Add(after);
        stack.Children.Add(columns);
        return card;
    }

    private static StackPanel ValueColumn(string label, string value, Brush accent)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            LineHeight = 19,
        });
        return stack;
    }

    private static Border Card(Thickness? margin = null) => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 14, 16, 14),
        Margin = margin ?? new Thickness(0),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
        Margin = new Thickness(0, 0, 0, 5),
    };
}
