using System.Windows;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private async Task RetryFailedUploadStepsAsync(QuizHistorySummary history)
    {
        var journal = _data.SocialUploadJournal;
        var failed = journal.List(history.Id).Where(entry => entry.HasFailure).ToList();
        if (failed.Count == 0)
        {
            MessageBox.Show(this, "This quiz has no failed upload step to retry.",
                "Retry Failed Step", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var uploadFailure = failed.FirstOrDefault(entry =>
            entry.EffectiveFailedStep == SocialUploadJournalStep.Upload);
        if (uploadFailure is not null)
        {
            MessageBox.Show(this,
                $"The {uploadFailure.Platform} video upload itself failed. The upload window will reopen so the video can be sent again.",
                "Retry Upload", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowQuizUploadDialog(history);
            return;
        }

        var completed = new List<string>();
        var errors = new List<string>();
        foreach (var entry in failed)
        {
            var step = entry.EffectiveFailedStep;
            try
            {
                journal.RecordStepStarted(history.Id, entry.Platform, step);
                await RetryUploadStepAsync(history, entry, step);
                journal.RecordStepCompleted(history.Id, entry.Platform, step);
                completed.Add($"{entry.Platform} {step}");
            }
            catch (Exception error)
            {
                journal.RecordFailure(history.Id, entry.Platform, step, error.Message);
                errors.Add($"{entry.Platform} {step}: {error.Message}");
            }
        }

        RefreshQuizHistory();
        RefreshUploadManager();
        var message = completed.Count == 0
            ? "No failed steps were completed."
            : "Completed: " + string.Join(", ", completed) + ".";
        if (errors.Count > 0) message += "\n\nStill failing:\n" + string.Join("\n", errors);
        MessageBox.Show(this, message, "Retry Failed Step", MessageBoxButton.OK,
            errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RetryUploadStepAsync(
        QuizHistorySummary history,
        SocialUploadJournalEntry entry,
        string step)
    {
        if (entry.RemoteId.Length == 0)
            throw new InvalidOperationException("The saved remote video ID is missing. Reset the platform upload state and upload again.");

        if (string.Equals(entry.Platform, "YouTube", StringComparison.Ordinal))
        {
            var token = await GetYouTubeManagementAccessTokenAsync();
            var channel = await _youtubeManagement.GetMyChannelAsync(token);
            var settings = _data.LoadSettings();
            SocialPublishingAccountGuard.EnsureMatches(
                "YouTube channel", settings.ApprovedYouTubeChannelId, channel.Id);
            if (step == SocialUploadJournalStep.Verification)
            {
                var privacy = history.YouTubePrivacy.Length > 0 ? history.YouTubePrivacy : "private";
                await _youtubeManagement.VerifyUploadedVideoAsync(
                    token, entry.RemoteId, channel.Id, history.UploadTitleDisplay, privacy);
                return;
            }
            if (step == SocialUploadJournalStep.Thumbnail)
            {
                var thumbnail = SocialVideoUploadRules.FindLikelyThumbnail(history.ProjectFolder) ?? "";
                await _youtubeVideoUpload.SetThumbnailAsync(token, entry.RemoteId, thumbnail);
                return;
            }
            if (step == SocialUploadJournalStep.Comment)
            {
                var commentId = await _youtubeManagement.PostTopLevelCommentAsync(
                    token, entry.RemoteId, history.PinnedComment);
                _data.UpdateQuizHistoryYouTubeFirstComment(history.Id, commentId);
                return;
            }
        }

        if (string.Equals(entry.Platform, "Facebook", StringComparison.Ordinal))
        {
            var token = FacebookPageToken();
            var page = await _facebookAnalytics.GetPageIdentityAsync(token);
            var settings = _data.LoadSettings();
            SocialPublishingAccountGuard.EnsureMatches(
                "Facebook Page", settings.ApprovedFacebookPageId, page.PageId);
            if (step == SocialUploadJournalStep.Verification)
            {
                await _facebookReelUpload.VerifyUploadedReelAsync(token, entry.RemoteId);
                return;
            }
            if (step == SocialUploadJournalStep.Thumbnail)
            {
                var thumbnail = SocialVideoUploadRules.FindLikelyThumbnail(history.ProjectFolder) ?? "";
                await _facebookReelUpload.SetThumbnailAsync(token, entry.RemoteId, thumbnail);
                return;
            }
            if (step == SocialUploadJournalStep.Comment)
            {
                var commentId = await _facebookComments.PostTopLevelCommentAsync(
                    token, entry.RemoteId, history.PinnedComment);
                _data.UpdateQuizHistoryFacebookFirstComment(history.Id, commentId);
                return;
            }
        }

        throw new InvalidOperationException($"Automatic retry is not available for {entry.Platform} {step}.");
    }
}
