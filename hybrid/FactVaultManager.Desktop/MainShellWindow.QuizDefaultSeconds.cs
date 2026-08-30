namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_quizSecondsPerQuestionTextBox is not null &&
            string.Equals(_quizSecondsPerQuestionTextBox.Text?.Trim(), "8", StringComparison.Ordinal))
        {
            _quizSecondsPerQuestionTextBox.Text = QuizBuilderModePresets.Full.SecondsPerQuestion.ToString();
            _quizSecondsPerQuestion = QuizBuilderModePresets.Full.SecondsPerQuestion;
        }
    }
}
