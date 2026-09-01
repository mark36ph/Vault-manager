using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    private const string QuizBatchRenderButtonTag = "QuizBatchRenderButton";
    private static readonly bool QuizBatchRenderUiRegistered = RegisterQuizBatchRenderUi();
    private bool _quizBatchRenderRunning;

    private sealed record QuizBatchRenderItemResult(
        int Number,
        string ProjectFolder,
        string VideoPath,
        string YouTubeTitle,
        int WarningCount);

    private static bool RegisterQuizBatchRenderUi()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuizBatchRenderTarget_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void QuizBatchRenderTarget_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button renderButton ||
            !string.Equals(renderButton.Content?.ToString(), NativeQuizFinalRenderButtonText, StringComparison.Ordinal) ||
            Window.GetWindow(renderButton) is not MainShellWindow window ||
            renderButton.Parent is not Grid controls ||
            window._quizFormatComboBox?.Parent != controls)
        {
            return;
        }

        if (controls.Children.OfType<FrameworkElement>().Any(child =>
                string.Equals(child.Tag?.ToString(), QuizBatchRenderButtonTag, StringComparison.Ordinal)))
        {
            return;
        }

        var column = Grid.GetColumn(renderButton);
        controls.Children.Remove(renderButton);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Tag = QuizBatchRenderButtonTag,
        };
        Grid.SetColumn(actions, column);
        controls.Children.Add(actions);

        var batchButton = new Button
        {
            Content = "Batch Render...",
            Tag = QuizBatchRenderButtonTag,
            Height = renderButton.Height,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(0),
            ToolTip = "Build and render several fresh quiz videos sequentially using the current Builder rotation rules and Export settings.",
        };
        batchButton.Click += window.BatchRenderQuizVideos_Click;
        actions.Children.Add(batchButton);

        renderButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(renderButton);
    }

    private async void BatchRenderQuizVideos_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBatchRenderRunning)
            return;

        var requested = AskQuizBatchCount();
        if (requested is null)
            return;

        var batchButton = sender as Button;
        var progressWindow = CreateQuizBatchProgressWindow(
            requested.Value,
            out var progressText,
            out var progressBar,
            out var detailText);
        var completed = new List<QuizBatchRenderItemResult>();
        var failed = new List<(int Number, string Error)>();

        _quizBatchRenderRunning = true;
        if (batchButton is not null)
            batchButton.IsEnabled = false;

        progressWindow.Show();
        try
        {
            for (var index = 0; index < requested.Value; index++)
            {
                var number = index + 1;
                progressText.Text = $"Quiz {number:N0} of {requested.Value:N0}";
                progressBar.Value = index * 100.0 / requested.Value;
                detailText.Text = "Selecting fresh questions with rotation rules...";
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var result = await RenderOneBatchQuizAsync(
                        number,
                        requested.Value,
                        stage =>
                        {
                            detailText.Text = stage;
                            if (_quizPageStatusText is not null)
                                _quizPageStatusText.Text = $"Batch {number}/{requested.Value}: {stage}";
                        });
                    completed.Add(result);
                    progressBar.Value = number * 100.0 / requested.Value;
                }
                catch (Exception error)
                {
                    failed.Add((number, error.Message));
                    detailText.Text = $"Quiz {number:N0} failed; continuing with the next item...";
                    progressBar.Value = number * 100.0 / requested.Value;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            _quizBatchRenderRunning = false;
            if (batchButton is not null)
                batchButton.IsEnabled = true;
            progressWindow.Close();
        }

        RefreshQuizBank();
        RefreshQuizDraftUsageCounts();
        RefreshQuizHistory();
        RefreshQuizPreview();
        RefreshQuizPublishingSeries();

        if (_quizPageStatusText is not null)
            _quizPageStatusText.Text = failed.Count == 0
                ? $"Batch complete: {completed.Count:N0} final video{(completed.Count == 1 ? "" : "s")} rendered"
                : $"Batch complete: {completed.Count:N0} rendered, {failed.Count:N0} failed";

        ShowQuizBatchSummary(requested.Value, completed, failed);
    }

    private int? AskQuizBatchCount()
    {
        var countBox = new TextBox
        {
            Text = "5",
            MinWidth = 90,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "How many fresh quizzes do you want to render?",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(countBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Each quiz is built with the current category, question count, difficulty, rotation rules and Export settings. They render one at a time so narration and FFmpeg do not compete for resources.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
        var start = new Button { Content = "Start batch", MinWidth = 110, Height = 34, FontWeight = FontWeights.SemiBold };
        actions.Children.Add(cancel);
        actions.Children.Add(start);
        panel.Children.Add(actions);

        var dialog = new Window
        {
            Owner = this,
            Title = "Batch Render Final Videos",
            Width = 520,
            Height = 275,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) =>
        {
            if (!int.TryParse(countBox.Text.Trim(), out var count) || count is < 2 or > 20)
            {
                MessageBox.Show(dialog, "Enter a batch size from 2 to 20.", "Batch Render", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.Tag = count;
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true || dialog.Tag is not int result)
            return null;
        return result;
    }

    private Window CreateQuizBatchProgressWindow(
        int count,
        out TextBlock progressText,
        out ProgressBar progressBar,
        out TextBlock detailText)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Rendering {count:N0} quiz videos unattended",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });

        progressText = new TextBlock { Text = $"Quiz 1 of {count:N0}", FontWeight = FontWeights.SemiBold };
        panel.Children.Add(progressText);
        progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(0, 8, 0, 10) };
        panel.Children.Add(progressBar);
        detailText = new TextBlock
        {
            Text = "Preparing batch...",
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(detailText);
        panel.Children.Add(new TextBlock
        {
            Text = "You can leave the app running. A single summary will appear when the batch finishes; individual success dialogs are suppressed.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        });

        return new Window
        {
            Owner = this,
            Title = "Quiz Batch Render",
            Width = 520,
            Height = 245,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };
    }

    private async Task<QuizBatchRenderItemResult> RenderOneBatchQuizAsync(
        int batchNumber,
        int batchTotal,
        Action<string> progress)
    {
        if (_quizTitleTextBox is null || _quizFormatComboBox is null || _quizSecondsPerQuestionTextBox is null)
            throw new InvalidOperationException("Quiz export controls are not ready.");

        var draft = BuildBatchQuizDraft();
        _quizDraftQuestions = draft;
        if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
            throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");
        _quizSecondsPerQuestion = seconds;
        RefreshQuizDraftEditorGrid(_quizDraftQuestions.FirstOrDefault()?.Id);
        ApplyAutomaticQuizVisualVariationForDraft();
        await Dispatcher.Yield(DispatcherPriority.Background);

        var title = ProjectPathSecurity.ValidateSegment(_quizTitleTextBox.Text, "Quiz title");
        var vertical = _quizFormatComboBox.SelectedIndex == 1;
        var settings = _data.LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.ProjectsFolder))
            throw new InvalidOperationException("Set the Projects Folder in Settings before creating quiz videos.");

        var logoPath = (_quizLogoPathTextBox?.Text ?? "").Trim();
        if (logoPath.Length > 0)
        {
            logoPath = QuizBranding.ValidateLogoPath(logoPath);
            _data.SaveQuizLogoPath(logoPath);
        }

        var shuffleAnswers = _quizShuffleAnswersCheckBox?.IsChecked == true;
        var showCountdown = _quizCountdownCheckBox?.IsChecked != false;
        var animateReveal = _quizRevealAnimationCheckBox?.IsChecked != false;
        var narrate = _quizNarrationCheckBox?.IsChecked == true;
        var narrateAnswers = narrate && _quizNarrateAnswersCheckBox?.IsChecked == true;
        var selectedVoice = QuizVoiceCatalog.Validate(Convert.ToString(_quizVoiceComboBox?.SelectedItem) ?? QuizVoiceCatalog.DefaultVoice);
        var countdownTicks = showCountdown && _quizCountdownTickCheckBox?.IsChecked == true;
        var answerRevealSfx = _quizAnswerRevealSfxCheckBox?.IsChecked == true;
        var useBackgroundMusic = _quizBackgroundMusicCheckBox?.IsChecked == true;
        var exportQuestions = shuffleAnswers
            ? QuizAnswerShuffler.Shuffle(_quizDraftQuestions)
            : _quizDraftQuestions.ToList();

        var options = new QuizVideoBuildOptions(
            title,
            QuestionSeconds: seconds,
            AnswerSeconds: 3,
            Vertical: vertical,
            FrameRate: settings.FrameRate > 0 ? settings.FrameRate : 30,
            QuizLogoPath: logoPath,
            ShowCountdown: showCountdown,
            AnimateAnswerReveal: animateReveal);
        var visual = CurrentQuizVisualSettings();
        var publishing = CurrentQuizPublishMetadata(exportQuestions, vertical);
        var thumbnail = CurrentQuizThumbnailSettings(publishing);

        var preflight = QuizPreflight.Analyze(exportQuestions, options, visual.QuizType);
        var preflightErrors = preflight.Where(issue => issue.Severity == QuizPreflightSeverity.Error).ToList();
        if (preflightErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Quiz preflight failed: " +
                string.Join(" | ", preflightErrors.Select(issue => issue.Message)));
        }
        var warningCount = preflight.Count(issue => issue.Severity == QuizPreflightSeverity.Warning);

        var exportFolderName = QuizExportFolderNaming.BaseName(title, vertical);
        var stagingRoot = QuizExportStaging.CreateSessionRoot();
        var quizFolder = ProjectPathSecurity.CombineContained(stagingRoot, "Quizzes", exportFolderName);
        var voiceFolder = ProjectPathSecurity.CombineContained(stagingRoot, "Quizzes", exportFolderName, "Voice");
        var audioFolder = ProjectPathSecurity.CombineContained(stagingRoot, "Quizzes", exportFolderName, "Audio");
        Directory.CreateDirectory(quizFolder);

        try
        {
            IReadOnlyDictionary<int, QuizNarrationAsset> narrationByQuestion = new Dictionary<int, QuizNarrationAsset>();
            if (narrate)
            {
                var credentials = NativeProviderCredentials.FromSettings(settings);
                var apiKey = credentials.Get("openai");
                Directory.CreateDirectory(voiceFolder);

                using var speech = new NativeQuizSpeechProvider(apiKey, voice: selectedVoice);
                var media = new NativeFfmpegTimelineService();
                var generated = new Dictionary<int, QuizNarrationAsset>();
                for (var index = 0; index < exportQuestions.Count; index++)
                {
                    progress($"Quiz {batchNumber}/{batchTotal}: generating narration {index + 1}/{exportQuestions.Count}...");
                    var question = exportQuestions[index];
                    var path = await speech.GenerateQuestionAsync(
                        question,
                        index + 1,
                        narrateAnswers,
                        voiceFolder);
                    var duration = await media.MediaDurationAsync(path);
                    generated[question.Id] = new QuizNarrationAsset(question.Id, path, duration);
                }
                narrationByQuestion = generated;
            }

            QuizAudioCue? countdownTick = countdownTicks
                ? QuizAudioCueFactory.EnsureCountdownTick(audioFolder)
                : null;
            QuizAudioCue? revealChime = answerRevealSfx
                ? QuizAudioCueFactory.EnsureAnswerReveal(audioFolder)
                : null;

            QuizPreparedBackgroundMusic? backgroundMusic = null;
            var narrationSeconds = narrationByQuestion.Values.Sum(asset => asset.Duration);
            if (useBackgroundMusic)
            {
                progress($"Quiz {batchNumber}/{batchTotal}: preparing background music...");
                var sourceMusic = QuizMusicFile.Validate(_quizBackgroundMusicPathTextBox?.Text);
                var windows = QuizAudioTimelinePlanner.BuildNarrationWindows(exportQuestions, options, narrationByQuestion);
                var totalDuration = options.EstimatedDuration(exportQuestions.Count, narrationSeconds);
                backgroundMusic = await new NativeQuizBackgroundMusicRenderer().RenderAsync(
                    sourceMusic,
                    audioFolder,
                    totalDuration,
                    windows);
            }

            progress($"Quiz {batchNumber}/{batchTotal}: rendering cards...");
            var result = await Task.Run(() =>
            {
                var rendered = new NativeQuizVideoBuilder().BuildAndExport(
                    exportQuestions,
                    options,
                    stagingRoot,
                    narrationByQuestion);
                new QuizThemedCardRenderer().OverwriteCards(
                    rendered.ProjectFolder,
                    exportQuestions,
                    options,
                    narrationByQuestion,
                    visual);
                rendered = QuizAudioTimelineAugmenter.ApplyAndReExport(
                    rendered,
                    exportQuestions,
                    options,
                    new QuizAudioAssets(
                        countdownTick,
                        revealChime,
                        backgroundMusic,
                        narrate ? selectedVoice : ""));
                return QuizVisualExportRewriter.ReExport(rendered, exportQuestions, options);
            });

            progress($"Quiz {batchNumber}/{batchTotal}: creating thumbnail and metadata...");
            var renderedProjectFolder = result.ProjectFolder;
            var thumbnailPath = await Task.Run(() =>
            {
                var path = new QuizThumbnailRenderer().Write(
                    renderedProjectFolder,
                    publishing,
                    exportQuestions,
                    thumbnail,
                    visual,
                    logoPath,
                    vertical);
                QuizPublishMetadataFiles.Write(renderedProjectFolder, publishing);
                return path;
            });

            progress($"Quiz {batchNumber}/{batchTotal}: rendering final MP4 with FFmpeg...");
            var stagedThumbnailPath = thumbnailPath;
            var stagedResult = result;
            result = await Task.Run(() => QuizExportStaging.Publish(stagedResult, settings.ProjectsFolder, stagingRoot));
            thumbnailPath = Path.Combine(result.ProjectFolder, Path.GetFileName(stagedThumbnailPath));
            var finalVideoPath = NativeQuizFinalRenderer.OutputPath(result.ProjectFolder);
            if (!File.Exists(finalVideoPath) || new FileInfo(finalVideoPath).Length == 0)
                throw new InvalidOperationException("Final MP4 was not created for this batch item.");

            progress($"Quiz {batchNumber}/{batchTotal}: recording history...");
            var historyId = _data.RecordQuizExport(
                title,
                _quizDraftQuestions,
                vertical,
                seconds,
                shuffleAnswers,
                result.ProjectFolder,
                publishing);
            MarkQuizPublishingExportComplete(
                result.ProjectFolder,
                thumbnailPath,
                result.ResolveExport.FcpXml.Path,
                historyId);

            RefreshQuizDraftUsageCounts();
            RefreshQuizHistory();
            RefreshQuizPublishingSeries();

            if (_quizDraftStatusText is not null)
                _quizDraftStatusText.Text = $"Batch {batchNumber}/{batchTotal} ready • {publishing.SeriesName} {publishing.EpisodeLabel} • {Path.GetFileName(finalVideoPath)}";
            if (_quizPublishingStatusText is not null)
                _quizPublishingStatusText.Text = $"Batch output ready: {publishing.YouTubeTitle}";

            return new QuizBatchRenderItemResult(
                batchNumber,
                result.ProjectFolder,
                finalVideoPath,
                publishing.YouTubeTitle,
                warningCount);
        }
        catch
        {
            TryDeleteQuizBatchStaging(stagingRoot);
            throw;
        }
    }

    private List<QuizQuestion> BuildBatchQuizDraft()
    {
        if (_quizQuestionCountTextBox is null || _quizSecondsPerQuestionTextBox is null)
            throw new InvalidOperationException("Quiz Builder controls are not ready.");
        if (!int.TryParse(_quizQuestionCountTextBox.Text.Trim(), out var count) || count is < 1 or > 100)
            throw new ArgumentException("Question count must be a whole number from 1 to 100.");
        if (!int.TryParse(_quizSecondsPerQuestionTextBox.Text.Trim(), out var seconds) || seconds is < 2 or > 60)
            throw new ArgumentException("Seconds per question must be a whole number from 2 to 60.");

        var preferLeastUsed = _quizPreferLeastUsedCheckBox?.IsChecked == true;
        var avoidRecent = _quizAvoidRecentCheckBox?.IsChecked == true;
        var recentQuizCount = 0;
        if (avoidRecent)
        {
            if (_quizRecentQuizCountTextBox is null ||
                !int.TryParse(_quizRecentQuizCountTextBox.Text.Trim(), out recentQuizCount) ||
                recentQuizCount is < 1 or > 50)
            {
                throw new ArgumentException("Recent quiz avoidance must be a whole number from 1 to 50.");
            }
        }

        var category = SelectedQuizCategory();
        var difficulty = SelectedQuizDifficulty();
        var preset = _quizModeComboBox?.SelectedItem as QuizBuilderModePreset;
        var marathon = QuizMarathonPlanner.IsMarathonPreset(preset);
        if (marathon && !QuizMarathonPlanner.IsSupportedQuestionCount(count))
            throw new InvalidOperationException("Marathon Mode uses 30, 50, or 100 questions.");
        if (marathon && !string.IsNullOrWhiteSpace(difficulty))
            throw new InvalidOperationException("Marathon Mode requires All difficulties.");
        if (marathon && !QuizMarathonPlanner.IsSupportedTheme(category))
            throw new InvalidOperationException("Marathon Mode currently supports Space, Technology, or All categories.");

        IReadOnlyList<QuizQuestion> matching;
        if (marathon && string.IsNullOrWhiteSpace(category))
        {
            matching = _data.GetQuizQuestions(
                    category: "Space",
                    difficulty: "",
                    limit: 10_000,
                    enabledOnly: true)
                .Concat(_data.GetQuizQuestions(
                    category: "Technology",
                    difficulty: "",
                    limit: 10_000,
                    enabledOnly: true))
                .GroupBy(question => question.Id)
                .Select(group => group.First())
                .ToList();
        }
        else
        {
            matching = _data.GetQuizQuestions(
                category: category,
                difficulty: difficulty,
                limit: 10_000,
                enabledOnly: true,
                imageOnly: IsLogoQuizSelected(),
                excludeCategory: QuizTypeCatalog.ExcludedRandomCategory(category));
        }

        var recentIds = avoidRecent
            ? _data.GetRecentQuizQuestionIds(recentQuizCount)
            : new HashSet<int>();

        return (marathon
                ? QuizMarathonPlanner.Select(matching, count, category, preferLeastUsed, recentIds)
                : QuizDifficultyProgressionSelector.Applies(count, difficulty)
                    ? QuizDifficultyProgressionSelector.Select(matching, count, preferLeastUsed, recentIds)
                    : QuizRotationSelector.Select(matching, count, preferLeastUsed, recentIds))
            .ToList();
    }

    private void ShowQuizBatchSummary(
        int requested,
        IReadOnlyList<QuizBatchRenderItemResult> completed,
        IReadOnlyList<(int Number, string Error)> failed)
    {
        var warningTotal = completed.Sum(item => item.WarningCount);
        var lines = new List<string>
        {
            $"Batch finished: {completed.Count:N0} of {requested:N0} final videos created.",
        };
        if (warningTotal > 0)
            lines.Add($"Layout warnings encountered: {warningTotal:N0} (non-blocking). ");

        if (completed.Count > 0)
        {
            lines.Add("");
            lines.Add("Created:");
            foreach (var item in completed.Take(8))
                lines.Add($"#{item.Number}: {item.YouTubeTitle}\n{item.VideoPath}");
            if (completed.Count > 8)
                lines.Add($"...and {completed.Count - 8:N0} more.");
        }

        if (failed.Count > 0)
        {
            lines.Add("");
            lines.Add("Failed:");
            foreach (var item in failed.Take(6))
                lines.Add($"#{item.Number}: {item.Error}");
            if (failed.Count > 6)
                lines.Add($"...and {failed.Count - 6:N0} more failures.");
        }

        MessageBox.Show(
            this,
            string.Join("\n", lines),
            "Quiz Batch Render",
            MessageBoxButton.OK,
            failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void TryDeleteQuizBatchStaging(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Staging cleanup is best-effort; a later app cleanup can remove locked files.
        }
    }
}
