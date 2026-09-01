using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public sealed record LibraryReleasePlatformStatusRow(
    string YouTube,
    string Facebook,
    string Instagram);

public static class LibraryReleasePlatformStatusPlanner
{
    public static string SocialPromoStatus(
        IEnumerable<PublicationStateEntry> publications,
        string platform,
        bool youtubePublic,
        bool promoFileReady,
        bool metadataUploaded)
    {
        ArgumentNullException.ThrowIfNull(publications);
        if (metadataUploaded)
            return "Posted";

        var entry = publications
            .Where(item => string.Equals(item.ContentKind, PublicationContentKind.Promo, StringComparison.Ordinal))
            .Where(item => string.Equals(item.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        if (entry?.HasIssue == true)
            return "Needs upload";
        if (entry?.HasRemotePublication == true)
            return "Posted";
        if (!youtubePublic)
            return "Waiting";
        return promoFileReady ? "Needs upload" : "Promo missing";
    }
}

public partial class MainShellWindow
{
    private bool _libraryPlatformStatusFixInitialized;
    private bool _libraryPlatformLayoutApplied;
    private bool _libraryStableLayoutLocked;
    private DispatcherTimer? _libraryPlatformStatusFixTimer;
    private readonly Dictionary<int, LibraryReleasePlatformStatusRow> _libraryReleasePlatformStatusByHistoryId = [];

    public void InitializeLibraryPlatformStatusFix()
    {
        if (_libraryPlatformStatusFixInitialized)
            return;

        _libraryPlatformStatusFixInitialized = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyLibraryPlatformStatusFix));

        _libraryPlatformStatusFixTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _libraryPlatformStatusFixTimer.Tick += (_, _) =>
        {
            if (MainTabs.SelectedIndex != _quizHistoryTabIndex)
                return;

            if (!_libraryPlatformLayoutApplied)
            {
                ApplyLibraryPlatformStatusFix();
                return;
            }

            RefreshLibraryReleasePlatformStatusSnapshot();
        };
        _libraryPlatformStatusFixTimer.Start();
        Closed += (_, _) => _libraryPlatformStatusFixTimer?.Stop();

        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (!ReferenceEquals(eventArgs.OriginalSource, MainTabs) || MainTabs.SelectedIndex != _quizHistoryTabIndex)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RefreshLibraryReleasePlatformStatusSnapshot));
        };
    }

    private void ApplyLibraryPlatformStatusFix()
    {
        if (_quizHistoryGrid is null)
            return;

        // Build 126 had its own 30-second refresh timer. Build 128 owns the Library platform
        // refresh path, so stop the older timer rather than letting two refresh loops repaint the grid.
        _libraryPublicationStatusRefreshTimer?.Stop();

        // Let the older general cleanup establish the base widths/styles once, then freeze the
        // Library layout so its one-second timer cannot keep re-measuring the table.
        if (!_libraryStableLayoutLocked)
            ApplyQuizHistoryTableCleanup();

        var instagramPromo = _quizHistoryGrid.Columns
            .FirstOrDefault(column => string.Equals(column.Header?.ToString(), "IG promo", StringComparison.Ordinal));
        if (instagramPromo is not null)
            _quizHistoryGrid.Columns.Remove(instagramPromo);

        var legacyStatus = _quizHistoryGrid.Columns
            .FirstOrDefault(column => string.Equals(column.Header?.ToString(), "Status", StringComparison.Ordinal));
        if (legacyStatus is not null)
            _quizHistoryGrid.Columns.Remove(legacyStatus);

        // YT keeps a descriptive value such as Scheduled/Public. FB and IG are rebound once by
        // the symbol layer and are never rebound by the background status timer.
        RebindLibraryPlatformColumn("YT", LibraryReleasePlatformStatusField.YouTube, 90);
        SetLibraryColumnWidth("Stage", 128);
        SetLibraryColumnWidth("Next action", 180);
        SetLibraryColumnWidth("Upload date", 108);

        _libraryPlatformLayoutApplied = true;
        _libraryStableLayoutLocked = true;

        ApplyLibraryPlatformSymbolFix();
        RefreshLibraryReleasePlatformStatusSnapshot();
    }

    private void RebindLibraryPlatformColumn(
        string header,
        LibraryReleasePlatformStatusField field,
        double width)
    {
        if (_quizHistoryGrid is null)
            return;
        if (_quizHistoryGrid.Columns.FirstOrDefault(column =>
                string.Equals(column.Header?.ToString(), header, StringComparison.Ordinal)) is not DataGridTextColumn column)
            return;

        column.Binding = new Binding
        {
            Converter = new LibraryReleasePlatformStatusConverter(this, field),
        };
        column.Width = new DataGridLength(width);
        column.CanUserSort = false;
    }

    private void SetLibraryColumnWidth(string header, double width)
    {
        if (_quizHistoryGrid is null)
            return;
        var column = _quizHistoryGrid.Columns.FirstOrDefault(value =>
            string.Equals(value.Header?.ToString(), header, StringComparison.Ordinal));
        if (column is not null && column.Width != new DataGridLength(width))
            column.Width = new DataGridLength(width);
    }

    private void RefreshLibraryReleasePlatformStatusSnapshot()
    {
        if (_quizHistoryGrid?.ItemsSource is not IEnumerable<QuizHistorySummary> source)
            return;

        // The older Build 126 timer may have been created after our startup callback. Stop it on
        // every data pass as well so it cannot reappear as a second layout/repaint loop.
        _libraryPublicationStatusRefreshTimer?.Stop();

        try
        {
            var histories = source.ToList();
            IReadOnlyList<PublicationStateEntry> publications;
            try { publications = _data.PublicationState.List(); }
            catch (Exception error)
            {
                Debug.WriteLine("Library release platform state: " + error.Message);
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

            var next = new Dictionary<int, LibraryReleasePlatformStatusRow>();
            foreach (var history in histories)
            {
                var entries = byHistory.TryGetValue(history.Id, out var stored) ? stored : [];
                var verifiedYouTubePublic = verifiedPublicIds.Contains(history.Id) ||
                    InstagramPromoFollowupPlanner.IsVerifiedYouTubePublic(history.Id, state, entries);

                if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
                {
                    next[history.Id] = new LibraryReleasePlatformStatusRow(
                        LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.YouTube, verifiedYouTubePublic),
                        LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Facebook),
                        LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.Instagram));
                    continue;
                }

                var promoFileReady = false;
                var facebookUploaded = false;
                var instagramUploaded = false;
                try
                {
                    promoFileReady = history.ProjectFolder.Trim().Length > 0 &&
                                     Directory.Exists(history.ProjectFolder) &&
                                     QuizPromoShortPaths.FindExisting(history.ProjectFolder) is not null;
                    facebookUploaded = QuizPromoShortSocialPublicationStore.LoadFacebook(history.ProjectFolder) is not null;
                    instagramUploaded = QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null;
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"Library promo status #{history.Id}: {error.Message}");
                }

                next[history.Id] = new LibraryReleasePlatformStatusRow(
                    LibraryPublicationStatusPlanner.FullQuizStatus(entries, PublicationPlatform.YouTube, verifiedYouTubePublic),
                    LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                        entries,
                        PublicationPlatform.Facebook,
                        verifiedYouTubePublic,
                        promoFileReady,
                        facebookUploaded),
                    LibraryReleasePlatformStatusPlanner.SocialPromoStatus(
                        entries,
                        PublicationPlatform.Instagram,
                        verifiedYouTubePublic,
                        promoFileReady,
                        instagramUploaded));
            }

            var changed = next.Count != _libraryReleasePlatformStatusByHistoryId.Count ||
                          next.Any(pair =>
                              !_libraryReleasePlatformStatusByHistoryId.TryGetValue(pair.Key, out var current) ||
                              current != pair.Value);
            if (!changed)
                return;

            _libraryReleasePlatformStatusByHistoryId.Clear();
            foreach (var pair in next)
                _libraryReleasePlatformStatusByHistoryId[pair.Key] = pair.Value;

            _quizHistoryGrid.Items.Refresh();
        }
        catch (Exception error)
        {
            Debug.WriteLine("Library release platform status refresh: " + error);
        }
    }

    private LibraryReleasePlatformStatusRow ResolveLibraryReleasePlatformStatus(QuizHistorySummary history) =>
        _libraryReleasePlatformStatusByHistoryId.TryGetValue(history.Id, out var status)
            ? status
            : new LibraryReleasePlatformStatusRow("—", "—", "—");

    private enum LibraryReleasePlatformStatusField
    {
        YouTube,
        Facebook,
        Instagram,
    }

    private sealed class LibraryReleasePlatformStatusConverter(
        MainShellWindow owner,
        LibraryReleasePlatformStatusField field) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QuizHistorySummary history)
                return "—";
            var status = owner.ResolveLibraryReleasePlatformStatus(history);
            return field switch
            {
                LibraryReleasePlatformStatusField.YouTube => status.YouTube,
                LibraryReleasePlatformStatusField.Facebook => status.Facebook,
                LibraryReleasePlatformStatusField.Instagram => status.Instagram,
                _ => "—",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
