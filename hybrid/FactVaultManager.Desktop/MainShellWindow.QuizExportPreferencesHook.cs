using System.Windows;
using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizExportPreferencesHookRegistered = RegisterQuizExportPreferencesHook();
    private Button? _quizResolvePreferencesAppliedButton;
    private bool _quizResolvePreferencesApplying;

    private static bool RegisterQuizExportPreferencesHook()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizResolveExportButton_Loaded));
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(QuizResolveExportButton_Click));
        return true;
    }

    private static void QuizResolveExportButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Create Resolve Quiz", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
            return;

        window.ApplyLastQuizResolveExportPreferences(button);
    }

    private static void QuizResolveExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !string.Equals(button.Content?.ToString(), "Create Resolve Quiz", StringComparison.Ordinal) ||
            Window.GetWindow(button) is not MainShellWindow window)
            return;

        window.SaveCurrentQuizResolveExportPreferences();
    }

    private void ApplyLastQuizResolveExportPreferences(Button exportButton)
    {
        if (ReferenceEquals(_quizResolvePreferencesAppliedButton, exportButton))
            return;
        if (_quizFormatComboBox is null || _quizCountdownCheckBox is null || _quizRevealAnimationCheckBox is null ||
            _quizNarrationCheckBox is null || _quizNarrateAnswersCheckBox is null || _quizVoiceComboBox is null ||
            _quizCountdownTickCheckBox is null || _quizAnswerRevealSfxCheckBox is null ||
            _quizBackgroundMusicCheckBox is null || _quizBackgroundMusicPathTextBox is null)
            return;

        _quizResolvePreferencesApplying = true;
        try
        {
            var saved = _data.LoadQuizResolveExportPreferences();
            _quizFormatComboBox.SelectedIndex = saved.FormatIndex;
            _quizCountdownCheckBox.Content = "Full question countdown";
            _quizCountdownCheckBox.ToolTip = "Show every second of the answer countdown, for example 8, 7, 6, 5, 4, 3, 2, 1.";
            _quizCountdownCheckBox.IsChecked = saved.ShowCountdown;
            _quizRevealAnimationCheckBox.IsChecked = saved.AnimateReveal;
            _quizNarrationCheckBox.IsChecked = saved.Narrate;
            _quizNarrateAnswersCheckBox.IsChecked = saved.NarrateAnswers;
            _quizVoiceComboBox.SelectedItem = saved.Voice;
            _quizCountdownTickCheckBox.IsChecked = saved.CountdownTicks;
            _quizAnswerRevealSfxCheckBox.IsChecked = saved.AnswerRevealSfx;

            var musicExists = saved.BackgroundMusicPath.Length > 0 && File.Exists(saved.BackgroundMusicPath);
            _quizBackgroundMusicPathTextBox.Text = musicExists ? saved.BackgroundMusicPath : "";
            _quizBackgroundMusicCheckBox.IsChecked = saved.UseBackgroundMusic && musicExists;

            _quizNarrateAnswersCheckBox.IsEnabled = saved.Narrate;
            _quizVoiceComboBox.IsEnabled = saved.Narrate;
            _quizCountdownTickCheckBox.IsEnabled = saved.ShowCountdown;
            _quizResolvePreferencesAppliedButton = exportButton;
        }
        finally
        {
            _quizResolvePreferencesApplying = false;
        }

        HookQuizResolvePreferenceChanges();
    }

    private void HookQuizResolvePreferenceChanges()
    {
        if (_quizFormatComboBox is null || _quizCountdownCheckBox is null || _quizRevealAnimationCheckBox is null ||
            _quizNarrationCheckBox is null || _quizNarrateAnswersCheckBox is null || _quizVoiceComboBox is null ||
            _quizCountdownTickCheckBox is null || _quizAnswerRevealSfxCheckBox is null ||
            _quizBackgroundMusicCheckBox is null || _quizBackgroundMusicPathTextBox is null)
            return;

        _quizFormatComboBox.SelectionChanged -= QuizResolvePreferenceChanged;
        _quizFormatComboBox.SelectionChanged += QuizResolvePreferenceChanged;
        _quizVoiceComboBox.SelectionChanged -= QuizResolvePreferenceChanged;
        _quizVoiceComboBox.SelectionChanged += QuizResolvePreferenceChanged;
        _quizBackgroundMusicPathTextBox.TextChanged -= QuizResolvePreferenceChanged;
        _quizBackgroundMusicPathTextBox.TextChanged += QuizResolvePreferenceChanged;

        foreach (var checkBox in new[]
                 {
                     _quizCountdownCheckBox,
                     _quizRevealAnimationCheckBox,
                     _quizNarrationCheckBox,
                     _quizNarrateAnswersCheckBox,
                     _quizCountdownTickCheckBox,
                     _quizAnswerRevealSfxCheckBox,
                     _quizBackgroundMusicCheckBox,
                 })
        {
            checkBox.Checked -= QuizResolvePreferenceChanged;
            checkBox.Unchecked -= QuizResolvePreferenceChanged;
            checkBox.Checked += QuizResolvePreferenceChanged;
            checkBox.Unchecked += QuizResolvePreferenceChanged;
        }
    }

    private void QuizResolvePreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (_quizResolvePreferencesApplying || _quizResolvePreferencesAppliedButton is null)
            return;

        SaveCurrentQuizResolveExportPreferences();
    }

    private void SaveCurrentQuizResolveExportPreferences()
    {
        if (_quizFormatComboBox is null)
            return;

        _data.SaveQuizResolveExportPreferences(new QuizResolveExportPreferences(
            FormatIndex: _quizFormatComboBox.SelectedIndex,
            ShowCountdown: _quizCountdownCheckBox?.IsChecked != false,
            AnimateReveal: _quizRevealAnimationCheckBox?.IsChecked != false,
            Narrate: _quizNarrationCheckBox?.IsChecked == true,
            NarrateAnswers: _quizNarrateAnswersCheckBox?.IsChecked == true,
            Voice: Convert.ToString(_quizVoiceComboBox?.SelectedItem) ?? "alloy",
            CountdownTicks: _quizCountdownTickCheckBox?.IsChecked == true,
            AnswerRevealSfx: _quizAnswerRevealSfxCheckBox?.IsChecked == true,
            UseBackgroundMusic: _quizBackgroundMusicCheckBox?.IsChecked == true,
            BackgroundMusicPath: (_quizBackgroundMusicPathTextBox?.Text ?? "").Trim()));
    }
}
