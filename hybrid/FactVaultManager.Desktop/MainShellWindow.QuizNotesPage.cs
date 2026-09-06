namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // Keep this legacy alias so existing navigation insertion helpers place
    // Upload Manager and YouTube Manager after Quiz History.
    private int _quizNotesTabIndex => _quizHistoryTabIndex;

    private void InitializeQuizNotesPage()
    {
        // Quiz Notes UI removed. Existing saved notes remain supported by
        // QuizNotesStore for backwards compatibility.
    }
}
