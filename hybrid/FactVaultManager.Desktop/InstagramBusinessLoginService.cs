using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record InstagramBusinessLoginResult(string AccessToken, string UserId, string Username, string AccountType, long ExpiresInSeconds);

public static class InstagramBusinessLoginService
{
    public const string RedirectUri = "http://localhost:53682/instagram/callback/";
    private const string AuthorizeEndpoint = "https://www.instagram.com/oauth/authorize";
    private const string ShortTokenEndpoint = "https://api.instagram.com/oauth/access_token";
    private const string LongTokenEndpoint = "https://graph.instagram.com/access_token";
    private const string ProfileEndpoint = "https://graph.instagram.com/me?fields=user_id,username,account_type";
    private const string Scope = "instagram_business_basic,instagram_business_manage_comments,instagram_business_content_publish";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static async Task<InstagramBusinessLoginResult> ConnectAsync(
        string appId,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        appId = Require(appId, "Instagram App ID");
        appSecret = Require(appSecret, "Instagram App Secret");

        using var listener = new TcpListener(IPAddress.Loopback, 53682);
        listener.Start();
        try
        {
            var state = Guid.NewGuid().ToString("N");
            var authorizeUri = BuildAuthorizeUri(appId, state);
            Process.Start(new ProcessStartInfo(authorizeUri.AbsoluteUri) { UseShellExecute = true });

            var callback = await WaitForCallbackAsync(listener, state, cancellationToken);
            var shortToken = await ExchangeCodeAsync(appId, appSecret, callback.Code, cancellationToken);
            var longToken = await ExchangeForLongLivedTokenAsync(appSecret, shortToken, cancellationToken);
            var profile = await GetProfileAsync(longToken.AccessToken, cancellationToken);

            return new InstagramBusinessLoginResult(
                longToken.AccessToken,
                profile.UserId,
                profile.Username,
                profile.AccountType,
                longToken.ExpiresInSeconds);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Uri BuildAuthorizeUri(string appId, string state)
    {
        var query = string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scope,
            ["response_type"] = "code",
            ["state"] = state,
            ["enable_fb_login"] = "0",
            ["force_reauth"] = "true",
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizeEndpoint}?{query}");
    }

    private static async Task<InstagramCallback> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = client.GetStream();

        var request = await ReadHttpRequestAsync(stream, timeout.Token);
        var target = request.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
        var uri = new Uri(new Uri(RedirectUri), target);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var error = query["error"];
        if (!string.IsNullOrWhiteSpace(error))
        {
            await WriteResponseAsync(stream, "Instagram authorization was cancelled or rejected.", timeout.Token);
            throw new InvalidOperationException($"Instagram authorization failed: {error}.");
        }

        var state = query["state"] ?? "";
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteResponseAsync(stream, "The Instagram authorization could not be verified. You can close this window.", timeout.Token);
            throw new InvalidOperationException("Instagram authorization state validation failed. Start Connect Instagram again.");
        }

        var code = query["code"] ?? "";
        if (code.Length == 0)
        {
            await WriteResponseAsync(stream, "No Instagram authorization code was returned. You can close this window.", timeout.Token);
            throw new InvalidOperationException("Instagram did not return an authorization code.");
        }

        await WriteResponseAsync(stream, "Instagram connected successfully. You can close this window and return to Factburst Quiz Manager.", timeout.Token);
        return new InstagramCallback(code);
    }

    private static async Task<string> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();
        while (builder.Length < 32768)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read <= 0) break;
            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
        return builder.ToString();
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
    {
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Factburst</title></head><body style=\"font-family:Segoe UI,sans-serif;padding:40px\"><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static async Task<string> ExchangeCodeAsync(string appId, string appSecret, string code, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["client_secret"] = appSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
            ["code"] = code,
        });
        using var response = await Client.PostAsync(ShortTokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram token exchange failed (HTTP {(int)response.StatusCode}): {ReadApiError(body)}");
        return ReadRequired(body, "access_token");
    }

    private static async Task<LongLivedToken> ExchangeForLongLivedTokenAsync(string appSecret, string shortToken, CancellationToken cancellationToken)
    {
        var uri = $"{LongTokenEndpoint}?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(appSecret)}&access_token={Uri.EscapeDataString(shortToken)}";
        using var response = await Client.GetAsync(uri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram long-lived token exchange failed (HTTP {(int)response.StatusCode}): {ReadApiError(body)}");

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.TryGetProperty("access_token", out var accessToken)
            ? accessToken.GetString()?.Trim() ?? ""
            : "";
        if (token.Length == 0)
            throw new InvalidOperationException("Instagram did not return a long-lived access token.");
        var expires = document.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt64(out var value)
            ? value
            : 0;
        return new LongLivedToken(token, expires);
    }

    private static async Task<InstagramProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Instagram connected but the account could not be verified (HTTP {(int)response.StatusCode}): {ReadApiError(body)}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var userId = ReadString(root, "user_id");
        if (userId.Length == 0) userId = ReadString(root, "id");
        if (userId.Length == 0)
            throw new InvalidOperationException("Instagram connected but did not return an account ID.");
        return new InstagramProfile(userId, ReadString(root, "username"), ReadString(root, "account_type"));
    }

    private static string ReadRequired(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        var value = ReadString(document.RootElement, property);
        return value.Length > 0 ? value : throw new InvalidOperationException($"Instagram did not return {property}.");
    }

    private static string ReadApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error_message", out var message)) return message.GetString() ?? body;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) return error.GetString() ?? body;
            if (root.TryGetProperty("error", out error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out message)) return message.GetString() ?? body;
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(body) ? "No additional error details were returned." : body.Length > 400 ? body[..400] : body;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is not configured.") : value.Trim();

    private sealed record InstagramCallback(string Code);
    private sealed record LongLivedToken(string AccessToken, long ExpiresInSeconds);
    private sealed record InstagramProfile(string UserId, string Username, string AccountType);
}
