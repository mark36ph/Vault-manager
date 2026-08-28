namespace FactVaultManager.Desktop;

public static class QuizLongFormZeroIntro
{
    private const double Epsilon = 0.0001;

    public static void RenderAndApply(
        NativeTimeline timeline,
        string projectFolder,
        QuizVideoBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.Vertical)
            throw new ArgumentException("Zero-second intro is only for long-form landscape quizzes.", nameof(options));

        var trimmedSeconds = TrimTimeline(timeline);
        QuizOutroSequence.RenderAndApply(timeline, projectFolder, options);
        QuizGrowthEndScreen.Apply(timeline, projectFolder, options);
        timeline.Metadata["opening_sequence_applied"] = true;
        timeline.Metadata["opening_countdown_seconds"] = 0;
        timeline.Metadata["opening_spin_frames"] = 0;
        timeline.Metadata["zero_second_intro"] = true;
        timeline.Metadata["zero_second_intro_trimmed_seconds"] = trimmedSeconds;
        timeline.Validate();
    }

    public static double TrimTimeline(NativeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timeline.Validate();

        var videoTrack = timeline.Tracks.FirstOrDefault(track => track.Kind == NativeTimelineTrackKind.Video)
            ?? throw new InvalidOperationException("Quiz timeline has no video track.");
        var intro = videoTrack.Clips.FirstOrDefault(IsIntroClip);
        if (intro is null)
        {
            var firstSceneStart = timeline.Scenes
                .OrderBy(scene => scene.Start)
                .Select(scene => (double?)scene.Start)
                .FirstOrDefault();
            if (firstSceneStart is > Epsilon)
                throw new InvalidOperationException("Quiz timeline has no intro card to remove, but Question 1 does not start at zero.");

            timeline.Metadata["zero_second_intro"] = true;
            timeline.Metadata["zero_second_intro_trimmed_seconds"] = 0d;
            return 0;
        }
        if (intro.Start > Epsilon)
            throw new InvalidOperationException("Quiz intro must start at zero before it can be removed.");

        var cutAt = intro.End;
        foreach (var track in timeline.Tracks)
        {
            foreach (var clip in track.Clips.ToList())
            {
                if (ReferenceEquals(clip, intro))
                {
                    track.Clips.Remove(clip);
                    continue;
                }

                if (clip.End <= cutAt + Epsilon)
                {
                    track.Clips.Remove(clip);
                    continue;
                }

                if (clip.Start < cutAt - Epsilon)
                {
                    var trimmed = cutAt - clip.Start;
                    clip.SourceIn += trimmed;
                    clip.Duration -= trimmed;
                    clip.Start = 0;
                }
                else
                {
                    clip.Start = Math.Max(0, clip.Start - cutAt);
                }
            }
        }

        foreach (var scene in timeline.Scenes.ToList())
        {
            if (scene.End <= cutAt + Epsilon)
            {
                timeline.Scenes.Remove(scene);
                continue;
            }

            if (scene.Start < cutAt - Epsilon)
            {
                var trimmed = cutAt - scene.Start;
                scene.Duration -= trimmed;
                scene.Start = 0;
            }
            else
            {
                scene.Start = Math.Max(0, scene.Start - cutAt);
            }
        }

        timeline.Metadata["zero_second_intro"] = true;
        timeline.Metadata["zero_second_intro_trimmed_seconds"] = cutAt;
        timeline.Validate();
        return cutAt;
    }

    private static bool IsIntroClip(NativeTimelineClip clip) =>
        clip.Kind == NativeTimelineClipKind.Image &&
        clip.Metadata.TryGetValue("quiz_card", out var value) &&
        string.Equals(Convert.ToString(value), "intro", StringComparison.OrdinalIgnoreCase);
}
