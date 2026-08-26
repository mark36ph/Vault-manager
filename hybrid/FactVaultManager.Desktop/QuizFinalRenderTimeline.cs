namespace FactVaultManager.Desktop;

public static class QuizFinalRenderTimeline
{
    public const string PrimaryVideoTrackName = "Quiz Cards";

    public static NativeTimeline Prepare(NativeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timeline.Validate();

        var prepared = timeline.Clone();
        var videoTracks = prepared.Tracks
            .Where(track => track.Kind == NativeTimelineTrackKind.Video)
            .ToList();
        var primary = videoTracks.FirstOrDefault(track =>
            string.Equals(track.Name, PrimaryVideoTrackName, StringComparison.Ordinal));

        // The quiz builder owns a canonical, fully-rendered Quiz Cards track. Later
        // production passes can leave auxiliary video tracks on the timeline, but the
        // final MP4 renderer needs the baked quiz cards plus one animated background.
        // Only normalize timelines that carry that explicit quiz-builder contract so
        // unrelated/native timelines retain the renderer's strict validation.
        if (primary is null)
            return prepared;

        var background = videoTracks.FirstOrDefault(track =>
            string.Equals(track.Name, QuizAnimatedBackground.TrackName, StringComparison.Ordinal));
        var redundantTracks = videoTracks
            .Where(track => !ReferenceEquals(track, primary) && !ReferenceEquals(track, background))
            .ToList();
        if (redundantTracks.Count == 0)
            return prepared;

        var removedTrackIds = redundantTracks
            .Select(track => track.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedClipIds = redundantTracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedTrackNames = redundantTracks
            .Select(track => string.IsNullOrWhiteSpace(track.Name) ? "(unnamed)" : track.Name)
            .ToArray();

        prepared.Tracks.RemoveAll(track => removedTrackIds.Contains(track.Id));
        foreach (var scene in prepared.Scenes)
            scene.ClipIds.RemoveAll(id => removedClipIds.Contains(id));

        prepared.Metadata["final_render_primary_video_track"] = primary.Name;
        prepared.Metadata["final_render_removed_auxiliary_video_tracks"] = removedTrackNames;
        prepared.Validate();
        return prepared;
    }
}
