namespace FactVaultManager.Desktop.Tests;

public sealed class QuizPresentationWorkflowTests
{
    [Fact]
    public void ThemeCatalog_ResolvesDisplayNameAndKey()
    {
        Assert.Equal("game-show", QuizVisualThemeCatalog.Normalize("Game Show"));
        Assert.Equal("Bright", QuizVisualThemeCatalog.Resolve("bright").DisplayName);
        Assert.Contains("Minimal", QuizVisualThemeCatalog.DisplayNames);
    }

    [Fact]
    public void LogoPositionCatalog_FallsBackToBottomRight()
    {
        Assert.Equal("Top left", QuizLogoPositionCatalog.Normalize("top left"));
        Assert.Equal("Bottom right", QuizLogoPositionCatalog.Normalize("somewhere else"));
    }

    [Fact]
    public void VisualSettings_NormalizeValidatesLogoScale()
    {
        var normalized = new QuizVisualRenderSettings("Clean", "top right", 1.4).Normalize();

        Assert.Equal("clean", normalized.ThemeKey);
        Assert.Equal("Top right", normalized.LogoPosition);
        Assert.Equal(1.4, normalized.LogoScale);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QuizVisualRenderSettings("dark", "Bottom right", 2.5).Normalize());
    }

    [Fact]
    public void Preflight_WarnsWhenQuestionTextIsTooLong()
    {
        var question = Question(
            17,
            new string('Q', 180),
            "Short A",
            "Short B",
            "Short C",
            "Short D");

        var issues = QuizPreflight.Analyze([question], new QuizVideoBuildOptions("Quiz"));

        Assert.Contains(issues, issue =>
            issue.Severity == QuizPreflightSeverity.Warning &&
            issue.QuestionId == 17 &&
            issue.Message.Contains("long", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preflight_CompactQuestionPasses()
    {
        var question = Question(
            5,
            "Which planet is the largest?",
            "Earth",
            "Mars",
            "Jupiter",
            "Saturn");

        var issues = QuizPreflight.Analyze([question], new QuizVideoBuildOptions("General Knowledge Quiz"));

        Assert.Empty(issues);
        Assert.Equal("Preflight passed — no layout warnings found.", QuizPreflight.Summary(issues));
    }

    [Fact]
    public void PresetNormalize_PreservesWorkflowChoicesAndNormalizesVisuals()
    {
        var preset = new QuizPreset
        {
            Name = "  Shorts  ",
            QuestionCount = 5,
            QuestionSeconds = 7,
            Format = "VERTICAL",
            ThemeKey = "Bright",
            LogoPosition = "top left",
            LogoScale = 1.2,
            Voice = "NOVA",
            Difficulty = "MEDIUM",
        }.Normalize();

        Assert.Equal("Shorts", preset.Name);
        Assert.Equal("vertical", preset.Format);
        Assert.Equal("bright", preset.ThemeKey);
        Assert.Equal("Top left", preset.LogoPosition);
        Assert.Equal("nova", preset.Voice);
        Assert.Equal("medium", preset.Difficulty);
    }

    private static QuizQuestion Question(
        int id,
        string text,
        string a,
        string b,
        string c,
        string d) => new(
            id,
            text,
            a,
            b,
            c,
            d,
            2,
            "A short explanation.",
            "General Knowledge",
            "easy",
            "Test",
            0,
            true);
}
