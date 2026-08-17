namespace FactVaultManager.Desktop.Tests;

public sealed class QuizThumbnailTests
{
    [Fact]
    public void Defaults_BuildsScoreHeadlineAndEpisodeSubtitle()
    {
        var metadata = QuizPublishMetadataGenerator.Generate(
            "Science Challenge",
            8,
            [Question(1), Question(2), Question(3)],
            vertical: false);

        var thumbnail = QuizThumbnailDefaults.Create(metadata, 3);

        Assert.Equal("CAN YOU SCORE 3/3?", thumbnail.Headline);
        Assert.Equal("Science Challenge #008", thumbnail.Subtitle);
    }

    [Fact]
    public void Settings_NormalizeRejectsMissingOrOverlongCopy()
    {
        Assert.Throws<ArgumentException>(() => new QuizThumbnailSettings("", "Subtitle").Normalize());
        Assert.Throws<ArgumentException>(() => new QuizThumbnailSettings("Headline", "").Normalize());
        Assert.Throws<ArgumentException>(() =>
            new QuizThumbnailSettings(new string('H', QuizThumbnailSettings.MaxHeadlineLength + 1), "Subtitle").Normalize());
    }

    [Fact]
    public void Checklist_OnlyMarksUploadPackageCompleteAfterExport()
    {
        var beforeExport = QuizPublishChecklist.Evaluate(
            draftQuestionCount: 10,
            metadataReady: true,
            thumbnailReady: true,
            preflightReady: true,
            exportSettingsReady: true,
            exportCompleted: false);
        var afterExport = QuizPublishChecklist.Evaluate(
            draftQuestionCount: 10,
            metadataReady: true,
            thumbnailReady: true,
            preflightReady: true,
            exportSettingsReady: true,
            exportCompleted: true);

        Assert.True(beforeExport.Take(5).All(item => item.IsComplete));
        Assert.False(beforeExport[^1].IsComplete);
        Assert.True(afterExport.All(item => item.IsComplete));
        Assert.Contains("Upload package", QuizPublishChecklist.Format(afterExport));
    }

    private static QuizQuestion Question(int id) => new(
        id,
        $"Question {id}?",
        "Answer A",
        "Answer B",
        "Answer C",
        "Answer D",
        0,
        "Explanation",
        "Science",
        "medium",
        "Test",
        0);
}
