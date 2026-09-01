using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record LibraryPublicationStatusRow(
    string YouTube,
    string Facebook,
    string Instagram,
    string InstagramPromo);

public static class LibraryPublicationStatusPlanner
{
    public static string FullQuizStatus(
        IEnumerable<PublicationStateEntry> publications,
        string platform,
        bool verifiedPublic = false)
    {
        ArgumentNullException.ThrowIfNull(publications);
        if (verifiedPublic && string.Equals(platform, PublicationPlatform.YouTube, StringComparison.OrdinalIgnoreCase))
            return "Public";

        var entry = publications
            .Where(item => string.Equals(item.ContentKind, PublicationContentKind.Quiz, StringComparison.Ordinal))
            .Where(item => string.Equals(item.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        return Compact(entry);
    }

    public static string InstagramPromoStatus(
        IEnumerable<PublicationStateEntry> publications,
        bool verifiedYouTubePublic,
        bool promoFileReady,
        bool instagramMetadataUploaded)
    {
        ArgumentNullException.ThrowIfNull(publications);
        if (instagramMetadataUploaded)
            return "Posted";

        var entry = publications
            .Where(item => string.Equals(item.ContentKind, PublicationContentKind.Promo, StringComparison.Ordinal))
            .Where(item => string.Equals(item.Platform, PublicationPlatform.Instagram, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (entry?.HasIssue == true)
            return "Needs upload";
        if (entry?.HasRemotePublication == true)
            return entry.State == PublicationStateStatus.Published ? "Posted" : "Uploaded";
        if (!verifiedYouTubePublic)
            return "Waiting";
        return promoFileReady ? "Needs upload" : "Promo missing";
    }

    private static string Compact(PublicationStateEntry? entry)
    {
        if (entry is null)
            return "—";
        if (entry.HasIssue && entry.State == PublicationStateStatus.Failed)
            return "Failed";
        return entry.State switch
        {
            PublicationStateStatus.InProgress => "Uploading",
            PublicationStateStatus.Scheduled => "Scheduled",
            PublicationStateStatus.Published => "Public",
            PublicationStateStatus.Uploaded => entry.Visibility.Trim().ToLowerInvariant() switch
            {
                "private" => "Private",
                "unlisted" => "Unlisted",
                "public" => "Public",
                _ => "Uploaded",
            },
            PublicationStateStatus.Failed => "Failed",
            _ => "—",
        };
    }
}

public partial class MainShellWindow
{
    private bool _libraryPublicationStatusInitialized;
    private int _libraryPublicationStatusAttempts;
    private DispatcherTimer? _libraryPublicationStatusRetryTimer;
    private DispatcherTimer? _libraryPublicationStatusRefreshTimer;
    private readonly HashSet<int> _libraryPublicationReconciledIds = [];
    private readonly Dictionary<int, LibraryPublicationStatusRow> _libraryPublicationStatusByHistoryId = [];

    public void InitializeLibraryPublicationStatusUi()
    {
        if (_libraryPublicationStatusInitialized)
            return;

        _libraryPublicationStatusInitialized = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(BuildLibraryPublicationStatusUi));
    }

    private void BuildLibraryPublicationStatusUi()
    {
        if (_quizHistoryGrid is null)
        {
            RetryLibraryPublicationStatusUi();
            return;
        }

        if (!_quizHistoryGrid.Columns.Any(column => string.Equals(column.Header?.ToString(), "YT", StringComparison.Ordinal)))
        {
            var insertAt = _quizHistoryGrid.Columns
                .Select((column, index) => new { column, index })
                .FirstOrDefault(item => string.Equals(item.column.Header?.ToString(), "Next action", StringComparison.Ordinal))?.index + 1
                ?? Math.Min(5, _quizHistoryGrid.Columns.Count);

            _quizHistoryGrid.Columns.Insert(insertAt++, StatusColumn("YT", LibraryPublicationStatusField.YouTube, 82));
            _quizHistoryGrid.Columns.Insert(insertAt++, StatusColumn("FB", LibraryPublicationStatusField.Facebook, 82));
            _quizHistoryGrid.Columns.Insert(insertAt++, StatusColumn("IG", LibraryPublicationStatusField.Instagram, 82));
            _quizHistoryGrid.Columns.Insert(insertAt, StatusColumn("IG promo", LibraryPublicationStatusField.InstagramPromo, 112));
        }

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(DataGrid));
        descriptor?.AddValueChanged(
            _quizHistoryGrid,
            (_, _) => Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(RefreshLibraryPublicationStatusSnapshot)));

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) || MainTabs.SelectedIndex != _quizHistoryTabIndex)
                return;
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(RefreshLibraryPublicationStatusSnapshot));
        };

        _libraryPublicationStatusRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _libraryPublicationStatusRefreshTimer.Tick += (_, _) =>
        {
            if (MainTabs.SelectedIndex == _quizHistoryTabIndex)
                RefreshLibraryPublicationStatusSnapshot();
        };
        _libraryPublicationStatusRefreshTimer.Start();
        Closed += (_, _) => _libraryPublicationStatusRefreshTimer?.Stop();

        RefreshLibraryPublicationStatusSnapshot();
    }

    private DataGridTextColumn StatusColumn(
        string header,
        LibraryPublicationStatusField field,
        double width) =>
        new()
        {
            Header = header,
            Binding = new Binding
            {
                Converter = new LibraryPublicationStatusConverter(this, field),
            },
            CanUserSort = false,
            Width = new DataGridLength(width),
        };

    private void RetryLibraryPublicationStatusUi()
    {
        if (++_libraryPublicationStatusAttempts >= 60)
            return;

        _libraryPublicationStatusRetryTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(125),
        };
        _libraryPublicationStatusRetryTimer.Tick -= LibraryPublicationStatusRetryTimer_Tick;
        _libraryPublicationStatusRetryTimer.Tick += LibraryPublicationStatusRetryTimer_Tick;
        _libraryPublicationStatusRetryTimer.Start();
    }

    private void LibraryPublicationStatusRetryTimer_Tick(object? sender, EventArgs e)
    {
        _libraryPublicationStatusRetryTimer?.Stop();
        BuildLibraryPublicationStatusUi();
    }

    private void RefreshLibraryPublicationStatusSnapshot()
    {
        if (_quizHistoryGrid?.ItemsSource is not IEnumerable<QuizHistorySummary> source)
            return;

        try
        {
            var histories = source.ToList();
            var publicationState = _data.PublicationState;
            foreach (var history in histories)
            {
                if (!_libraryPublicationReconciledIds.Add(history.Id))
                    continue;
                try { publicationState.Reconcile(history.Id, history.ProjectFolder); }
                catch (Exception error) { Debug.WriteLine($"Library publication reconcile #{history.Id}: {error.Message}"); }
            }

            IReadOnlyList<PublicationStateEntry> publications;
            try { publications = publicationState.List(); }
            catch (Exception error)
            {
                Debug.WriteLine("Library publication state: " + error.Message);
                publications = [];
            }

            var byHistory = publications
                .GroupBy(entry => entry.HistoryId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<PublicationStateEntry>)group.ToList());
            var state = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            var verifiedPublicIds = state.PostReleaseAudits
                .Where(record => record.IsPublic)
                .GroupBy(record => record.HistoryId)
                .Select(group => group.OrderByDescending(record => record.CheckedAtUtc).First().HistoryId)
                .ToHashSet();

            _libraryPublicationStatusByHistoryId.Clear();
            foreach (var history in histories)
            {
                var entries = byHistory.TryGetValue(history.Id, out var stored) ? stored : [];
                var verifiedYouTubePublic = verifiedPublicIds.Contains(history.Id) ||
                    InstagramPromoFollowupPlanner.IsVerifiedYouTubePublic(history.Id, state, entries);
                var instagramUploaded = false;
                var promoFileReady = false;
                try
                {
                    instagramUploaded = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null;
                    promoFileReady = history.ProjectFolder.Trim().Length > 0 &&
                                     Directory.Exists(history.ProjectFolder) &&
                                     QuizPromoShortPaths.FindExisting(history.ProjectFolder) is not null;
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"Library Instagram promo status #{history.Id}: {error.Message}");
                }

                _libraryPublicationStatusByHistoryId[history.Id] = new LibraryPublicationStatusRow(
                    LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.YouTube, verifiedYouTubePublic),
                    LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Facebook),
                    LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Instagram),
                    LibraryPublicationStatusPlanner.InstagramPromoStatus(
                        entries,
                        verifiedYouTubePublic,
                        promoFileReady,
                        instagramUploaded));
            }

            _quizHistoryGrid.Items.Refresh();
        }
        catch (Exception error)
        {
            Debug.WriteLine("Library publication status refresh: " + error);
        }
    }

    private LibraryPublicationStatusRow ResolveLibraryPublicationStatus(QuizHistorySummary history) =>
        _libraryPublicationStatusByHistoryId.TryGetValue(history.Id, out var status)
            ? status
            : new LibraryPublicationStatusRow("—", "—", "—", "Waiting");

    private enum LibraryPublicationStatusField
    {
        YouTube,
        Facebook,
        Instagram,
        InstagramPromo,
    }

    private sealed class LibraryPublicationStatusConverter(
        MainShellWindow owner,
        LibraryPublicationStatusField field) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QuizHistorySummary history)
                return "—";
            var status = owner.ResolveLibraryPublicationStatus(history);
            return field switch
            {
                LibraryPublicationStatusField.YouTube => status.YouTube,
                LibraryPublicationStatusField.Facebook => status.Facebook,
                LibraryPublicationStatusField.Instagram => status.Instagram,
                LibraryPublicationStatusField.InstagramPromo => status.InstagramPromo,
                _ => "—",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
