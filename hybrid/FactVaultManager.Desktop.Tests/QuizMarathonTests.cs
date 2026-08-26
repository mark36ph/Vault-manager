namespace FactVaultManager.Desktop.Tests;

public sealed class QuizMarathonTests
{
    [Theory]
    [InlineData(30, 9, 9, 9, 3)]
    [InlineData(50, 15, 15, 15, 5)]
    [InlineData(100, 30, 30, 30, 10)]
    public void DifficultyTargets_ScaleMarathonsProgressively(
        int count,
        int easy,
        int medium,
        int hard,
        int insane)
    {
        var targets = QuizDifficultyProgressionSelector.TargetsFor(count);

        Assert.Equal([easy, medium, hard, insane], targets.Select(target => target.Count).ToArray());
        Assert.Equal(
            [QuizDifficulty.Easy, QuizDifficulty.Medium, QuizDifficulty.Hard, QuizDifficulty.Insane],
            targets.Select(target => target.Difficulty).ToArray());
        Assert.True(QuizDifficultyProgressionSelector.Applies(count, ""));
    }

    [Fact]
    public void Select_CombinedThirtyQuestionMarathonBalancesTopicsAndDifficulty()
    {
        var pool = BuildPool(perDifficultyPerTopic: 12);

        var selected = QuizMarathonPlanner.Select(
            pool,
            30,
            category: "",
            preferLeastUsed: false,
            recentlyUsedQuestionIds: new HashSet<int>(),
            random: new Random(17));

        Assert.Equal(30, selected.Count);
        Assert.Equal(9, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Easy));
        Assert.Equal(9, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Medium));
        Assert.Equal(9, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Hard));
        Assert.Equal(3, selected.Count(question => question.DifficultyLevel == QuizDifficulty.Insane));
        Assert.Equal(15, selected.Count(question => question.Category == "Space"));
        Assert.Equal(15, selected.Count(question => question.Category == "Technology"));
        Assert.Equal(
            selected.Select(question => question.DifficultyLevel).OrderBy(value => value).ToArray(),
            selected.Select(question => question.DifficultyLevel).ToArray());
    }

    [Fact]
    public void Sections_CombinedMarathonCreatesTopicRoundsAndOneFinalInsaneRound()
    {
        var selected = QuizMarathonPlanner.Select(
            BuildPool(perDifficultyPerTopic: 12),
            30,
            category: "",
            random: new Random(3));

        var sections = QuizMarathonPlanner.Sections(selected);

        Assert.Equal(7, sections.Count);
        Assert.Equal("SPACE ROUND • EASY", sections[0].Label);
        Assert.Equal("TECH ROUND • EASY", sections[1].Label);
        Assert.Equal("SPACE ROUND • MEDIUM", sections[2].Label);
        Assert.Equal("TECH ROUND • MEDIUM", sections[3].Label);
        Assert.Equal("SPACE ROUND • HARD", sections[4].Label);
        Assert.Equal("TECH ROUND • HARD", sections[5].Label);
        Assert.Equal("FINAL INSANE ROUND", sections[6].Label);
        Assert.Equal(QuizDifficulty.Insane, sections[6].Difficulty);
    }

    [Fact]
    public void Sections_SingleTopicMarathonCreatesFourDifficultySections()
    {
        var selected = QuizMarathonPlanner.Select(
            BuildPool(perDifficultyPerTopic: 12),
            30,
            category: "Space",
            random: new Random(9));

        var sections = QuizMarathonPlanner.Sections(selected);

        Assert.Equal(4, sections.Count);
        Assert.Equal(
            ["SPACE ROUND • EASY", "SPACE ROUND • MEDIUM", "SPACE ROUND • HARD", "FINAL INSANE ROUND"],
            sections.Select(section => section.Label).ToArray());
        Assert.All(selected, question => Assert.Equal("Space", question.Category));
    }

    [Fact]
    public void MarathonTheme_RejectsUnrelatedCategories()
    {
        Assert.False(QuizMarathonPlanner.IsSupportedTheme("History"));
        Assert.Throws<ArgumentException>(() => QuizMarathonPlanner.ThemeDisplayName("History"));
    }

    private static IReadOnlyList<QuizQuestion> BuildPool(int perDifficultyPerTopic)
    {
        var questions = new List<QuizQuestion>();
        var id = 1;
        foreach (var difficulty in QuizDifficultyCatalog.StorageValues)
        {
            foreach (var category in new[] { "Space", "Technology" })
            {
                for (var index = 0; index < perDifficultyPerTopic; index++)
                {
                    questions.Add(new QuizQuestion(
                        id,
                        $"{category} {difficulty} question {index + 1}?",
                        "A",
                        "B",
                        "C",
                        "D",
                        0,
                        "Explanation",
                        category,
                        difficulty,
                        "test",
                        TimesUsed: index,
                        IsEnabled: true));
                    id++;
                }
            }
        }
        return questions;
    }
}
