using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private static readonly bool QuizVisualVariationEventsRegistered = RegisterQuizVisualVariationEvents();
    private readonly List<UIElement> _quizVisualVariationPreviewDecorations = [];

    private static bool RegisterQuizVisualVariationEvents()
    {
        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(QuizVisualVariationComboBox_SelectionChanged),
            handledEventsToo: true);
        return true;
    }

    private static void QuizVisualVariationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || Window.GetWindow(combo) is not MainShellWindow window)
            return;
        if (!ReferenceEquals(combo, window._quizThemeComboBox) &&
            !ReferenceEquals(combo, window._quizFormatComboBox) &&
            !ReferenceEquals(combo, window._quizCategoryComboBox))
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(window.RefreshQuizVisualVariationPreview));
    }

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

        var variation = QuizVisualVariationPlanner.ForQuestions(_quizDraftQuestions);
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

        var planned = QuizVisualVariationPlanner.ForQuestions(_quizDraftQuestions);
        var themeKey = CurrentQuizVisualSettings().ThemeKey;
        return (planned with { ThemeKey = themeKey }).DisplayName;
    }

    private void RefreshQuizVisualVariationPreview()
    {
        if (_quizPreviewSurface is null || _quizPreviewImage is null)
            return;

        ClearQuizVisualVariationPreview(resetImage: true);
        if (_quizDraftQuestions.Count == 0)
            return;

        var vertical = _quizFormatComboBox?.SelectedIndex == 1;
        var quizType = IsLogoQuizSelected() ? QuizTypeCatalog.Logo : QuizTypeCatalog.Standard;
        if (!QuizVisualVariationPlanner.Applies(vertical, quizType))
            return;

        var planned = QuizVisualVariationPlanner.ForQuestions(_quizDraftQuestions);
        var layout = QuizCardLayoutCatalog.Resolve(planned.LayoutKey);
        var theme = QuizVisualThemeCatalog.Resolve(CurrentQuizVisualSettings().ThemeKey);
        var width = _quizPreviewSurface.Width;
        var height = _quizPreviewSurface.Height;

        if (layout.RailSide == QuizCardRailSide.None)
        {
            AddPreviewFrame(theme, layout.EdgeInset);
            return;
        }

        _quizPreviewImage.Width = width * layout.CardScale;
        _quizPreviewImage.Height = height * layout.CardScale;
        _quizPreviewImage.VerticalAlignment = VerticalAlignment.Center;
        _quizPreviewImage.HorizontalAlignment = layout.RailSide == QuizCardRailSide.Left
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        _quizPreviewImage.Margin = layout.RailSide == QuizCardRailSide.Left
            ? new Thickness(0, 0, layout.EdgeInset, 0)
            : new Thickness(layout.EdgeInset, 0, 0, 0);

        var accent = theme.Accent;
        var secondary = theme.Countdown;
        var rail = new Border
        {
            Width = layout.RailWidth,
            Margin = new Thickness(layout.EdgeInset),
            HorizontalAlignment = layout.RailSide == QuizCardRailSide.Left
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(20),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(210, accent.R, accent.G, accent.B), 0),
                    new(Color.FromArgb(142, secondary.R, secondary.G, secondary.B), 0.55),
                    new(Color.FromArgb(205, accent.R, accent.G, accent.B), 1),
                },
                new Point(0, 0),
                new Point(0, 1)),
            IsHitTestVisible = false,
        };
        AddPreviewDecoration(rail);

        var line = new Border
        {
            Width = 5,
            Margin = layout.RailSide == QuizCardRailSide.Left
                ? new Thickness(layout.EdgeInset + layout.RailWidth + 12, layout.EdgeInset + 12, 0, layout.EdgeInset + 12)
                : new Thickness(0, layout.EdgeInset + 12, layout.EdgeInset + layout.RailWidth + 12, layout.EdgeInset + 12),
            HorizontalAlignment = layout.RailSide == QuizCardRailSide.Left
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(220, accent.R, accent.G, accent.B)),
            IsHitTestVisible = false,
        };
        AddPreviewDecoration(line);
        AddPreviewFrame(theme, 0, _quizPreviewImage);
    }

    private void AddPreviewFrame(QuizVisualTheme theme, double inset, FrameworkElement? relativeTo = null)
    {
        var accent = theme.Accent;
        var secondary = theme.Countdown;
        var outer = new Border
        {
            Margin = relativeTo is null
                ? new Thickness(inset)
                : relativeTo.Margin,
            Width = relativeTo?.Width ?? double.NaN,
            Height = relativeTo?.Height ?? double.NaN,
            HorizontalAlignment = relativeTo?.HorizontalAlignment ?? HorizontalAlignment.Stretch,
            VerticalAlignment = relativeTo?.VerticalAlignment ?? VerticalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(Color.FromArgb(205, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(7),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        AddPreviewDecoration(outer);

        var innerMargin = relativeTo is null
            ? new Thickness(inset + 10)
            : relativeTo.HorizontalAlignment == HorizontalAlignment.Right
                ? new Thickness(0, 10, relativeTo.Margin.Right + 10, 10)
                : new Thickness(relativeTo.Margin.Left + 10, 10, 0, 10);
        var inner = new Border
        {
            Margin = innerMargin,
            Width = relativeTo is null ? double.NaN : Math.Max(1, relativeTo.Width - 20),
            Height = relativeTo is null ? double.NaN : Math.Max(1, relativeTo.Height - 20),
            HorizontalAlignment = relativeTo?.HorizontalAlignment ?? HorizontalAlignment.Stretch,
            VerticalAlignment = relativeTo?.VerticalAlignment ?? VerticalAlignment.Stretch,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, secondary.R, secondary.G, secondary.B)),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        AddPreviewDecoration(inner);
    }

    private void AddPreviewDecoration(UIElement decoration)
    {
        if (_quizPreviewSurface is null)
            return;
        _quizPreviewSurface.Children.Add(decoration);
        _quizVisualVariationPreviewDecorations.Add(decoration);
    }

    private void ClearQuizVisualVariationPreview(bool resetImage = true)
    {
        if (_quizPreviewSurface is not null)
        {
            foreach (var decoration in _quizVisualVariationPreviewDecorations)
                _quizPreviewSurface.Children.Remove(decoration);
        }
        _quizVisualVariationPreviewDecorations.Clear();

        if (!resetImage || _quizPreviewImage is null)
            return;
        _quizPreviewImage.Width = double.NaN;
        _quizPreviewImage.Height = double.NaN;
        _quizPreviewImage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _quizPreviewImage.VerticalAlignment = VerticalAlignment.Stretch;
        _quizPreviewImage.Margin = new Thickness(0);
    }
}
