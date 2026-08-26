using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private bool _uploadManagerThumbnailActionsInitialized;

    internal bool InitializeUploadManagerThumbnailRegenerationActions()
    {
        if (_uploadManagerThumbnailActionsInitialized)
            return true;

        InitializeUploadManagerPage();
        if (_uploadManagerTabIndex < 0 ||
            _uploadManagerTabIndex >= MainTabs.Items.Count ||
            MainTabs.Items[_uploadManagerTabIndex] is not TabItem { Content: Border { Child: Grid root } })
            return false;

        var actions = root.Children
            .OfType<WrapPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 3);
        if (actions is null)
            return false;

        BuildCompactUploadManagerActions(actions);
        _uploadManagerThumbnailActionsInitialized = true;
        return true;
    }

    private void BuildCompactUploadManagerActions(WrapPanel actions)
    {
        actions.Children.Clear();
        actions.HorizontalAlignment = HorizontalAlignment.Right;

        var commentsButton = new Button
        {
            Content = "First Comments",
            MinWidth = 118,
            ToolTip = "Review or post the selected quiz's first comment.",
        };
        StyleQuizHistoryButton(commentsButton, Color.FromRgb(204, 70, 255));
        commentsButton.Click += (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowQuizPublishingMetadata(history, manageComments: true);
            else
                MessageBox.Show(this, "Select a quiz first.", "First Comments",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };
        actions.Children.Add(commentsButton);

        var toolsButton = BuildUploadManagerMenuButton(
            "Quiz Tools ▾",
            "Retry/reset upload state or regenerate thumbnails.",
            Color.FromRgb(0, 204, 255));
        AddUploadManagerMenuItem(toolsButton, "Retry Failed Step", async (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                await RetryFailedUploadStepsAsync(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Retry Failed Step",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuItem(toolsButton, "Reset Upload State", (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowResetUploadStateDialog(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Reset Upload State",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuSeparator(toolsButton);
        AddUploadManagerMenuItem(toolsButton, "Regenerate Thumbnail", (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                RegenerateSelectedQuizThumbnail(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Regenerate Thumbnail",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuItem(toolsButton, "Regenerate All Thumbnails", async (_, _) =>
            await RegenerateAllLongFormQuizThumbnailsAsync(toolsButton));
        actions.Children.Add(toolsButton);

        var promoButton = BuildUploadManagerMenuButton(
            "Promo Short ▾",
            "Create or upload the selected long-form quiz's promotional Short.",
            Color.FromRgb(204, 70, 255));
        AddUploadManagerMenuItem(promoButton, "Create Promo Short", (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowQuizPromoShortDialog(history);
            else
                MessageBox.Show(this, "Select a long-form quiz first.", "Create Promo Short",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        AddUploadManagerMenuItem(promoButton, "Upload Promo Short", (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowQuizPromoShortUploadDialog(history);
            else
                MessageBox.Show(this, "Select the long-form quiz first.", "Upload Promo Short",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        });
        actions.Children.Add(promoButton);

        var uploadSelected = new Button
        {
            Content = "Upload Selected",
            MinWidth = 122,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Upload the selected quiz to its configured destinations.",
        };
        StyleQuizHistoryButton(uploadSelected, Color.FromRgb(70, 235, 115));
        uploadSelected.Click += (_, _) =>
        {
            if (_uploadManagerGrid?.SelectedItem is QuizHistorySummary history)
                ShowQuizUploadDialog(history);
            else
                MessageBox.Show(this, "Select a quiz first.", "Upload Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };
        actions.Children.Add(uploadSelected);

        var uploadQueue = new Button
        {
            Content = "Upload Queue",
            MinWidth = 112,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Open the upload queue.",
        };
        StyleQuizHistoryButton(uploadQueue, Color.FromRgb(0, 204, 255));
        uploadQueue.Click += (_, _) => ShowUploadQueueDialog();
        actions.Children.Add(uploadQueue);
    }

    private Button BuildUploadManagerMenuButton(string content, string toolTip, Color accent)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 118,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = toolTip,
        };
        StyleQuizHistoryButton(button, accent);

        var panel = new StackPanel
        {
            MinWidth = 260,
        };
        var popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Top,
            PlacementTarget = button,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 18, 78)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4),
                Child = panel,
            },
            Tag = panel,
        };
        button.Tag = popup;
        button.Click += (_, _) =>
        {
            popup.PlacementTarget = button;
            popup.IsOpen = !popup.IsOpen;
        };
        return button;
    }

    private static void AddUploadManagerMenuItem(Button owner, string header, RoutedEventHandler click)
    {
        if (!TryGetUploadManagerPopup(owner, out var popup, out var panel))
            return;

        var item = new Button
        {
            Content = header,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 18, 8),
            MinWidth = 260,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Template = BuildUploadManagerPopupItemTemplate(),
        };
        item.Click += (sender, args) =>
        {
            popup.IsOpen = false;
            click(sender, args);
        };
        panel.Children.Add(item);
    }

    private static ControlTemplate BuildUploadManagerPopupItemTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ContentControl.Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        text.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(Control.FontWeight))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hover.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(28, 50, 120))));
        template.Triggers.Add(hover);

        var pressed = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true,
        };
        pressed.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(36, 62, 140))));
        template.Triggers.Add(pressed);

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
        template.Triggers.Add(disabled);
        return template;
    }

    private static void AddUploadManagerMenuSeparator(Button owner)
    {
        if (!TryGetUploadManagerPopup(owner, out _, out var panel))
            return;

        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(Color.FromRgb(47, 68, 145)),
        });
    }

    private static bool TryGetUploadManagerPopup(Button owner, out Popup popup, out StackPanel panel)
    {
        if (owner.Tag is Popup foundPopup && foundPopup.Tag is StackPanel foundPanel)
        {
            popup = foundPopup;
            panel = foundPanel;
            return true;
        }

        popup = null!;
        panel = null!;
        return false;
    }

    private void RegenerateSelectedQuizThumbnail(QuizHistorySummary history)
    {
        try
        {
            history = ResolveThumbnailHistoryEntry(history);
            var result = RegenerateHistoricalThumbnail(history, CreateQuizQuestionLookup());
            RefreshUploadManager();

            MessageBox.Show(
                this,
                $"Thumbnail regenerated.\n\n" +
                $"Featured question: {result.FeaturedQuestionNumber} of {result.QuestionCount}\n" +
                $"Hook: {result.Hook}\n\n" +
                $"Saved to:\n{result.ThumbnailPath}\n\n" +
                "The quiz video and upload records were not changed.",
                "Regenerate Thumbnail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Regenerate Thumbnail", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RegenerateAllLongFormQuizThumbnailsAsync(Button sourceButton)
    {
        ArgumentNullException.ThrowIfNull(sourceButton);
        if (MessageBox.Show(
                this,
                "Regenerate Thumbnail.png for every long-form quiz in Quiz History?\n\n" +
                "Existing thumbnails will be overwritten. Videos, Resolve projects, promo Shorts and upload records will not be changed.",
                "Regenerate All Long-Form Thumbnails",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var originalContent = sourceButton.Content;
        sourceButton.IsEnabled = false;
        try
        {
            _data.RecoverQuizHistoryProjectFolders();
            var histories = _data.GetQuizHistory(2_000)
                .Where(QuizHistoricalThumbnailRegenerator.IsBatchEligible)
                .ToList();
            if (histories.Count == 0)
            {
                MessageBox.Show(this, "There are no long-form quizzes in Quiz History.",
                    "Regenerate All Long-Form Thumbnails", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lookup = CreateQuizQuestionLookup();
            var succeeded = 0;
            var failed = new List<string>();
            for (var index = 0; index < histories.Count; index++)
            {
                var history = histories[index];
                sourceButton.Content = $"Thumbnails {index + 1}/{histories.Count}";
                try
                {
                    RegenerateHistoricalThumbnail(history, lookup);
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed.Add($"{history.UploadTitleDisplay}: {error.Message}");
                }

                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            RefreshUploadManager();
            var summary = new StringBuilder();
            summary.AppendLine($"Regenerated: {succeeded:N0}");
            summary.AppendLine($"Skipped/failed: {failed.Count:N0}");
            if (failed.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Items needing attention:");
                foreach (var failure in failed.Take(8))
                    summary.AppendLine("• " + failure);
                if (failed.Count > 8)
                    summary.AppendLine($"• …and {failed.Count - 8:N0} more");
            }
            summary.AppendLine();
            summary.Append("Only Thumbnail.png files were changed.");

            MessageBox.Show(
                this,
                summary.ToString(),
                "Regenerate All Long-Form Thumbnails",
                MessageBoxButton.OK,
                failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Regenerate All Long-Form Thumbnails", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.Content = originalContent;
            sourceButton.IsEnabled = true;
        }
    }

    private QuizHistoricalThumbnailResult RegenerateHistoricalThumbnail(
        QuizHistorySummary history,
        Func<int, QuizQuestion?> questionLookup)
    {
        var questions = _data.GetQuizHistoryQuestions(history.Id);
        var logoPath = _data.LoadQuizLogoPath();
        return QuizHistoricalThumbnailRegenerator.Regenerate(
            history,
            questions,
            questionLookup,
            logoPath);
    }

    private QuizHistorySummary ResolveThumbnailHistoryEntry(QuizHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (Directory.Exists(history.ProjectFolder))
            return history;

        _data.RecoverQuizHistoryProjectFolders();
        return _data.GetQuizHistory(2_000).FirstOrDefault(item => item.Id == history.Id)
               ?? history;
    }

    private Func<int, QuizQuestion?> CreateQuizQuestionLookup()
    {
        var cached = _data.GetQuizQuestions(limit: 10_000, enabledOnly: false)
            .GroupBy(question => question.Id)
            .ToDictionary(group => group.Key, group => group.First());

        return id =>
        {
            if (id <= 0)
                return null;
            if (cached.TryGetValue(id, out var existing))
                return existing;

            var found = _data.GetQuizQuestions(
                    search: id.ToString(CultureInfo.InvariantCulture),
                    limit: 25,
                    enabledOnly: false)
                .FirstOrDefault(question => question.Id == id);
            if (found is not null)
                cached[id] = found;
            return found;
        };
    }
}
