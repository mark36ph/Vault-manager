namespace FactVaultManager.Desktop;

public sealed record WebsiteSeoAutoFixChange(
    string Field,
    string Before,
    string After);

public sealed record WebsiteSeoAutoFixProposal(
    bool CanApply,
    string Summary,
    FactburstWebsiteSeoValues Before,
    FactburstWebsiteSeoValues After,
    IReadOnlyList<WebsiteSeoAutoFixChange> Changes)
{
    public static WebsiteSeoAutoFixProposal Unavailable(FactburstWebsiteSeoValues current, string summary) =>
        new(false, summary, current, current, Array.Empty<WebsiteSeoAutoFixChange>());
}

public static class FactburstWebsiteSeoAutoFix
{
    public static WebsiteSeoAutoFixProposal Create(
        WebsiteSeoAuditRow row,
        IReadOnlyList<FactburstWebsiteSeoQuiz> allQuizzes)
    {
        ArgumentNullException.ThrowIfNull(row);
        allQuizzes ??= Array.Empty<FactburstWebsiteSeoQuiz>();

        var before = FactburstWebsiteSeoDefaults.Effective(row.Source);
        if (row.Severity != WebsiteSeoAuditSeverity.Warning)
        {
            return WebsiteSeoAutoFixProposal.Unavailable(
                before,
                row.Severity == WebsiteSeoAuditSeverity.Ready
                    ? "This quiz already passes the SEO audit."
                    : "This finding needs a manual content or publishing repair rather than an automatic SEO metadata change.");
        }

        var suggested = FactburstWebsiteSeoDefaults.Create(row.Source);
        var seoTitle = before.SeoTitle;
        var seoDescription = before.SeoDescription;
        var socialTitle = before.SocialTitle;
        var socialDescription = before.SocialDescription;

        var otherSeoTitles = allQuizzes
            .Where(quiz => !SameQuiz(quiz, row.Source))
            .Select(quiz => Normalize(FactburstWebsiteSeoDefaults.Effective(quiz).SeoTitle))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicateSeoTitle = seoTitle.Length > 0 && otherSeoTitles.Contains(Normalize(seoTitle));
        if (duplicateSeoTitle || seoTitle.Length > FactburstWebsiteSeoDefaults.RecommendedTitleLength)
            seoTitle = UniqueSeoTitle(row.Source, suggested.SeoTitle, otherSeoTitles);

        if (seoDescription.Length < 70 || seoDescription.Length > FactburstWebsiteSeoDefaults.RecommendedDescriptionLength)
            seoDescription = suggested.SeoDescription;

        if (socialTitle.Length > FactburstWebsiteSeoDefaults.RecommendedSocialTitleLength)
            socialTitle = suggested.SocialTitle;

        if (socialDescription.Length > FactburstWebsiteSeoDefaults.RecommendedSocialDescriptionLength)
            socialDescription = suggested.SocialDescription;

        var after = new FactburstWebsiteSeoValues(
            Compact(seoTitle),
            Compact(seoDescription),
            Compact(socialTitle),
            Compact(socialDescription));
        var changes = BuildChanges(before, after);
        if (changes.Count == 0)
        {
            return WebsiteSeoAutoFixProposal.Unavailable(
                before,
                "This warning cannot be cleared safely by changing only search and social metadata. Use Edit selected and follow the recommendation shown in the audit.");
        }

        var simulated = row.Source with
        {
            SeoTitle = after.SeoTitle,
            SeoDescription = after.SeoDescription,
            SocialTitle = after.SocialTitle,
            SocialDescription = after.SocialDescription,
        };
        var simulatedInventory = allQuizzes
            .Select(quiz => SameQuiz(quiz, row.Source) ? simulated : quiz)
            .ToList();
        if (!simulatedInventory.Any(quiz => SameQuiz(quiz, row.Source)))
            simulatedInventory.Add(simulated);

        var result = FactburstWebsiteSeoAudit.Build(simulatedInventory)
            .FirstOrDefault(item => SameQuiz(item.Source, simulated));
        if (result is null || result.Severity != WebsiteSeoAuditSeverity.Ready)
        {
            return WebsiteSeoAutoFixProposal.Unavailable(
                before,
                "The app can suggest metadata changes, but they would not fully clear this warning. Use Edit selected so the remaining issue can be handled explicitly.");
        }

        return new WebsiteSeoAutoFixProposal(
            true,
            $"{changes.Count} SEO field{(changes.Count == 1 ? "" : "s")} will be updated and the simulated result passes the full catalogue audit.",
            before,
            after,
            changes);
    }

    private static string UniqueSeoTitle(
        FactburstWebsiteSeoQuiz quiz,
        string suggestedTitle,
        IReadOnlySet<string> otherSeoTitles)
    {
        var candidates = new List<string>();
        AddCandidate(candidates, suggestedTitle);

        var title = Compact(quiz.Title);
        var category = Compact(quiz.Category);
        if (title.Length > 0 && category.Length > 0)
            AddCandidate(candidates, ComposeSeoTitle($"{title} – {category}"));

        var slugLabel = HumanizeSlug(quiz.Slug);
        if (slugLabel.Length > 0)
            AddCandidate(candidates, ComposeSeoTitle(slugLabel));

        foreach (var candidate in candidates)
        {
            if (candidate.Length > 0 &&
                candidate.Length <= FactburstWebsiteSeoDefaults.RecommendedTitleLength &&
                !otherSeoTitles.Contains(Normalize(candidate)))
                return candidate;
        }

        var code = StableCode(quiz.Slug);
        var baseTitle = title.Length > 0 ? title : category.Length > 0 ? category + " Quiz" : "Factburst Quiz";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var qualifier = attempt == 0 ? $"Q{code}" : $"Q{code}-{attempt + 1}";
            var candidate = ComposeSeoTitle($"{qualifier} {baseTitle}");
            if (!otherSeoTitles.Contains(Normalize(candidate)))
                return candidate;
        }

        return ComposeSeoTitle($"Q{code} {HumanizeSlug(quiz.Slug)}");
    }

    private static string ComposeSeoTitle(string core)
    {
        const string suffix = " | Factburst Quiz";
        var normalized = Compact(core);
        var maxCore = Math.Max(12, FactburstWebsiteSeoDefaults.RecommendedTitleLength - suffix.Length);
        if (normalized.Length > maxCore)
            normalized = TrimAtWord(normalized, maxCore);
        return normalized.EndsWith("Factburst Quiz", StringComparison.OrdinalIgnoreCase)
            ? TrimAtWord(normalized, FactburstWebsiteSeoDefaults.RecommendedTitleLength)
            : normalized + suffix;
    }

    private static IReadOnlyList<WebsiteSeoAutoFixChange> BuildChanges(
        FactburstWebsiteSeoValues before,
        FactburstWebsiteSeoValues after)
    {
        var changes = new List<WebsiteSeoAutoFixChange>();
        AddChange(changes, "SEO title", before.SeoTitle, after.SeoTitle);
        AddChange(changes, "Meta description", before.SeoDescription, after.SeoDescription);
        AddChange(changes, "Social title", before.SocialTitle, after.SocialTitle);
        AddChange(changes, "Social description", before.SocialDescription, after.SocialDescription);
        return changes;
    }

    private static void AddChange(List<WebsiteSeoAutoFixChange> changes, string field, string before, string after)
    {
        if (!string.Equals(Compact(before), Compact(after), StringComparison.Ordinal))
            changes.Add(new WebsiteSeoAutoFixChange(field, Compact(before), Compact(after)));
    }

    private static void AddCandidate(List<string> candidates, string? value)
    {
        var clean = Compact(value);
        if (clean.Length > 0 && !candidates.Contains(clean, StringComparer.OrdinalIgnoreCase))
            candidates.Add(clean);
    }

    private static bool SameQuiz(FactburstWebsiteSeoQuiz left, FactburstWebsiteSeoQuiz right) =>
        ReferenceEquals(left, right) ||
        (string.Equals(left.Slug, right.Slug, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));

    private static string HumanizeSlug(string? value)
    {
        var words = Compact(value)
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string StableCode(string? value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in Normalize(value))
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return (hash & 0xFFFFF).ToString("X5");
        }
    }

    private static string TrimAtWord(string value, int maxLength)
    {
        var text = Compact(value);
        if (text.Length <= maxLength) return text;
        var candidate = text[..Math.Max(1, maxLength)].TrimEnd();
        var lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace >= Math.Max(8, maxLength / 2)) candidate = candidate[..lastSpace];
        return candidate.TrimEnd(' ', '-', '–', ':', ';', ',', '.');
    }

    private static string Normalize(string? value) => Compact(value).ToLowerInvariant();

    private static string Compact(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
