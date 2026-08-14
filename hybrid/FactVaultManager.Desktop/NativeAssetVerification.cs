using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactVaultManager.Desktop;

public sealed class NativeAssetVerificationException : Exception
{
    public NativeAssetVerificationException(string message) : base(message) { }
}

public sealed record NativeAssetVerificationResult(
    bool Accepted,
    string Decision,
    string Quality,
    string Style,
    bool SubjectUncertain,
    bool ObviousMismatch,
    double MismatchConfidence,
    bool PhysicalContradiction,
    double PhysicalContradictionConfidence,
    string HardNegative,
    double HardNegativeConfidence,
    bool RequestedSubjectVisible,
    bool RequestedSceneEvidenceVisible,
    bool ExplicitSubjectContradiction,
    double ExplicitSubjectConfidence,
    string SubjectIdentityMode);

public interface INativeAssetVerifier
{
    Task<NativeAssetVerificationResult> VerifyAsync(
        string query,
        NativeAcquiredAsset asset,
        CancellationToken cancellationToken = default);
}

public sealed class NativeOpenAIImageRelevanceVerifier : INativeAssetVerifier, IDisposable
{
    private const double RejectConfidence = 0.90;
    private const double PhysicalContradictionConfidence = 0.82;
    private const double SubjectUncertainConfidence = 0.45;
    private const double HardNegativeConfidence = 0.85;
    private const double WrongNamedSubjectConfidence = 0.72;
    private const double DecorativePersonConfidence = 0.70;
    private const double SoftFormatConfidence = 0.97;
    private const double ExplicitSubjectContradictionConfidence = 0.55;

    private static readonly HashSet<string> HardNegatives = new(StringComparer.Ordinal)
    {
        "none", "wrong_named_subject", "unrequested_fantasy_creature", "unrequested_person",
        "unrequested_statue_or_sculpture", "unrequested_animal", "unrequested_vehicle_or_spacecraft",
        "unrequested_logo_or_symbol", "unrequested_generic_diagram", "other_obvious_unrelated_subject",
    };

    private static readonly HashSet<string> VisualQualities = new(StringComparer.Ordinal)
    {
        "preferred", "acceptable", "weak",
    };

    private static readonly HashSet<string> VisualStyles = new(StringComparer.Ordinal)
    {
        "literal", "representational", "decorative",
    };

    private static readonly HashSet<string> IdentityModes = new(StringComparer.Ordinal)
    {
        "visually_recognizable", "named_or_contextual",
    };

    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public NativeOpenAIImageRelevanceVerifier(
        string apiKey,
        string model = "gpt-5-mini",
        HttpClient? client = null)
    {
        _apiKey = Required(apiKey, "OpenAI API key");
        _model = Required(model, "visual verification model");
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _ownsClient = client is null;
        if (_client.DefaultRequestHeaders.Accept.Count == 0)
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("FactVaultManager/1.0");
    }

    public async Task<NativeAssetVerificationResult> VerifyAsync(
        string query,
        NativeAcquiredAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var rawQuery = query ?? "";
        var explicitSubjectGate = rawQuery.Contains("EXPLICIT-SUBJECT VISUAL REQUIREMENT:", StringComparison.Ordinal);

        if (!asset.Candidate.Kind.Equals("image", StringComparison.OrdinalIgnoreCase))
            return Accepted("non-image asset");
        if (!File.Exists(asset.Path) || new FileInfo(asset.Path).Length <= 0)
            return Rejected("missing or empty image");

        var sceneQuery = Regex.Replace(rawQuery.Trim(), @"\s+", " ");
        var candidateTitle = Regex.Replace((asset.Candidate.Title ?? "").Trim(), @"\s+", " ");
        var instruction = BuildInstruction(sceneQuery, candidateTitle);
        var dataUrl = BuildImageDataUrl(asset.Path);

        var hardNegativeValues = HardNegatives.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var qualityValues = VisualQualities.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var styleValues = VisualStyles.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var identityValues = IdentityModes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var required = new[]
        {
            "obvious_mismatch", "confidence", "physical_contradiction", "physical_contradiction_confidence",
            "hard_negative", "hard_negative_confidence", "visual_quality", "visual_style",
            "requested_subject_visible", "requested_scene_evidence_visible", "explicit_subject_contradiction",
            "explicit_subject_confidence", "subject_identity_mode",
        };

        object NumSchema() => new Dictionary<string, object?>
        {
            ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1,
        };

        var schemaProperties = new Dictionary<string, object?>
        {
            ["obvious_mismatch"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["confidence"] = NumSchema(),
            ["physical_contradiction"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["physical_contradiction_confidence"] = NumSchema(),
            ["hard_negative"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = hardNegativeValues },
            ["hard_negative_confidence"] = NumSchema(),
            ["visual_quality"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = qualityValues },
            ["visual_style"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = styleValues },
            ["requested_subject_visible"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["requested_scene_evidence_visible"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["explicit_subject_contradiction"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["explicit_subject_confidence"] = NumSchema(),
            ["subject_identity_mode"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = identityValues },
        };

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_output_tokens"] = 800,
            ["reasoning"] = new Dictionary<string, object?> { ["effort"] = "minimal" },
            ["text"] = new Dictionary<string, object?>
            {
                ["verbosity"] = "low",
                ["format"] = new Dictionary<string, object?>
                {
                    ["type"] = "json_schema",
                    ["name"] = "visual_mismatch_decision",
                    ["description"] = "Topic-neutral mismatch, explicit visual subject, identity mode, quality, and factual-style classification.",
                    ["strict"] = true,
                    ["schema"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = schemaProperties,
                        ["required"] = required,
                        ["additionalProperties"] = false,
                    },
                },
            },
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "input_text", ["text"] = instruction },
                        new Dictionary<string, object?> { ["type"] = "input_image", ["image_url"] = dataUrl, ["detail"] = "high" },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        JsonDocument responseDocument;
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new NativeAssetVerificationException($"OpenAI visual verifier HTTP {(int)response.StatusCode}: {ExtractError(responseBody)}");
            responseDocument = JsonDocument.Parse(responseBody);
        }
        catch (NativeAssetVerificationException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new NativeAssetVerificationException(error.Message);
        }

        using (responseDocument)
        {
            if (responseDocument.RootElement.ValueKind != JsonValueKind.Object)
                throw new NativeAssetVerificationException("visual verifier response must be a JSON object");
            var text = ResponseText(responseDocument.RootElement);
            var decision = ParseDecision(text);
            return ApplyThresholds(decision, explicitSubjectGate);
        }
    }

    private static NativeAssetVerificationResult ApplyThresholds(RawDecision raw, bool explicitSubjectGate)
    {
        var uncertain = raw.PhysicalContradiction &&
            raw.PhysicalContradictionConfidence >= SubjectUncertainConfidence &&
            raw.PhysicalContradictionConfidence < PhysicalContradictionConfidence;

        NativeAssetVerificationResult Result(bool accepted, string decision) => new(
            accepted, decision, raw.VisualQuality, raw.VisualStyle, uncertain,
            raw.ObviousMismatch, raw.Confidence, raw.PhysicalContradiction, raw.PhysicalContradictionConfidence,
            raw.HardNegative, raw.HardNegativeConfidence, raw.RequestedSubjectVisible,
            raw.RequestedSceneEvidenceVisible, raw.ExplicitSubjectContradiction,
            raw.ExplicitSubjectConfidence, raw.SubjectIdentityMode);

        if (explicitSubjectGate)
        {
            if (raw.SubjectIdentityMode == "visually_recognizable" &&
                !raw.RequestedSubjectVisible && !raw.RequestedSceneEvidenceVisible)
            {
                return Result(false,
                    $"explicit subject missing from pixels: identity_mode={raw.SubjectIdentityMode}, " +
                    $"subject_visible={raw.RequestedSubjectVisible}, scene_evidence_visible={raw.RequestedSceneEvidenceVisible}, " +
                    $"contradiction={raw.ExplicitSubjectContradiction}/{raw.ExplicitSubjectConfidence:0.00}");
            }
            if (raw.ExplicitSubjectContradiction && raw.ExplicitSubjectConfidence >= ExplicitSubjectContradictionConfidence)
                return Result(false, $"explicit subject contradiction ({raw.ExplicitSubjectConfidence:0.00}, threshold {ExplicitSubjectContradictionConfidence:0.00})");
        }

        if (raw.PhysicalContradiction && raw.PhysicalContradictionConfidence >= PhysicalContradictionConfidence)
            return Result(false, $"physical contradiction ({raw.PhysicalContradictionConfidence:0.00}, threshold {PhysicalContradictionConfidence:0.00})");

        var threshold = HardNegativeConfidence;
        if (raw.HardNegative == "wrong_named_subject") threshold = WrongNamedSubjectConfidence;
        else if (raw.HardNegative == "unrequested_person" && raw.VisualStyle == "decorative") threshold = DecorativePersonConfidence;
        else if (raw.HardNegative is "unrequested_logo_or_symbol" or "unrequested_generic_diagram") threshold = SoftFormatConfidence;

        if (raw.HardNegative != "none" && raw.HardNegativeConfidence >= threshold)
            return Result(false, $"hard negative {raw.HardNegative} ({raw.HardNegativeConfidence:0.00}, threshold {threshold:0.00})");
        if (raw.ObviousMismatch && raw.Confidence >= RejectConfidence)
            return Result(false, $"obvious mismatch ({raw.Confidence:0.00}, threshold {RejectConfidence:0.00})");

        var suffix = uncertain ? ", subject_uncertain" : "";
        return Result(true,
            $"kept: mismatch={raw.ObviousMismatch}/{raw.Confidence:0.00}, " +
            $"physical_contradiction={raw.PhysicalContradiction}/{raw.PhysicalContradictionConfidence:0.00}, " +
            $"hard_negative={raw.HardNegative}/{raw.HardNegativeConfidence:0.00}, " +
            $"identity_mode={raw.SubjectIdentityMode}, subject_visible={raw.RequestedSubjectVisible}, " +
            $"scene_evidence_visible={raw.RequestedSceneEvidenceVisible}, " +
            $"explicit_contradiction={raw.ExplicitSubjectContradiction}/{raw.ExplicitSubjectConfidence:0.00}, " +
            $"quality={raw.VisualQuality}, style={raw.VisualStyle}{suffix}");
    }

    private static RawDecision ParseDecision(string text)
    {
        var rawText = (text ?? "").Trim();
        if (rawText.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = rawText.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].StartsWith("```", StringComparison.Ordinal)) lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim() == "```") lines.RemoveAt(lines.Count - 1);
            rawText = string.Join("\n", lines).Trim();
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(rawText); }
        catch (JsonException error) { throw new NativeAssetVerificationException($"visual verifier returned invalid JSON: {(rawText.Length == 0 ? "<empty>" : rawText)}: {error.Message}"); }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new NativeAssetVerificationException("visual verifier decision must be a JSON object");

            var hardNegative = RequiredString(root, "hard_negative");
            var quality = RequiredString(root, "visual_quality");
            var style = RequiredString(root, "visual_style");
            var identity = root.TryGetProperty("subject_identity_mode", out var identityElement)
                ? identityElement.GetString() ?? "visually_recognizable"
                : "visually_recognizable";
            if (!HardNegatives.Contains(hardNegative)) throw new NativeAssetVerificationException($"visual verifier returned invalid hard_negative: {hardNegative}");
            if (!VisualQualities.Contains(quality)) throw new NativeAssetVerificationException($"visual verifier returned invalid visual_quality: {quality}");
            if (!VisualStyles.Contains(style)) throw new NativeAssetVerificationException($"visual verifier returned invalid visual_style: {style}");
            if (!IdentityModes.Contains(identity)) throw new NativeAssetVerificationException($"visual verifier returned invalid subject_identity_mode: {identity}");

            return new RawDecision(
                RequiredBool(root, "obvious_mismatch"), RequiredConfidence(root, "confidence"),
                RequiredBool(root, "physical_contradiction"), RequiredConfidence(root, "physical_contradiction_confidence"),
                hardNegative, RequiredConfidence(root, "hard_negative_confidence"), quality, style,
                RequiredBool(root, "requested_subject_visible"), RequiredBool(root, "requested_scene_evidence_visible"),
                RequiredBool(root, "explicit_subject_contradiction"), RequiredConfidence(root, "explicit_subject_confidence"), identity);
        }
    }

    private static string BuildInstruction(string sceneQuery, string candidateTitle) =>
        "You are a topic-neutral visual mismatch and factual-quality detector for short-form factual video stock imagery. " +
        "The scene search query is the source of truth. Judge visible pixels first; metadata is only a hint.\n\n" +
        $"Scene search query: {sceneQuery}\nStock metadata title: {candidateTitle}\n\n" +
        "Return explicit judgments for requested_subject_visible, requested_scene_evidence_visible, explicit_subject_contradiction, " +
        "explicit_subject_confidence, and subject_identity_mode. Use visually_recognizable for ordinary animals, objects, machines, " +
        "materials, vehicles and body parts whose presence can normally be judged from pixels. Use named_or_contextual for named places, " +
        "landmarks, people/entities, historical sites, celestial identities and other subjects that may be plausible without unique pixel proof. " +
        "A named/contextual subject must not be rejected merely because pixels cannot uniquely prove its name; reject when visible content contradicts it. " +
        "For a visually recognizable explicit subject, if neither the subject nor genuine scene-specific evidence is visible, it is unsafe.\n\n" +
        "Detect semantic-name collisions such as an animal versus a vehicle/company with the same word. physical_contradiction means visible defining " +
        "features conflict with the requested typed/named subject. hard_negative must be exactly one allowed category. Rate visual_quality as preferred, " +
        "acceptable or weak, and visual_style as literal, representational or decorative. A still image need not directly demonstrate an abstract action, " +
        "duration, measurement or process when the underlying subject is appropriate. Reject clear different subjects; keep plausible but non-unique named matches.";

    private static string BuildImageDataUrl(string path)
    {
        var data = File.ReadAllBytes(path);
        var mime = DetectMime(path, data);
        return $"data:{mime};base64,{Convert.ToBase64String(data)}";
    }

    private static string DetectMime(string path, byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (data.Length >= 6 && (Encoding.ASCII.GetString(data, 0, 6) is "GIF87a" or "GIF89a")) return "image/gif";
        if (data.Length >= 12 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WEBP") return "image/webp";
        if (data.Length >= 12 && Encoding.ASCII.GetString(data, 4, 4) == "ftyp")
        {
            var brand = Encoding.ASCII.GetString(data, 8, 4).ToLowerInvariant();
            if (brand is "avif" or "avis" or "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
                throw new NativeAssetVerificationException($"unsupported downloaded image format for OpenAI vision: {brand}");
        }
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => throw new NativeAssetVerificationException($"unsupported visual-verification file type: {Path.GetExtension(path)}"),
        };
    }

    private static string ResponseText(JsonElement root)
    {
        var direct = ReadString(root, "output_text").Trim();
        if (direct.Length > 0) return direct;
        var chunks = new List<string>();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in content.EnumerateArray())
                {
                    var text = ReadString(entry, "text").Trim();
                    if (text.Length > 0) chunks.Add(text);
                }
            }
        }
        if (chunks.Count > 0) return string.Join("\n", chunks);
        if (ReadString(root, "status") == "incomplete")
        {
            var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                ? ReadString(details, "reason") : "";
            throw new NativeAssetVerificationException("visual verifier response was incomplete" + (reason.Length > 0 ? $": {reason}" : ""));
        }
        return "";
    }

    private static bool RequiredBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new NativeAssetVerificationException($"visual verifier {name} must be boolean");
        return value.GetBoolean();
    }

    private static double RequiredConfidence(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetDouble(out var number) || number < 0 || number > 1)
            throw new NativeAssetVerificationException($"visual verifier {name} must be between 0 and 1");
        return number;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = ReadString(root, name).Trim();
        if (value.Length == 0) throw new NativeAssetVerificationException($"visual verifier {name} is required");
        return value;
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
            : "";

    private static string ExtractError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var message = ReadString(error, "message").Trim();
                if (message.Length > 0) return message;
            }
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] : body;
    }

    private static NativeAssetVerificationResult Accepted(string decision) => new(
        true, decision, "preferred", "literal", false, false, 0, false, 0,
        "none", 0, false, false, false, 0, "visually_recognizable");

    private static NativeAssetVerificationResult Rejected(string decision) => new(
        false, decision, "weak", "literal", false, true, 1, false, 0,
        "other_obvious_unrelated_subject", 1, false, false, true, 1, "visually_recognizable");

    private static string Required(string? value, string name)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException($"{name} is required");
        return text;
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private sealed record RawDecision(
        bool ObviousMismatch,
        double Confidence,
        bool PhysicalContradiction,
        double PhysicalContradictionConfidence,
        string HardNegative,
        double HardNegativeConfidence,
        string VisualQuality,
        string VisualStyle,
        bool RequestedSubjectVisible,
        bool RequestedSceneEvidenceVisible,
        bool ExplicitSubjectContradiction,
        double ExplicitSubjectConfidence,
        string SubjectIdentityMode);
}

public sealed class NativeNamedSubjectVerifier : INativeAssetVerifier
{
    private const double ExplicitRejectConfidence = 0.55;

    private static readonly HashSet<string> BroadAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        "space", "science", "nature", "history", "technology", "engineering", "health", "medicine",
        "animals", "animal", "ocean", "geography", "physics", "chemistry", "biology", "astronomy",
        "earth", "environment", "transport", "architecture", "geology",
    };

    private static readonly HashSet<string> GenericCapitalized = new(StringComparer.Ordinal)
    {
        "Aerial", "Close", "Documentary", "Realistic", "Scientific", "Vertical", "Wide", "Landscape",
        "Photo", "Photography", "Video", "Image",
    };

    private static readonly HashSet<string> TwoWordPrefixes = new(StringComparer.Ordinal)
    {
        "Mount", "Mt", "Mauna", "Lake", "Cape", "Fort", "Saint", "St",
    };

    private static readonly HashSet<string> EntityTerminals = new(StringComparer.Ordinal)
    {
        "Bridge", "Reef", "Telescope", "Station", "Tower", "Building", "Palace", "Temple", "Cathedral",
        "Church", "Mosque", "River", "Sea", "Ocean", "Lake", "Island", "Falls", "Dam", "Park", "City",
        "University", "Museum", "Airport", "Volcano", "Mountain", "Peak", "Monument", "Castle", "Canal",
        "Desert", "Forest", "Bay", "Gulf", "Peninsula", "Spacecraft", "Rover", "Tomb", "Wall", "Capitol",
        "Center", "Centre",
    };

    private static readonly HashSet<string> LowercaseNamedTerminals = new(StringComparer.OrdinalIgnoreCase)
    {
        "airport", "bridge", "building", "canal", "capitol", "castle", "cathedral", "center", "centre", "church",
        "city", "dam", "monument", "mosque", "museum", "palace", "park", "rover", "spacecraft", "station",
        "telescope", "temple", "tomb", "tower", "university", "wall",
    };

    private static readonly HashSet<string> ContextualPlaceAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        "nature", "geography", "geology", "earth", "environment",
    };

    private static readonly HashSet<string> ContextualPlaceCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "basin", "caldera", "geyser", "geothermal", "hot", "spring", "springs", "waterfall", "falls", "national", "park", "thermal",
    };

    private static readonly HashSet<string> GenericContextualSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "broad", "canyon", "desert", "forest", "glacier", "island", "lake", "mountain", "ocean", "peak",
        "river", "sea", "valley", "volcano", "waterfall", "falls",
    };

    private readonly INativeAssetVerifier _baseVerifier;

    public NativeNamedSubjectVerifier(INativeAssetVerifier baseVerifier) =>
        _baseVerifier = baseVerifier ?? throw new ArgumentNullException(nameof(baseVerifier));

    public async Task<NativeAssetVerificationResult> VerifyAsync(
        string query,
        NativeAcquiredAsset asset,
        CancellationToken cancellationToken = default)
    {
        var entity = NamedSubjectPhrase(query);
        var subject = ExplicitSubjectPhrase(query);
        var lowercaseEntity = LowercaseNamedPhrase(query);
        var checkQuery = (query ?? "").Trim();
        var anchoredEntity = "";

        if (entity.Length > 0 && subject.Length > 0 &&
            Normalize(entity) == Normalize(subject) && !DuplicatedAnchoredSubject(query, entity))
            anchoredEntity = entity;
        else if (lowercaseEntity.Length > 0)
            anchoredEntity = lowercaseEntity;

        if (anchoredEntity.Length > 0)
        {
            checkQuery += "\n\nNAMED-SUBJECT IDENTITY REQUIREMENT: " +
                $"The requested named subject is '{anchoredEntity}'. Judge it as that complete entity rather than separate matching keywords. " +
                "Reject clear evidence of a different named subject or semantic meaning, but do not demand impossible pixel-only proof for visually similar " +
                "places, landforms, buildings, species, machines or celestial bodies. If it is visually plausible but not uniquely provable, keep it as uncertain.";
        }
        else if (subject.Length > 0)
        {
            checkQuery += "\n\nEXPLICIT-SUBJECT VISUAL REQUIREMENT: " +
                $"The required concrete subject is '{subject}'. Inspect visible pixels; metadata must not prove presence. " +
                "For an ordinary visually recognizable subject, reject clearly unrelated dominant content. The anchor itself may be absent only when the current " +
                "query visibly shows a distinctive requested product, trace, result, body part, habitat detail or other scene-specific evidence. " +
                "If neither the anchor nor credible scene-specific evidence is visible, classify it as a mismatch.";
        }

        var result = await _baseVerifier.VerifyAsync(checkQuery, asset, cancellationToken);

        if (result.Accepted && subject.Length > 0 && anchoredEntity.Length == 0 &&
            !result.RequestedSubjectVisible && !result.RequestedSceneEvidenceVisible)
        {
            return result with
            {
                Accepted = false,
                Decision = $"explicit subject missing from pixels for {subject}: {result.Decision}",
            };
        }

        var softContradiction = Math.Max(
            result.HardNegative == "other_obvious_unrelated_subject" ? result.HardNegativeConfidence : 0,
            Math.Max(result.ObviousMismatch ? result.MismatchConfidence : 0,
                result.PhysicalContradiction ? result.PhysicalContradictionConfidence : 0));
        if (result.Accepted && subject.Length > 0 && anchoredEntity.Length == 0 && softContradiction >= ExplicitRejectConfidence)
        {
            return result with
            {
                Accepted = false,
                Decision = $"explicit subject contradiction for {subject}: {result.Decision}",
            };
        }

        return result;
    }

    public static string NamedSubjectPhrase(string query)
    {
        var tokens = Tokens(query);
        if (tokens.Count == 0) return "";
        if (BroadAnchors.Contains(tokens[0])) tokens.RemoveAt(0);
        var runs = new List<List<string>>();
        var current = new List<string>();
        foreach (var token in tokens)
        {
            var named = token.Length > 1 && (char.IsUpper(token[0]) || token.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) && !GenericCapitalized.Contains(token);
            if (named) current.Add(token);
            else if (current.Count > 0) { runs.Add(current); current = new List<string>(); }
        }
        if (current.Count > 0) runs.Add(current);
        if (runs.Count == 0) return "";
        var candidates = runs.Select(TrimNamedRun).ToList();
        var best = candidates.OrderByDescending(run => run.Count).ThenByDescending(run => string.Join(" ", run).Length).First();
        return string.Join(" ", best).Trim();
    }

    public static string ExplicitSubjectPhrase(string query)
    {
        var tokens = Tokens(query);
        if (tokens.Count == 0) return "";
        if (tokens.Count >= 2 && BroadAnchors.Contains(tokens[0]))
        {
            var anchored = tokens[1];
            var named = anchored.Length > 1 && (char.IsUpper(anchored[0]) || anchored.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) && !GenericCapitalized.Contains(anchored);
            if (!named) return anchored;
            var entity = NamedSubjectPhrase(query);
            if (entity.Length > 0 && Normalize(entity).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == anchored.ToLowerInvariant())
                return entity;
            return anchored;
        }
        return NamedSubjectPhrase(query);
    }

    private static string LowercaseNamedPhrase(string query)
    {
        var tokens = Tokens(query);
        if (tokens.Count < 3 || !BroadAnchors.Contains(tokens[0])) return "";
        var anchored = tokens[1];
        if (anchored.Length > 0 && (char.IsUpper(anchored[0]) || anchored.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))) return "";
        for (var index = 2; index < Math.Min(tokens.Count, 5); index++)
            if (LowercaseNamedTerminals.Contains(tokens[index])) return string.Join(" ", tokens.Skip(1).Take(index));
        if (ContextualPlaceAnchors.Contains(tokens[0]) && !GenericContextualSubjects.Contains(anchored))
        {
            var following = tokens.Skip(2).Take(5).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (following.Count(ContextualPlaceCues.Contains) >= 2) return anchored;
        }
        return "";
    }

    private static bool DuplicatedAnchoredSubject(string query, string entity)
    {
        var words = Tokens(query);
        var entityWords = Normalize(entity).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return entityWords.Length == 1 && words.Count >= 3 && BroadAnchors.Contains(words[0]) &&
            words[1].Equals(entityWords[0], StringComparison.OrdinalIgnoreCase) &&
            words[2].Equals(entityWords[0], StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> TrimNamedRun(List<string> tokens)
    {
        if (tokens.Count == 0) return [];
        if (TwoWordPrefixes.Contains(tokens[0]) && tokens.Count >= 2) return tokens.Take(2).ToList();
        for (var index = 0; index < tokens.Count; index++)
            if (EntityTerminals.Contains(tokens[index])) return tokens.Take(index + 1).ToList();
        return tokens.Take(2).ToList();
    }

    private static List<string> Tokens(string query) =>
        Regex.Matches(query ?? "", "[A-Za-z0-9][A-Za-z0-9'’-]*").Select(match => match.Value).ToList();

    private static string Normalize(string value) =>
        string.Join(" ", Regex.Matches((value ?? "").ToLowerInvariant(), "[a-z0-9]+").Select(match => match.Value));
}
