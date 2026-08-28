using System.Text.Json;

namespace FactVaultManager.Desktop.Tests;

public sealed class QuizAutopilotTests
{
    [Fact]
    public void PersistQuizProjectQuestionMedia_CopiesLogoAndMakesPromoProjectSelfContained()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quiz-autopilot-{Guid.NewGuid():N}");
        var sourceFolder = Path.Combine(root, "bank");
        var projectFolder = Path.Combine(root, "project");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(projectFolder);
        try
        {
            var sourceImage = Path.Combine(sourceFolder, "brand.png");
            File.WriteAllBytes(sourceImage, [1, 2, 3, 4]);
            File.WriteAllText(
                Path.Combine(projectFolder, "quiz.json"),
                """
                {
                  "title": "Logo Quiz",
                  "questions": [
                    {
                      "number": 1,
                      "id": 42,
                      "question": "Which logo is this?",
                      "answers": ["Alpha", "Beta", "Gamma", "Delta"],
                      "correct_index": 0,
                      "category": "Logos",
                      "difficulty": "insane"
                    }
                  ]
                }
                """);

            var question = new QuizQuestion(
                42,
                "Which logo is this?",
                "Alpha",
                "Beta",
                "Gamma",
                "Delta",
                0,
                "Alpha is the correct logo.",
                "Logos",
                "insane",
                "test",
                0,
                true,
                sourceImage);

            var changed = MainShellWindow.PersistQuizProjectQuestionMedia(
                projectFolder,
                new Dictionary<int, QuizQuestion> { [question.Id] = question });

            Assert.True(changed);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectFolder, "quiz.json")));
            var rootElement = document.RootElement;
            Assert.Equal(QuizTypeCatalog.Logo, rootElement.GetProperty("quiz_type").GetString());
            var savedPath = rootElement.GetProperty("questions")[0].GetProperty("image_path").GetString();
            Assert.Equal("QuestionMedia/question-42.png", savedPath);
            Assert.True(File.Exists(Path.Combine(projectFolder, "QuestionMedia", "question-42.png")));

            var promoSource = QuizPromoNativeShortRenderer.LoadVisualSource(
                projectFolder,
                "Fallback",
                "",
                42);
            Assert.Equal(QuizTypeCatalog.Logo, promoSource.Visual.QuizType);
            Assert.Equal(
                Path.Combine(projectFolder, "QuestionMedia", "question-42.png")
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                promoSource.Question.ImagePath
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
