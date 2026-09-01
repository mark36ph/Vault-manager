using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _libraryInstagramActionInitialized;
    private int _libraryInstagramActionAttempts;
    private DispatcherTimer? _libraryInstagramActionRetryTimer;
    private Button? _libraryInstagramButton;

    public void InitializeLibraryInstagramAction()
    {
        if (_libraryInstagramActionInitialized)
            return;

        _libraryInstagramActionInitialized = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(EnsureLibraryInstagramAction));
    }

    private void EnsureLibraryInstagramAction()
    {
        if (_quizHistoryTabIndex < 0 ||
            _quizHistoryTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_quizHistoryTabIndex] is not TabItem historyTab ||
            historyTab.Content is not Border { Child: Grid root })
        {
            RetryLibraryInstagramAction();
            return;
        }

        var footer = root.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 3);
        var actions = footer?.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 1);
        if (actions is null)
        {
            RetryLibraryInstagramAction();
            return;
        }

        var existing = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "IG", StringComparison.Ordinal));
        if (existing is not null)
        {
            _libraryInstagramButton = existing;
            return;
        }

        var instagram = new Button
        {
            Content = "IG",
            MinWidth = 54,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Post the selected full quiz promo to Instagram",
        };
        StyleQuizHistoryButton(instagram, Color.FromRgb(255, 88, 184));
        instagram.Click += async (_, _) => await PublishSelectedQuizInstagramPromoAsync();

        var folder = actions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Folder", StringComparison.Ordinal));
        var insertAt = folder is null ? actions.Children.Count : actions.Children.IndexOf(folder);
        actions.Children.Insert(Math.Max(0, insertAt), instagram);
        _libraryInstagramButton = instagram;
    }

    private void RetryLibraryInstagramAction()
    {
        if (++_libraryInstagramActionAttempts >= 50)
            return;

        _libraryInstagramActionRetryTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _libraryInstagramActionRetryTimer.Tick -= LibraryInstagramActionRetryTimer_Tick;
        _libraryInstagramActionRetryTimer.Tick += LibraryInstagramActionRetryTimer_Tick;
        _libraryInstagramActionRetryTimer.Start();
    }

    private void LibraryInstagramActionRetryTimer_Tick(object? sender, EventArgs e)
    {
        _libraryInstagramActionRetryTimer?.Stop();
        EnsureLibraryInstagramAction();
    }

    private async Task PublishSelectedQuizInstagramPromoAsync()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(
                this,
                "Select a full quiz in Library first.",
                "Instagram promo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!string.Equals(history.VideoType, "Video", StringComparison.Ordinal))
        {
            MessageBox.Show(
                this,
                "Select the full Video row for the quiz. The IG action posts that quiz's prepared Instagram promo.",
                "Instagram promo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (QuizPromoShortSocialPublicationStore.LoadInstagram(history.ProjectFolder) is not null)
            {
                MessageBox.Show(
                    this,
                    "The Instagram promo for this quiz is already posted.",
                    "Instagram promo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshLibraryReleasePlatformStatusSnapshot();
                return;
            }

            IReadOnlyList<PublicationStateEntry> publications;
            try { publications = _data.PublicationState.List(); }
            catch { publications = []; }
            var historyPublications = publications
                .Where(entry => entry.HistoryId == history.Id)
                .ToList();
            var autopilotState = FactburstFullAutopilotStateStore.Load(_data.SettingsPath);
            if (!InstagramPromoFollowupPlanner.IsVerifiedYouTubePublic(
                    history.Id,
                    autopilotState,
                    historyPublications))
            {
                MessageBox.Show(
                    this,
                    "The full YouTube quiz is not verified public yet. Instagram will become available after the YouTube release is public.",
                    "Instagram promo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (history.ProjectFolder.Trim().Length == 0 || !Directory.Exists(history.ProjectFolder))
                throw new DirectoryNotFoundException("The quiz project folder is missing, so the prepared Instagram promo cannot be found.");

            var video = QuizPromoShortPaths.FindExisting(history.ProjectFolder)
                        ?? throw new FileNotFoundException("The prepared Instagram promo video could not be found for this quiz.");
            video = SocialVideoUploadRules.ValidateVideoFile(video);
            var title = QuizPromoShortUploadMetadata.Title(history.UploadTitleDisplay);
            var preflight = await ConfirmSocialPublishingPreflightAsync(
                this,
                SocialUploadDestination.Instagram,
                video,
                title,
                "private",
                scheduledFor: null);
            if (preflight is null)
                return;

            _data.PublicationState.BeginAttempt(
                history.Id,
                PublicationPlatform.Instagram,
                PublicationContentKind.Promo);

            await PublishInstagramPromoCoreAsync(
                history,
                preflight.FacebookPageToken,
                "library-instagram-promo");

            MessageBox.Show(
                this,
                "Instagram promo published successfully.",
                "Instagram promo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (_instagramAnalyticsGrid is not null)
                await RefreshInstagramManagerAsync(false);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Library Instagram promo #{history.Id}: {error}");
            _data.PublicationState.RecordFailure(
                history.Id,
                PublicationPlatform.Instagram,
                PublicationContentKind.Promo,
                SocialUploadJournalStep.Upload,
                error.Message,
                source: "library-instagram-promo");
            MessageBox.Show(
                this,
                error.Message,
                "Instagram promo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SyncAutopilotNeedsYouWithInstagram();
            RefreshLibraryReleasePlatformStatusSnapshot();
        }
    }
}
