using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FactVaultManager.Desktop;

public sealed record YouTubeOAuthTokens(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string Scope);

public sealed class YouTubeOAuthService
{
    public const string ManagementScope = "https://www.googleapis.com/auth/youtube.force-ssl";
    public const string AnalyticsReadonlyScope = "https://www.googleapis.com/auth/yt-analytics.readonly";
    public static string RequiredScopes => $"{ManagementScope} {AnalyticsReadonlyScope}";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _client;

    public YouTubeOAuthService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<YouTubeOAuthTokens> AuthorizeAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";
            var authorizationUri = CreateAuthorizationUri(clientId.Trim(), redirectUri, state, challenge);
            Process.Start(new ProcessStartInfo(authorizationUri) { UseShellExecute = true });

            using var browser = await listener.AcceptTcpClientAsync(timeout.Token);
            var callback = await ReadCallbackAsync(browser, redirectUri, timeout.Token);
            if (!string.Equals(callback.State, state, StringComparison.Ordinal))
                throw new InvalidOperationException("Google sign-in returned an invalid security state. Please try again.");
            if (callback.Error.Length > 0)
                throw new InvalidOperationException($"Google sign-in was not completed: {callback.Error}.");
            if (callback.Code.Length == 0)
                throw new InvalidOperationException("Google sign-in did not return an authorization code.");

            return await ExchangeCodeAsync(
                clientId.Trim(), clientSecret.Trim(), redirectUri, callback.Code, verifier, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Google sign-in timed out. Please try again.");
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<string> RefreshAccessTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Connect the app to your YouTube channel in Settings first.");

        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["refresh_token"] = refreshToken.Trim(),
            ["grant_type"] = "refresh_token",
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
            values["client_secret"] = clientSecret.Trim();

        using var response = await _client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(values), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw OAuthError(json, "Google could not refresh the YouTube connection");
        var tokens = ParseTokenResponse(json);
        if (tokens.AccessToken.Length == 0)
            throw new InvalidOperationException("Google did not return a usable YouTube access token.");
        return tokens.AccessToken;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        using var response = await _client.PostAsync(
            RevokeEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken.Trim() }),
            cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest)
            throw new InvalidOperationException($"Google could not disconnect YouTube (HTTP {(int)response.StatusCode}).");
    }

    public static string CreateAuthorizationUri(
        string clientId,
        string redirectUri,
        string state,
        string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = RequiredScopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return AuthorizationEndpoint + "?" + string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    public static YouTubeOAuthTokens ParseTokenResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new YouTubeOAuthTokens(
            ReadString(root, "access_token"),
            ReadString(root, "refresh_token"),
            root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 0,
            ReadString(root, "scope"));
    }

    private async Task<YouTubeOAuthTokens> ExchangeCodeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        };
        if (clientSecret.Length > 0)
            values["client_secret"] = clientSecret;

        using var response = await _client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(values), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw OAuthError(json, "Google could not complete YouTube sign-in");
        var tokens = ParseTokenResponse(json);
        if (tokens.AccessToken.Length == 0 || tokens.RefreshToken.Length == 0)
            throw new InvalidOperationException("Google did not return the tokens needed to keep YouTube connected.");
        return tokens;
    }

    private static async Task<(string Code, string State, string Error)> ReadCallbackAsync(
        TcpClient client,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        var requestLine = await reader.ReadLineAsync(cancellationToken) ?? "";
        string? line;
        do { line = await reader.ReadLineAsync(cancellationToken); } while (!string.IsNullOrEmpty(line));

        var requestTarget = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
        var callbackUri = new Uri(new Uri(redirectUri), requestTarget);
        var parameters = ParseQuery(callbackUri.Query);
        var successful = parameters.TryGetValue("code", out var code) && code.Length > 0;
        var html = successful
            ? "<html><body style='font-family:Segoe UI'><h2>YouTube connected</h2><p>You can close this window and return to Factburst Quiz Manager.</p></body></html>"
            : "<html><body style='font-family:Segoe UI'><h2>YouTube was not connected</h2><p>Return to Factburst Quiz Manager and try again.</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);

        return (
            parameters.GetValueOrDefault("code", ""),
            parameters.GetValueOrDefault("state", ""),
            parameters.GetValueOrDefault("error", ""));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            values[key] = value;
        }
        return values;
    }

    private static InvalidOperationException OAuthError(string json, string prefix)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var description = ReadString(document.RootElement, "error_description");
            if (description.Length == 0) description = ReadString(document.RootElement, "error");
            return new InvalidOperationException(description.Length == 0 ? prefix + "." : $"{prefix}: {description}");
        }
        catch (JsonException)
        {
            return new InvalidOperationException(prefix + ".");
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString()?.Trim() ?? "" : "";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Enter the Google OAuth desktop client ID in Settings → YouTube first.");
    }
}
