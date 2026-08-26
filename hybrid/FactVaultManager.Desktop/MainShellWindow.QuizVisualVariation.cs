using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizVisualVariationEventsRegistered = RegisterQuizVisualVariationEvents();
    private BitmapSource? _quizVisualVariationBasePreview;
    private BitmapSource? _quizVisualVariationAppliedPreview;
    private QuizVisualVariation? _quizLastAutomaticVisualVariation;

    private static bool RegisterQuizVisualVariationEvents()
    {
        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(QuizVisualVariationComboBox_SelectionChanged),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            TextBox.TextChangedEvent,
            new TextChangedEventHandler(QuizVisualVariationTextBox_TextChanged),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(ToggleButton),
            ToggleButton.CheckedEvent,
            new RoutedEventHandler(QuizVisualVariationToggle_Changed),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(ToggleButton),
            ToggleButton.UncheckedEvent,
            new RoutedEventHandler(QuizVisualVariationToggle_Changed),
            handledEventsToo: true);
        return true;
    }

    private static void QuizVisualVariationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || Window.GetWindow(combo) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(combo, window._quizThemeComboBox) &&
            !ReferenceEquals(combo, window._quizFormatComboBox) &&
            !ReferenceEquals(combo, window._quizCategoryComboBox) &&
            !ReferenceEquals(combo, window._quizPreviewCardComboBox) &&
            !ReferenceEquals(combo, window._quizPreviewQuestionComboBox))
            return;

        window.ScheduleQuizVisualVariationPreviewRefresh();
    }

    private static void QuizVisualVariationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || Window.GetWindow(textBox) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(textBox, window._quizTitleTextBox) &&
            !ReferenceEquals(textBox, window._quizSecondsPerQuestionTextBox) &&
            !ReferenceEquals(textBox, window._quizLogoPathTextBox))
            return;

        window.ScheduleQuizVisualVariationPreviewRefresh();
    }

    private static void QuizVisualVariationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || Window.GetWindow(toggle) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(toggle, window._quizCountdownCheckBox) &&
            !ReferenceEquals(toggle, window._quizRevealAnimationCheckBox))
            return;

        window.ScheduleQuizVisualVariationPreviewRefresh();
    }

    private void ScheduleQuizVisualVariationPreviewRefresh() =>
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(RefreshQuizVisualVariationPreview));

    private void ApplyAutomaticQuizVisualVariationForDraft()
    {
        if (_quizDraftQuestions.Count == 0)
        {
            ClearQuizVisualVariationPreview();
            return;
        }

        var vertical = _quizFormatComboBox?.SelectedIndex == 1;
        var quizType = IsLogoQuizSelected() ? QuizTypeCatalog.Logo : QuizTypeCatalog.Standard;
        if (!QuizVisualVariationPlanner.Applies(vertical, quizType))
        {
            ClearQuizVisualVariationPreview();
            return;
        }

        var variation = QuizVisualVariationPlanner.NextAfter(_quizLastAutomaticVisualVariation);
        _quizLastAutomaticVisualVariation = variation;
        if (_quizThemeComboBox is not null)
            _quizThemeComboBox.SelectedItem = QuizVisualThemeCatalog.Resolve(variation.ThemeKey).DisplayName;

        RefreshQuizVisualVariationPreview();
    }

    private string CurrentQuizVisualVariationDisplay()
    {
        var vertical = _quizFormatComboBox?.SelectedIndex == 1;
        var quizType = IsLogoQuizSelected() ? QuizTypeCatalog.Logo : QuizTypeCatalog.Standard;
        if (_quizDraftQuestions.Count == 0 || !QuizVisualVariationPlanner.Applies(vertical, quizType))
            return "Fixed layout";

        return QuizVisualVariationPlanner.ForTheme(CurrentQuizVisualSettings().ThemeKey).DisplayName;
    }

    private void RefreshQuizVisualVariationPreview()
    {
        if (_quizPreviewImage is null)
            return;

        RestoreQuizVisualVariationBasePreview();
        if (_quizDraftQuestions.Count == 0 || _quizPreviewImage.Source is not BitmapSource source)
            return;

        var vertical = _quizFormatComboBox?.SelectedIndex == 1;
        var quizType = IsLogoQuizSelected() ? QuizTypeCatalog.Logo : QuizTypeCatalog.Standard;
        if (!QuizVisualVariationPlanner.Applies(vertical, quizType))
            return;

        var theme = QuizVisualThemeCatalog.Resolve(CurrentQuizVisualSettings().ThemeKey);
        var variation = QuizVisualVariationPlanner.ForTheme(theme.Key);
        var hideChoicePrompt = SelectedQuizPreviewCardKind() == QuizPreviewCardKind.Question;

        _quizVisualVariationBasePreview = source;
        _quizVisualVariationAppliedPreview = QuizCardVariationPostProcessor.ApplyPreview(
            source,
            theme,
            variation.LayoutKey,
            hideChoicePrompt);
        _quizPreviewImage.Source = _quizVisualVariationAppliedPreview;
    }

    private void RestoreQuizVisualVariationBasePreview()
    {
        if (_quizPreviewImage is not null &&
            _quizVisualVariationBasePreview is not null &&
            _quizVisualVariationAppliedPreview is not null &&
            ReferenceEquals(_quizPreviewImage.Source, _quizVisualVariationAppliedPreview))
        {
            _quizPreviewImage.Source = _quizVisualVariationBasePreview;
        }

        _quizVisualVariationBasePreview = null;
        _quizVisualVariationAppliedPreview = null;
    }

    private void ClearQuizVisualVariationPreview(bool resetImage = true)
    {
        if (resetImage)
            RestoreQuizVisualVariationBasePreview();
        else
        {
            _quizVisualVariationBasePreview = null;
            _quizVisualVariationAppliedPreview = null;
        }

        if (_quizPreviewImage is null)
            return;
        _quizPreviewImage.Width = double.NaN;
        _quizPreviewImage.Height = double.NaN;
        _quizPreviewImage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _quizPreviewImage.VerticalAlignment = VerticalAlignment.Stretch;
        _quizPreviewImage.Margin = new Thickness(0);
    }
}
