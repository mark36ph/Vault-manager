using System.Text.Json;
using System.Text.Json.Nodes;

namespace FactVaultManager.Desktop;

internal static class LogoQuizProjectArtworkRepair
{
    private const string QuizFileName = "quiz.json";

    internal static async Task<int> RepairAsync(
        string projectFolder,
        CancellationToken cancellationToken = default)
    {
        var repaired = RepairAvailableArtwork(projectFolder, FindCachedSimpleIcon);
        var missingBrands = FindMissingLogoBrands(projectFolder);
        foreach (var brand in missingBrands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SimpleIconsService.DownloadPngAsync(
                    brand,
                    SimpleIconColourMode.Brand,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A custom or unavailable logo can remain unresolved without blocking the app.
            }
        }

        return repaired + RepairAvailableArtwork(projectFolder, FindCachedSimpleIcon);
    }

    internal static int RepairAvailableArtwork(
        string projectFolder,
        Func<string, string?> artworkResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(artworkResolver);

        projectFolder = Path.GetFullPath(projectFolder);
        var quizPath = Path.Combine(projectFolder, QuizFileName);
        if (!File.Exists(quizPath)) return 0;

        var root = ReadRoot(quizPath);
        if (!IsLogoQuiz(root) || root["questions"] is not JsonArray questions)
            return 0;

        var repaired = 0;
        for (var index = 0; index < questions.Count; index++)
        {
            if (questions[index] is not JsonObject question) continue;

            var existing = ReadString(question, "image_path");
            if (existing.Length > 0)
            {
                var resolvedExisting = Path.IsPathRooted(existing)
                    ? Path.GetFullPath(existing)
                    : Path.GetFullPath(Path.Combine(projectFolder, existing));
                if (File.Exists(resolvedExisting))
                {
                    if (!Path.IsPathRooted(existing)) continue;
                    var persisted = PersistArtwork(projectFolder, question, index, resolvedExisting);
                    question["image_path"] = persisted;
                    repaired++;
                    continue;
                }
            }

            var brand = CorrectAnswer(question);
            if (brand.Length == 0) continue;
            var source = artworkResolver(brand);
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;

            question["image_path"] = PersistArtwork(projectFolder, question, index, source);
            repaired++;
        }

        if (repaired > 0)
            WriteRootAtomically(quizPath, root);
        return repaired;
    }

    internal static string? FindCachedSimpleIcon(string brand)
    {
        var slug = SimpleIconsCatalog.CreateSlug(brand);
        if (slug.Length == 0) return null;
        var cache = Path.Combine(Path.GetTempPath(), "FactVaultManager", "simple-icons");
        foreach (var colour in new[] { "brand", "black" })
        {
            var candidate = Path.Combine(cache, $"{slug}-{colour}.png");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IReadOnlyList<string> FindMissingLogoBrands(string projectFolder)
    {
        var quizPath = Path.Combine(Path.GetFullPath(projectFolder), QuizFileName);
        if (!File.Exists(quizPath)) return Array.Empty<string>();
        var root = ReadRoot(quizPath);
        if (!IsLogoQuiz(root) || root["questions"] is not JsonArray questions)
            return Array.Empty<string>();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in questions)
        {
            if (node is not JsonObject question) continue;
            var existing = ReadString(question, "image_path");
            if (existing.Length > 0)
            {
                var path = Path.IsPathRooted(existing)
                    ? existing
                    : Path.Combine(projectFolder, existing);
                if (File.Exists(path)) continue;
            }

            var brand = CorrectAnswer(question);
            if (brand.Length > 0 && FindCachedSimpleIcon(brand) is null)
                result.Add(brand);
        }
        return result.ToArray();
    }

    private static string PersistArtwork(
        string projectFolder,
        JsonObject question,
        int index,
        string source)
    {
        source = Path.GetFullPath(source);
        var assetsFolder = Path.Combine(projectFolder, "Assets", "QuestionImages");
        Directory.CreateDirectory(assetsFolder);

        var extension = Path.GetExtension(source);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
        var id = ReadInt(question, "id", index + 1);
        var brand = CorrectAnswer(question);
        var slug = SimpleIconsCatalog.CreateSlug(brand);
        if (slug.Length == 0) slug = $"question{id}";
        var fileName = $"{index + 1:000}_{id}_{slug}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(assetsFolder, fileName);
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);

        return Path.Combine("Assets", "QuestionImages", fileName).Replace('\\', '/');
    }

    private static string CorrectAnswer(JsonObject question)
    {
        if (question["answers"] is not JsonArray answers || answers.Count == 0)
            return "";
        var index = Math.Clamp(ReadInt(question, "correct_index", 0), 0, answers.Count - 1);
        return answers[index]?.GetValue<string>()?.Trim() ?? "";
    }

    private static bool IsLogoQuiz(JsonObject root) =>
        string.Equals(
            QuizTypeCatalog.Normalize(ReadString(root, "quiz_type")),
            QuizTypeCatalog.Logo,
            StringComparison.OrdinalIgnoreCase);

    private static JsonObject ReadRoot(string quizPath)
    {
        var node = JsonNode.Parse(File.ReadAllText(quizPath)) as JsonObject;
        return node ?? throw new InvalidDataException("The saved quiz project is not a JSON object.");
    }

    private static string ReadString(JsonObject value, string name)
    {
        try { return value[name]?.GetValue<string>()?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static int ReadInt(JsonObject value, string name, int fallback)
    {
        try { return value[name]?.GetValue<int>() ?? fallback; }
        catch { return fallback; }
    }

    private static void WriteRootAtomically(string quizPath, JsonObject root)
    {
        var temporary = quizPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, quizPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
