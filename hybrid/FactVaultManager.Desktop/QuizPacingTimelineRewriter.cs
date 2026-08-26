namespace FactVaultManager.Desktop;

public static class QuizPacing
{
    public const double NarrationSuspenseSeconds = 0.5;
    public const double AnswerRevealPauseSeconds = 0.35;

    public static double NarrationSuspenseFor(QuizVideoBuildOptions options, double narrationSeconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !options.Vertical && narrationSeconds > 0 ? NarrationSuspenseSeconds : 0;
    }

    public static double AnswerRevealPauseFor(QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !options.Vertical && options.AnimateAnswerReveal ? AnswerRevealPauseSeconds : 0;
    }
}

public static class QuizPacingTimelineRewriter
{
    private const double Epsilon = 0.0001;

    public static double Apply(NativeTimeline timeline, QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        timeline.Validate();

        if (options.Vertical)
            return 0;
        if (timeline.Metadata.TryGetValue("quiz_pacing_applied", out var applied) &&
            applied is bool alreadyApplied && alreadyApplied)
        {
            return 0;
        }

        var insertions = new List<PacingInsertion>();
        foreach (var scene in timeline.Scenes.OrderBy(scene => scene.Start))
        {
            var narrationSeconds = SceneNarrationSeconds(scene);
            var suspenseSeconds = QuizPacing.NarrationSuspenseFor(options, narrationSeconds);
            var answerPauseSeconds = QuizPacing.AnswerRevealPauseFor(options);

            if (suspenseSeconds > 0)
            {
                var at = scene.Start + narrationSeconds;
                var source = FindVideoHoldSource(timeline, scene, at, preferBefore: false);
                insertions.Add(new PacingInsertion(
                    at,
                    suspenseSeconds,
                    scene,
                    source.Track,
                    source.Clip.Kind,
                    source.Clip.Source!,
                    source.Clip.SourceIn,
                    $"{scene.Title} Suspense Beat",
                    "suspense"));
            }

            if (answerPauseSeconds > 0)
            {
                var at = scene.Start + narrationSeconds + options.QuestionSeconds;
                var source = FindVideoHoldSource(timeline, scene, at, preferBefore: true);
                insertions.Add(new PacingInsertion(
                    at,
                    answerPauseSeconds,
                    scene,
                    source.Track,
                    source.Clip.Kind,
                    source.Clip.Source!,
                    source.Clip.SourceIn,
                    $"{scene.Title} Answer Pause",
                    "answer_pause"));
            }

            scene.Metadata["narration_suspense_seconds"] = suspenseSeconds;
            scene.Metadata["answer_reveal_pause_seconds"] = answerPauseSeconds;
        }

        var addedSeconds = 0.0;
        foreach (var insertion in insertions.OrderBy(item => item.OriginalAt))
        {
            var at = insertion.OriginalAt + addedSeconds;
            foreach (var track in timeline.Tracks)
            {
                foreach (var clip in track.Clips.Where(clip => clip.Start >= at - Epsilon))
                    clip.Start += insertion.Duration;
            }

            foreach (var scene in timeline.Scenes)
            {
                if (!ReferenceEquals(scene, insertion.Scene) && scene.Start >= at - Epsilon)
                    scene.Start += insertion.Duration;
            }
            insertion.Scene.Duration += insertion.Duration;

            var metadata = new Dictionary<string, object?>
            {
                ["quiz_card"] = insertion.CardKind,
                ["pacing_hold"] = true,
            };
            if (insertion.Scene.Metadata.TryGetValue("question_id", out var questionId))
                metadata["question_id"] = questionId;

            var hold = insertion.Track.AddClip(new NativeTimelineClip
            {
                Kind = insertion.Kind,
                Start = at,
                Duration = insertion.Duration,
                Source = insertion.Source,
                SourceIn = insertion.SourceIn,
                Name = insertion.Name,
                Metadata = metadata,
            });
            insertion.Scene.ClipIds.Add(hold.Id);
            addedSeconds += insertion.Duration;
        }

        timeline.Metadata["quiz_pacing_applied"] = true;
        timeline.Metadata["narration_suspense_seconds"] = QuizPacing.NarrationSuspenseSeconds;
        timeline.Metadata["answer_reveal_pause_seconds"] = QuizPacing.AnswerRevealPauseFor(options);
        timeline.Metadata["quiz_pacing_added_seconds"] = addedSeconds;
        timeline.Validate();
        return addedSeconds;
    }

    private static (NativeTimelineTrack Track, NativeTimelineClip Clip) FindVideoHoldSource(
        NativeTimeline timeline,
        NativeTimelineScene scene,
        double boundary,
        bool preferBefore)
    {
        var sceneIds = scene.ClipIds.ToHashSet(StringComparer.Ordinal);
        var candidates = timeline.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .Where(pair => sceneIds.Contains(pair.Clip.Id))
            .Where(pair => pair.Clip.Kind is NativeTimelineClipKind.Image or NativeTimelineClipKind.Video)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Clip.Source))
            .ToList();

        (NativeTimelineTrack Track, NativeTimelineClip Clip)? selected = preferBefore
            ? candidates
                .Where(pair => pair.Clip.Start < boundary + Epsilon && pair.Clip.End <= boundary + Epsilon)
                .OrderByDescending(pair => pair.Clip.Start)
                .Cast<(NativeTimelineTrack Track, NativeTimelineClip Clip)?>()
                .FirstOrDefault()
            : candidates
                .Where(pair => pair.Clip.Start >= boundary - Epsilon)
                .OrderBy(pair => pair.Clip.Start)
                .Cast<(NativeTimelineTrack Track, NativeTimelineClip Clip)?>()
                .FirstOrDefault();

        selected ??= preferBefore
            ? candidates.OrderByDescending(pair => pair.Clip.Start).Cast<(NativeTimelineTrack Track, NativeTimelineClip Clip)?>().FirstOrDefault()
            : candidates.OrderBy(pair => pair.Clip.Start).Cast<(NativeTimelineTrack Track, NativeTimelineClip Clip)?>().FirstOrDefault();

        return selected ?? throw new InvalidOperationException($"{scene.Title} has no video card available for pacing hold.");
    }

    private static double SceneNarrationSeconds(NativeTimelineScene scene)
    {
        if (!scene.Metadata.TryGetValue("narration_seconds", out var value) || value is null)
            return 0;
        try
        {
            return Math.Max(0, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException($"{scene.Title} has invalid narration timing metadata.", error);
        }
    }

    private sealed record PacingInsertion(
        double OriginalAt,
        double Duration,
        NativeTimelineScene Scene,
        NativeTimelineTrack Track,
        NativeTimelineClipKind Kind,
        string Source,
        double SourceIn,
        string Name,
        string CardKind);
}
