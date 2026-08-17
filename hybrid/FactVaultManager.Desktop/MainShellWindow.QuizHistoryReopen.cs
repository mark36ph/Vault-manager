using System.Windows;

namespace FactVaultManager.Desktop;

public sealed record QuizHistoryDraftRestoreResult(
    IReadOnlyList<QuizQuestion> Questions,
    IReadOnlyList<int> MissingQuestionIds);

public static class QuizHistoryDraftRestorer
{
    public static QuizHistoryDraftRestoreResult Restore(
        IReadOnlyList<QuizHistoryQuestion> historyQuestions,
        IReadOnlyList<QuizQuestion> bankQuestions)
    {
        ArgumentNullException.ThrowIfNull(historyQuestions);
        ArgumentNullException.ThrowIfNull(bankQuestions);

        var bank = bankQuestions.ToDictionary(question => question.Id);
        var restored = new List<QuizQuestion>();
        var missing = new List<int>();
        foreach (var history in historyQuestions.OrderBy(item => item.Position))
        {
            if (bank.TryGetValue(history.QuestionId, out var question))
                restored.Add(question);
            else
                missing.Add(history.QuestionId);
        }
        return new QuizHistoryDraftRestoreResult(restored, missing);
    }
}

public partial class MainShellWindow
{
    private void ReopenSelectedQuizHistoryInBuilder()
    {
        if (_quizHistoryGrid?.SelectedItem is not QuizHistorySummary history)
        {
            MessageBox.Show(this, "Select a quiz-history entry first.", "Reopen Quiz", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var historyQuestions = _data.GetQuizHistoryQuestions(history.Id);
            var bankQuestions = _data.GetQuizQuestions(limit: 10_000);
            var restored = QuizHistoryDraftRestorer.Restore(historyQuestions, bankQuestions);
            if (restored.MissingQuestionIds.Count > 0)
            {
                throw new InvalidOperationException(
                    "This quiz cannot be reopened exactly because these question-bank entries were deleted: " +
                    string.Join(", ", restored.MissingQuestionIds.Select(id => $"#{id}")) + ".");
            }
            if (restored.Questions.Count == 0)
                throw new InvalidOperationException("This quiz history entry does not contain any reusable questions.");

            _quizDraftQuestions = restored.Questions.ToList();
            _quizSecondsPerQuestion = history.QuestionSeconds;
            if (_quizQuestionCountTextBox is not null)
                _quizQuestionCountTextBox.Text = restored.Questions.Count.ToString();
            if (_quizSecondsPerQuestionTextBox is not null)
                _quizSecondsPerQuestionTextBox.Text = history.QuestionSeconds.ToString();
            if (_quizDraftGrid is not null)
                _quizDraftGrid.ItemsSource = QuizDraftRows(_quizDraftQuestions);
            if (_quizTitleTextBox is not null && !string.IsNullOrWhiteSpace(history.Title))
                _quizTitleTextBox.Text = history.Title;
            if (_quizFormatComboBox is not null)
                _quizFormatComboBox.SelectedIndex = string.Equals(history.Format, "9:16", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (_quizShuffleAnswersCheckBox is not null)
                _quizShuffleAnswersCheckBox.IsChecked = history.ShuffleAnswers;

            InvalidateQuizPublishingExportCompletion();
            if (_quizDraftStatusText is not null)
                _quizDraftStatusText.Text = $"Reopened Quiz History #{history.Id} • {restored.Questions.Count} questions • ready to review or export again.";
            if (_quizPageStatusText is not null)
                _quizPageStatusText.Text = $"Reopened {history.Title} from Quiz History";

            MainTabs.SelectedIndex = _quizTabIndex;
            SelectQuizWorkspacePage("draft");
            RefreshQuizPreview();
            RefreshQuizPublishingPage();
            UpdateQuizHeaderButtons();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Reopen Quiz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
