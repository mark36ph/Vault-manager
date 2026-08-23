using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace FactVaultManager.Desktop;

[Flags]
public enum SocialUploadDestination
{
    None = 0,
    YouTube = 1,
    Facebook = 2,
    Instagram = 4,
}

public static class SocialUploadQueuePlanner
{
    public static SocialUploadDestination RemainingDestinations(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var remaining = history.PublishedOnYouTube
            ? SocialUploadDestination.None
            : SocialUploadDestination.YouTube;
        if (!string.Equals(history.VideoType, "Short", StringComparison.Ordinal))
            return remaining;
        if (!history.PublishedOnFacebook) remaining |= SocialUploadDestination.Facebook;
        if (!history.PublishedOnInstagram) remaining |= SocialUploadDestination.Instagram;
        return remaining;
    }

    public static string Display(SocialUploadDestination destinations)
    {
        var names = new List<string>();
        if (destinations.HasFlag(SocialUploadDestination.YouTube)) names.Add("YouTube");
        if (destinations.HasFlag(SocialUploadDestination.Facebook)) names.Add("Facebook");
        if (destinations.HasFlag(SocialUploadDestination.Instagram)) names.Add("Instagram");
        return names.Count == 0 ? "Complete" : string.Join(" + ", names);
    }

    public static bool IsMetaAuthenticationError(string? message)
    {
        var value = (message ?? "").ToLowerInvariant();
        return value.Contains("error validating access token", StringComparison.Ordinal) ||
               value.Contains("session has expired", StringComparison.Ordinal) ||
               value.Contains("access token has expired", StringComparison.Ordinal) ||
               value.Contains("invalid oauth access token", StringComparison.Ordinal);
    }
}

public sealed record SocialUploadQueuePathMatch(int HistoryId, string VideoPath, string ThumbnailPath);

public static class SocialUploadQueuePathFinder
{
    public static IReadOnlyList<SocialUploadQueuePathMatch> FindMissingVideos(
        IEnumerable<QuizHistorySummary> histories,
        string searchRoot)
    {
        ArgumentNullException.ThrowIfNull(histories);
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return [];

        var pending = histories.ToList();
        if (pending.Count == 0) return [];

        var candidates = new Dictionary<int, List<(int Score, SocialUploadQueuePathMatch Match)>>();
        foreach (var folder in EnumerateDirectoriesSafely(searchRoot))
        {
            var video = SocialVideoUploadRules.FindLikelyRenderedVideo(folder);
            if (video is null) continue;
            var thumbnail = SocialVideoUploadRules.FindLikelyThumbnail(folder) ?? "";
            foreach (var history in pending)
            {
                var score = MatchScore(history, folder, video);
                if (score == 0) continue;
                if (!candidates.TryGetValue(history.Id, out var found))
                    candidates[history.Id] = found = [];
                found.Add((score, new SocialUploadQueuePathMatch(history.Id, video, thumbnail)));
            }
        }

        return candidates.Values
            .Select(found => found
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Match.VideoPath))
                .First().Match)
            .ToList();
    }

    private static int MatchScore(QuizHistorySummary history, string folder, string video)
    {
        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var searchable = Normalize(folderName + " " + Path.GetFileNameWithoutExtension(video));
        var identityScore = new[]
            {
                Normalize(history.SeriesName),
                Normalize(history.Categories),
                Normalize(history.Title),
            }
            .Where(identity => identity.Length >= 3)
            .Where(identity => ContainsPhrase(searchable, identity))
            .Select(identity => identity.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 10)
            .DefaultIfEmpty(0)
            .Max();
        if (identityScore == 0) return 0;

        var score = 100 + identityScore;
        if (string.Equals(Normalize(folderName), Normalize(StoredFolderName(history.ProjectFolder)),
                StringComparison.Ordinal))
            score += 50;
        if (folderName.Contains(history.EpisodeNumber.ToString("000"), StringComparison.Ordinal))
            score += 20;
        return score;
    }

    private static bool ContainsPhrase(string searchable, string identity) =>
        (" " + searchable + " ").Contains(" " + identity + " ", StringComparison.Ordinal);

    private static string Normalize(string? value)
    {
        var characters = (value ?? "").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string StoredFolderName(string? path) =>
        (path ?? "")
        .Trim()
        .TrimEnd('\\', '/')
        .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault() ?? "";

    private static IEnumerable<string> EnumerateDirectoriesSafely(string searchRoot)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(searchRoot));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
                    pending.Push(child);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}

public sealed class SocialUploadQueueItem : INotifyPropertyChanged
{
    private bool _include;
    private SocialUploadDestination _remaining;
    private string _status;

    public SocialUploadQueueItem(QuizHistorySummary history)
    {
        HistoryId = history.Id;
        Title = history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title;
        VideoType = history.VideoType;
        VideoPath = SocialVideoUploadRules.FindLikelyRenderedVideo(history.ProjectFolder) ?? "";
        ThumbnailPath = SocialVideoUploadRules.FindLikelyThumbnail(history.ProjectFolder) ?? "";
        _remaining = SocialUploadQueuePlanner.RemainingDestinations(history);
        _include = _remaining != SocialUploadDestination.None && VideoPath.Length > 0;
        _status = VideoPath.Length == 0 ? "Video file not found" : "Ready";
    }

    public int HistoryId { get; }
    public string Title { get; }
    public string VideoType { get; }
    public string VideoPath { get; private set; }
    public string ThumbnailPath { get; private set; }
    public string VideoFile => VideoPath.Length == 0 ? "Not found" : Path.GetFileName(VideoPath);
    public string RemainingDisplay => SocialUploadQueuePlanner.Display(_remaining);
    public SocialUploadDestination Remaining => _remaining;

    public bool Include
    {
        get => _include;
        set
        {
            if (_include == value) return;
            _include = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public void ApplyRemaining(SocialUploadDestination remaining)
    {
        _remaining = remaining;
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(RemainingDisplay));
        if (remaining == SocialUploadDestination.None) Include = false;
    }

    public void ApplyPaths(string videoPath, string thumbnailPath)
    {
        VideoPath = videoPath;
        ThumbnailPath = thumbnailPath;
        Status = "Ready";
        Include = _remaining != SocialUploadDestination.None;
        OnPropertyChanged(nameof(VideoPath));
        OnPropertyChanged(nameof(ThumbnailPath));
        OnPropertyChanged(nameof(VideoFile));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record SocialUploadQueueResult(
    IReadOnlyList<string> Completed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public partial class MainShellWindow
{
    private void ShowUploadQueueDialog()
    {
        var candidates = _data.GetQuizHistory()
            .Where(history => SocialUploadQueuePlanner.RemainingDestinations(history) != SocialUploadDestination.None)
            .Select(history => new SocialUploadQueueItem(history))
            .ToList();
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "Every quiz has already been published to its available platforms.",
                "Upload Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var items = new ObservableCollection<SocialUploadQueueItem>(candidates);
        var dialog = new Window
        {
            Title = "Quiz Upload Queue",
            Owner = this,
            Width = 1120,
            Height = 720,
            MinWidth = 900,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 13, 57)),
        };
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = "Upload Queue",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Selected quizzes upload one at a time. Completed platforms are skipped automatically when you retry.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        root.Children.Add(heading);

        var options = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (var index = 0; index < 6; index++)
            options.ColumnDefinitions.Add(new ColumnDefinition { Width = index == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        options.Children.Add(new TextBlock
        {
            Text = "YouTube privacy",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var privacy = new ComboBox { Width = 118, Height = 32, Margin = new Thickness(0, 0, 18, 0) };
        privacy.Items.Add("private");
        privacy.Items.Add("unlisted");
        privacy.Items.Add("public");
        privacy.SelectedItem = "private";
        Grid.SetColumn(privacy, 1);
        options.Children.Add(privacy);
        var notify = new CheckBox
        {
            Content = "Notify subscribers",
            IsChecked = true,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        };
        Grid.SetColumn(notify, 2);
        options.Children.Add(notify);
        var findMissing = new Button
        {
            Content = "Find missing videos",
            MinWidth = 142,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        StyleQuizHistoryButton(findMissing, Color.FromRgb(255, 202, 45));
        Grid.SetColumn(findMissing, 3);
        options.Children.Add(findMissing);
        var selectReady = new Button { Content = "Select ready", MinWidth = 104, MinHeight = 32 };
        StyleQuizHistoryButton(selectReady, Color.FromRgb(0, 204, 255));
        Grid.SetColumn(selectReady, 4);
        options.Children.Add(selectReady);
        var clear = new Button
        {
            Content = "Clear",
            MinWidth = 76,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        StyleQuizHistoryButton(clear, Color.FromRgb(204, 70, 255));
        Grid.SetColumn(clear, 5);
        options.Children.Add(clear);
        Grid.SetRow(options, 1);
        root.Children.Add(options);

        var grid = BuildUploadQueueGrid();
        grid.ItemsSource = items;
        var table = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 204, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Child = grid,
        };
        Grid.SetRow(table, 2);
        root.Children.Add(table);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var queueStatus = new TextBlock
        {
            Text = $"{items.Count(item => item.Include):N0} ready item(s) selected.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 215, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(queueStatus);
        var stop = new Button
        {
            Content = "Stop queue",
            MinWidth = 100,
            MinHeight = 36,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(8, 0, 0, 0),
        };
        StyleQuizHistoryButton(stop, Color.FromRgb(248, 90, 105));
        Grid.SetColumn(stop, 1);
        footer.Children.Add(stop);
        var close = new Button
        {
            Content = "Close",
            MinWidth = 82,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };
        StyleQuizHistoryButton(close, Color.FromRgb(204, 70, 255));
        Grid.SetColumn(close, 2);
        footer.Children.Add(close);
        var start = new Button
        {
            Content = "Start queue",
            MinWidth = 116,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        StyleQuizHistoryButton(start, Color.FromRgb(70, 235, 115));
        Grid.SetColumn(start, 3);
        footer.Children.Add(start);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        selectReady.Click += (_, _) =>
        {
            foreach (var item in items)
                item.Include = item.Remaining != SocialUploadDestination.None && item.VideoPath.Length > 0;
            queueStatus.Text = $"{items.Count(item => item.Include):N0} ready item(s) selected.";
        };
        clear.Click += (_, _) =>
        {
            foreach (var item in items) item.Include = false;
            queueStatus.Text = "Queue selection cleared.";
        };
        findMissing.Click += async (_, _) =>
        {
            findMissing.IsEnabled = false;
            try
            {
                var queueHistory = _data.GetQuizHistory()
                    .Where(history => items.Any(item => item.HistoryId == history.Id))
                    .ToList();
                if (queueHistory.Count == 0)
                {
                    queueStatus.Text = "There are no queue items to check.";
                    return;
                }

                var picker = new OpenFolderDialog
                {
                    Title = "Select the folder containing your quiz project folders",
                };
                if (picker.ShowDialog(dialog) != true) return;

                queueStatus.Text = $"Checking video paths for {queueHistory.Count:N0} queue item(s)...";
                var matches = await Task.Run(() =>
                    SocialUploadQueuePathFinder.FindMissingVideos(queueHistory, picker.FolderName));
                foreach (var match in matches)
                {
                    var item = items.First(candidate => candidate.HistoryId == match.HistoryId);
                    item.ApplyPaths(match.VideoPath, match.ThumbnailPath);
                    var projectFolder = Path.GetDirectoryName(match.VideoPath) ?? "";
                    if (projectFolder.Length > 0)
                        _data.UpdateQuizHistoryProjectFolder(match.HistoryId, projectFolder);
                }

                var stillMissing = items.Count(item => item.VideoPath.Length == 0);
                queueStatus.Text = matches.Count == 0
                    ? "No matching quiz videos were found under that folder."
                    : $"Found and saved {matches.Count:N0} video path(s); {stillMissing:N0} still missing.";
                RefreshQuizHistory();
            }
            catch (Exception error)
            {
                queueStatus.Text = "Could not open or search that folder: " + error.Message;
                MessageBox.Show(dialog,
                    "The folder search could not be opened or completed.\n\n" + error.Message,
                    "Find Missing Videos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                findMissing.IsEnabled = true;
            }
        };

        CancellationTokenSource? cancellation = null;
        stop.Click += (_, _) =>
        {
            stop.Content = "Stopping...";
            queueStatus.Text = "Stopping the queue...";
            cancellation?.Cancel();
        };
        start.Click += async (_, _) =>
        {
            var selected = items.Where(item =>
                    item.Include &&
                    item.VideoPath.Length > 0 &&
                    item.Remaining != SocialUploadDestination.None)
                .ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(dialog, "Select at least one ready quiz first.",
                    "Upload Queue", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            cancellation = new CancellationTokenSource();
            start.IsEnabled = false;
            close.IsEnabled = false;
            stop.Content = "Stop queue";
            stop.Visibility = Visibility.Visible;
            privacy.IsEnabled = false;
            notify.IsEnabled = false;
            selectReady.IsEnabled = false;
            clear.IsEnabled = false;
            findMissing.IsEnabled = false;
            var completedItems = 0;
            try
            {
                for (var index = 0; index < selected.Count; index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var item = selected[index];
                    var current = _data.GetQuizHistory().FirstOrDefault(history => history.Id == item.HistoryId);
                    if (current is null)
                    {
                        item.Status = "Skipped: history entry no longer exists";
                        item.Include = false;
                        continue;
                    }

                    var remaining = SocialUploadQueuePlanner.RemainingDestinations(current);
                    item.ApplyRemaining(remaining);
                    if (remaining == SocialUploadDestination.None)
                    {
                        item.Status = "Complete";
                        continue;
                    }

                    queueStatus.Text = $"Uploading {index + 1:N0} of {selected.Count:N0}: {item.Title}";
                    item.Status = "Starting...";
                    var result = await UploadQueuedQuizAsync(
                        current,
                        item.VideoPath,
                        item.ThumbnailPath,
                        Convert.ToString(privacy.SelectedItem) ?? "private",
                        notify.IsChecked == true,
                        remaining,
                        status => item.Status = status,
                        cancellation.Token);

                    var updated = _data.GetQuizHistory().First(history => history.Id == item.HistoryId);
                    item.ApplyRemaining(SocialUploadQueuePlanner.RemainingDestinations(updated));
                    var metaAuthenticationError = result.Errors
                        .FirstOrDefault(SocialUploadQueuePlanner.IsMetaAuthenticationError);
                    if (result.Errors.Count > 0)
                    {
                        var prefix = result.Completed.Count > 0
                            ? $"Partial ({string.Join(", ", result.Completed)}); "
                            : "";
                        item.Status = prefix + string.Join(" | ", result.Errors);
                        item.Include = item.Remaining != SocialUploadDestination.None;
                    }
                    else
                    {
                        completedItems++;
                        item.Status = result.Warnings.Count == 0
                            ? "Complete"
                            : "Complete; " + string.Join(" | ", result.Warnings);
                        item.Include = false;
                    }
                    RefreshQuizHistory();
                    if (metaAuthenticationError is not null)
                    {
                        foreach (var waiting in selected.Skip(index + 1).Where(candidate =>
                                     candidate.Remaining.HasFlag(SocialUploadDestination.Facebook) ||
                                     candidate.Remaining.HasFlag(SocialUploadDestination.Instagram)))
                            waiting.Status = "Waiting: renew Meta token in Settings → Facebook";
                        queueStatus.Text =
                            "Queue stopped: renew the Facebook Page access token in Settings → Facebook, then retry.";
                        break;
                    }
                }

                if (!queueStatus.Text.StartsWith("Queue stopped:", StringComparison.Ordinal))
                    queueStatus.Text = $"Queue finished: {completedItems:N0} complete, " +
                                       $"{items.Count(item => item.Include):N0} ready to retry.";
            }
            catch (OperationCanceledException)
            {
                queueStatus.Text = "Queue stopped. Completed uploads were saved; selected remaining items can be retried.";
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                start.IsEnabled = true;
                close.IsEnabled = true;
                stop.Visibility = Visibility.Collapsed;
                privacy.IsEnabled = true;
                notify.IsEnabled = true;
                selectReady.IsEnabled = true;
                clear.IsEnabled = true;
                findMissing.IsEnabled = true;
                RefreshQuizHistory();
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static DataGrid BuildUploadQueueGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(35, 62, 145)),
            RowHeaderWidth = 0,
            MinRowHeight = 42,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(8, 14, 62)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(20, 31, 90)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(29, 38, 104)),
        };
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        grid.CellStyle = cellStyle;
        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(13, 18, 78))));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 202, 45))));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        grid.ColumnHeaderStyle = headerStyle;
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Queue",
            Binding = new Binding(nameof(SocialUploadQueueItem.Include))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = new DataGridLength(62),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Quiz",
            Binding = new Binding(nameof(SocialUploadQueueItem.Title)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Type",
            Binding = new Binding(nameof(SocialUploadQueueItem.VideoType)),
            IsReadOnly = true,
            Width = new DataGridLength(78),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Remaining platforms",
            Binding = new Binding(nameof(SocialUploadQueueItem.RemainingDisplay)),
            IsReadOnly = true,
            Width = new DataGridLength(220),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Video file",
            Binding = new Binding(nameof(SocialUploadQueueItem.VideoFile)),
            IsReadOnly = true,
            Width = new DataGridLength(190),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(SocialUploadQueueItem.Status)),
            IsReadOnly = true,
            Width = new DataGridLength(320),
        });
        return grid;
    }

    private async Task<SocialUploadQueueResult> UploadQueuedQuizAsync(
        QuizHistorySummary history,
        string videoPath,
        string thumbnailPath,
        string privacy,
        bool notifySubscribers,
        SocialUploadDestination destinations,
        Action<string> setStatus,
        CancellationToken cancellationToken)
    {
        var completed = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var metaAuthenticationFailed = false;
        var file = SocialVideoUploadRules.ValidateVideoFile(videoPath);
        var title = (history.YouTubeTitle.Length > 0 ? history.YouTubeTitle : history.Title).Trim();
        var description = SocialVideoUploadRules.UploadDescription(history).Trim();
        SocialVideoUploadRules.ValidateUploadMetadata(
            history.VideoType, title, description, requireFullYouTubeVideoLink: false);
        SocialVideoUploadRules.ValidatePrivacy(privacy);

        string? thumbnail = null;
        try
        {
            thumbnail = SocialVideoUploadRules.ValidateThumbnailFile(thumbnailPath);
        }
        catch (Exception error)
        {
            warnings.Add("Thumbnail skipped: " + error.Message);
        }

        double? duration = null;
        async Task<double> DurationAsync()
        {
            duration ??= await new NativeFfmpegTimelineService()
                .MediaDurationAsync(file, cancellationToken);
            return duration.Value;
        }

        if (destinations.HasFlag(SocialUploadDestination.YouTube))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SocialVideoUploadRules.ValidateUploadMetadata(
                    history.VideoType, title, description, requireFullYouTubeVideoLink: true);
                setStatus("Uploading to YouTube...");
                var accessToken = await GetYouTubeManagementAccessTokenAsync();
                var result = await _youtubeVideoUpload.UploadAsync(
                    accessToken,
                    file,
                    new YouTubeVideoUpload(title, description, privacy, notifySubscribers),
                    cancellationToken);
                _data.UpdateQuizHistoryYouTubeAnalytics(
                    history.Id, true, result.Url, 0, 0, DateTime.Today);
                completed.Add("YouTube");
                if (thumbnail is not null)
                {
                    setStatus("Setting YouTube thumbnail...");
                    try
                    {
                        await _youtubeVideoUpload.SetThumbnailAsync(
                            accessToken, result.VideoId, thumbnail, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        warnings.Add("YouTube thumbnail: " + error.Message);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                errors.Add("YouTube: " + error.Message);
            }
        }

        if (destinations.HasFlag(SocialUploadDestination.Facebook))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SocialVideoUploadRules.ValidateUploadMetadata(
                    history.VideoType, title, description, requireFullYouTubeVideoLink: true);
                SocialVideoUploadRules.ValidateFacebookDuration(await DurationAsync());
                setStatus("Uploading to Facebook...");
                var pageToken = FacebookPageToken();
                var result = await _facebookReelUpload.UploadAsync(
                    pageToken, file, title, description, cancellationToken: cancellationToken);
                _data.UpdateQuizHistoryFacebookAnalytics(
                    history.Id, true, result.Url, 0, 0, 0, 0, DateTime.Today);
                completed.Add("Facebook");
                if (thumbnail is not null)
                {
                    setStatus("Setting Facebook Reel cover...");
                    try
                    {
                        await _facebookReelUpload.SetThumbnailAsync(
                            pageToken, result.VideoId, thumbnail, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        warnings.Add("Facebook Reel cover: " + error.Message);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                errors.Add("Facebook: " + error.Message);
                metaAuthenticationFailed = SocialUploadQueuePlanner.IsMetaAuthenticationError(error.Message);
            }
        }

        if (destinations.HasFlag(SocialUploadDestination.Instagram) && !metaAuthenticationFailed)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SocialVideoUploadRules.ValidateInstagramDuration(await DurationAsync());
                setStatus("Uploading to Instagram...");
                var pageToken = FacebookPageToken();
                var result = await _instagramReelUpload.UploadReelAsync(
                    pageToken,
                    file,
                    SocialVideoUploadRules.InstagramCaption(description),
                    cancellationToken: cancellationToken);
                _data.UpdateQuizHistoryInstagramPublication(
                    history.Id, true, result.Url, DateTime.Today);
                completed.Add("Instagram");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                errors.Add("Instagram: " + error.Message);
            }
        }

        return new SocialUploadQueueResult(completed, warnings, errors);
    }
}
