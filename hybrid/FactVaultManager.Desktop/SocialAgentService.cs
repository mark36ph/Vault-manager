using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FactVaultManager.Desktop;

/// <summary>
/// Desktop-side executor for website social moderation commands. The website queues intent;
/// this process performs the real platform API call using the credentials already stored on the PC.
/// </summary>
public partial class MainShellWindow
{
    private readonly DispatcherTimer _socialAgentTimer = new();
    private bool _socialAgentRunning;

    private void InitializeSocialAgent()
    {
        if (_socialAgentTimer.Interval != TimeSpan.Zero)
            return;

        _socialAgentTimer.Interval = TimeSpan.FromSeconds(30);
        _socialAgentTimer.Tick += async (_, _) => await RunSocialAgentCycleAsync();
        Closed += (_, _) => _socialAgentTimer.Stop();
        _socialAgentTimer.Start();
        _ = RunSocialAgentCycleAsync();
    }

    private async Task RunSocialAgentCycleAsync()
    {
        if (_socialAgentRunning) return;
        _socialAgentRunning = true;
        try
        {
            var settings = SocialAgentSettingsStore.Load(_data.SettingsPath);
            if (!settings.IsConfigured) return;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            var commands = await GetCommandsAsync(client, settings.BaseUrl);
            foreach (var command in commands)
            {
                try
                {
                    var result = await ExecuteCommandAsync(command);
                    await PostResultAsync(client, settings.BaseUrl, command.Id, true, result, "");
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"Social Agent command {command.Id} failed: {error.Message}");
                    await PostResultAsync(client, settings.BaseUrl, command.Id, false, new { }, error.Message);
                }
            }

            // Comments are synced less frequently so a temporary platform/API problem cannot
            // turn the command queue into a noisy background workload.
            if (DateTime.UtcNow.Second < 30)
                await SyncYouTubeCommentsAsync(client, settings.BaseUrl);
        }
        catch (Exception error)
        {
            Debug.WriteLine("Social Agent cycle failed: " + error.Message);
        }
        finally
        {
            _socialAgentRunning = false;
        }
    }

    private sealed record SocialAgentCommand(
        long Id,
        long? CommentId,
        string QuizSlug,
        string Platform,
        string Action,
        JsonElement Payload);

    private static async Task<IReadOnlyList<SocialAgentCommand>> GetCommandsAsync(HttpClient client, string baseUrl)
    {
        using var response = await client.GetAsync(baseUrl.TrimEnd('/') + "/api/social/agent/commands?limit=20");
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var result = new List<SocialAgentCommand>();
        if (!document.RootElement.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in commands.EnumerateArray())
        {
            result.Add(new SocialAgentCommand(
                item.GetProperty("id").GetInt64(),
                item.TryGetProperty("comment_id", out var commentId) && commentId.ValueKind == JsonValueKind.Number ? commentId.GetInt64() : null,
                item.GetProperty("quiz_slug").GetString() ?? "",
                item.GetProperty("platform").GetString() ?? "",
                item.GetProperty("action").GetString() ?? "",
                item.TryGetProperty("payload", out var payload) ? payload.Clone() : default));
        }
        return result;
    }

    private async Task<object> ExecuteCommandAsync(SocialAgentCommand command)
    {
        var platform = command.Platform.Trim().ToLowerInvariant();
        var action = command.Action.Trim().ToLowerInvariant();
        var commentId = await ResolvePlatformCommentIdAsync(command);
        if (commentId.Length == 0)
            throw new InvalidOperationException("The platform comment ID is missing.");

        return platform switch
        {
            "youtube" => await ExecuteYouTubeCommandAsync(action, commentId, command.Payload),
            "facebook" => await ExecuteGraphCommentCommandAsync("facebook", action, commentId, command.Payload),
            "instagram" => await ExecuteGraphCommentCommandAsync("instagram", action, commentId, command.Payload),
            _ => throw new InvalidOperationException("Unsupported social platform.")
        };
    }

    private async Task<string> ResolvePlatformCommentIdAsync(SocialAgentCommand command)
    {
        if (command.CommentId is long localId)
        {
            // The website already validated and stored the platform comment ID. The command API
            // intentionally does not send that value in the command payload, so retrieve it from
            // the public moderation feed only when necessary is not possible here. Admin commands
            // therefore include platform_comment_id in the payload in newer deployments.
        }
        if (command.Payload.ValueKind == JsonValueKind.Object &&
            command.Payload.TryGetProperty("platform_comment_id", out var id) &&
            id.ValueKind == JsonValueKind.String)
            return id.GetString() ?? "";
        throw new InvalidOperationException("The queued command has no platform comment ID. Refresh the website Social Hub and retry the action.");
    }

    private async Task<object> ExecuteYouTubeCommandAsync(string action, string commentId, JsonElement payload)
    {
        var settings = _data.LoadSettings();
        var token = await GetYouTubeManagementAccessTokenAsync();
        var service = new YouTubeManagementService();

        switch (action)
        {
            case "reply":
                var text = ReadPayloadText(payload);
                await service.ReplyAsync(token, commentId, text);
                return new { platform = "youtube", action, comment_id = commentId };
            case "hide":
                await SetYouTubeModerationStatusAsync(token, commentId, "heldForReview");
                return new { platform = "youtube", action, comment_id = commentId, moderation_status = "heldForReview" };
            case "delete":
                await DeleteYouTubeCommentAsync(token, commentId);
                return new { platform = "youtube", action, comment_id = commentId };
            case "like":
            case "unlike":
            case "pin":
            case "unpin":
                throw new InvalidOperationException($"YouTube does not expose the '{action}' comment action through the API used by Factburst.");
            default:
                throw new InvalidOperationException("Unsupported YouTube comment action.");
        }
    }

    private async Task<object> ExecuteGraphCommentCommandAsync(string platform, string action, string commentId, JsonElement payload)
    {
        var settings = _data.LoadSettings();
        var token = platform == "instagram"
            ? settings.InstagramAccessToken.Trim()
            : settings.FacebookPageAccessToken.Trim();
        if (token.Length == 0)
            throw new InvalidOperationException($"Connect {platform} in Desktop Settings before using social moderation.");

        var root = "https://graph." + (platform == "instagram" ? "instagram.com" : "facebook.com") + "/v26.0";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (action == "reply")
        {
            var text = ReadPayloadText(payload);
            using var response = await client.PostAsync(
                $"{root}/{Uri.EscapeDataString(commentId)}/replies",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = text }));
            await EnsureGraphSuccessAsync(response, "Social reply failed");
            return new { platform, action, comment_id = commentId };
        }

        if (action == "like")
        {
            using var response = await client.PostAsync($"{root}/{Uri.EscapeDataString(commentId)}/likes", null);
            await EnsureGraphSuccessAsync(response, "Social like failed");
            return new { platform, action, comment_id = commentId };
        }

        if (action == "unlike")
        {
            using var response = await client.DeleteAsync($"{root}/{Uri.EscapeDataString(commentId)}/likes");
            await EnsureGraphSuccessAsync(response, "Social unlike failed");
            return new { platform, action, comment_id = commentId };
        }

        if (action == "hide")
        {
            using var response = await client.PostAsync(
                $"{root}/{Uri.EscapeDataString(commentId)}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["is_hidden"] = "true" }));
            await EnsureGraphSuccessAsync(response, "Social hide failed");
            return new { platform, action, comment_id = commentId, hidden = true };
        }

        if (action == "delete")
        {
            using var response = await client.DeleteAsync($"{root}/{Uri.EscapeDataString(commentId)}");
            await EnsureGraphSuccessAsync(response, "Social delete failed");
            return new { platform, action, comment_id = commentId };
        }

        throw new InvalidOperationException($"The {platform} API used by Factburst does not expose '{action}' yet.");
    }

    private static string ReadPayloadText(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Reply text is missing from the queued command.");
        var value = text.GetString()?.Trim() ?? "";
        if (value.Length == 0) throw new InvalidOperationException("Reply text is empty.");
        return value;
    }

    private static async Task SetYouTubeModerationStatusAsync(string accessToken, string commentId, string status)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var url = "https://www.googleapis.com/youtube/v3/comments/setModerationStatus?id="
            + Uri.EscapeDataString(commentId) + "&moderationStatus=" + Uri.EscapeDataString(status);
        using var response = await client.PostAsync(url, null);
        await EnsureGoogleSuccessAsync(response, "YouTube moderation request failed");
    }

    private static async Task DeleteYouTubeCommentAsync(string accessToken, string commentId)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.DeleteAsync(
            "https://www.googleapis.com/youtube/v3/comments?id=" + Uri.EscapeDataString(commentId));
        await EnsureGoogleSuccessAsync(response, "YouTube comment deletion failed");
    }

    private static async Task EnsureGraphSuccessAsync(HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"{fallback}: {ExtractApiError(text)} (HTTP {(int)response.StatusCode}).");
    }

    private static async Task EnsureGoogleSuccessAsync(HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"{fallback}: {ExtractApiError(text)} (HTTP {(int)response.StatusCode}).");
    }

    private static string ExtractApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                    return message.GetString() ?? "API error";
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "API error";
            }
        }
        catch (JsonException) { }
        var value = string.Join(' ', (body ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= 300 ? value : value[..300] + "…";
    }

    private async Task PostResultAsync(HttpClient client, string baseUrl, long commandId, bool success, object result, string error)
    {
        var json = JsonSerializer.Serialize(new
        {
            command_id = commandId,
            success,
            result,
            error = error ?? "",
        });
        using var response = await client.PostAsync(
            baseUrl.TrimEnd('/') + "/api/social/agent/commands/result",
            new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    private async Task SyncYouTubeCommentsAsync(HttpClient client, string baseUrl)
    {
        var settings = _data.LoadSettings();
        if (settings.YouTubeOAuthRefreshToken.Length == 0 || settings.YouTubeOAuthClientId.Length == 0)
            return;

        string token;
        try { token = await GetYouTubeManagementAccessTokenAsync(); }
        catch (Exception error)
        {
            Debug.WriteLine("Social Agent YouTube token refresh failed: " + error.Message);
            return;
        }

        var service = new YouTubeManagementService();
        var channel = await service.GetMyChannelAsync(token);
        var histories = _data.GetQuizHistory();
        var byVideoId = histories
            .Select(history => (history, videoId: YouTubeVideoAnalyticsService.TryGetVideoId(history.YouTubeUrl)))
            .Where(item => !string.IsNullOrWhiteSpace(item.videoId))
            .ToDictionary(item => item.videoId!, item => item.history, StringComparer.Ordinal);

        var comments = new List<object>();
        foreach (var status in new[] { "published", "heldForReview", "likelySpam" })
        {
            IReadOnlyList<YouTubeCommentItem> batch;
            try { batch = await service.ListCommentsAsync(token, channel.Id, status); }
            catch (Exception error)
            {
                Debug.WriteLine($"Social Agent YouTube comment sync ({status}) failed: {error.Message}");
                continue;
            }

            foreach (var comment in batch)
            {
                if (!byVideoId.TryGetValue(comment.VideoId, out var history)) continue;
                comments.Add(new
                {
                    quiz_slug = history.Id,
                    platform = "youtube",
                    platform_comment_id = comment.Id,
                    parent_comment_id = comment.Id == comment.ThreadId ? "" : comment.ThreadId,
                    author_name = comment.Author,
                    author_id = comment.AuthorChannelId,
                    text = comment.Text,
                    permalink = YouTubeManagementService.BuildCommentUrl(comment.VideoId, comment.Id),
                    status = comment.ModerationStatus,
                    created_at = comment.PublishedAt,
                });
            }
        }

        if (comments.Count == 0) return;
        var payload = JsonSerializer.Serialize(new { comments = comments.Take(500).ToArray() });
        using var response = await client.PostAsync(
            baseUrl.TrimEnd('/') + "/api/social/agent/comments",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }
}
