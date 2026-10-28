using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Providers;
using Microsoft.Data.Sqlite;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <param name="RateLimited">The service asked us to slow down; back off before retrying.</param>
public sealed record ClaudeAccountFetch(
    ClaudeRateLimits? Limits,
    string? Problem,
    bool RateLimited = false,
    ProviderConnection Connection = ProviderConnection.Unavailable,
    string StatusText = "Usage unavailable");

/// <summary>
/// Unofficial and opt-in: reads Claude plan limits from the endpoint behind Claude's usage
/// page, authenticated with the sign-in Claude Code saved on this PC. The token is never
/// refreshed, stored or logged here; Claude Code keeps owning it.
/// </summary>
public sealed class ClaudeAccountUsageClient : IDisposable
{
    private static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");
    private readonly string _credentialsPath;
    private readonly HttpClient _client;

    public ClaudeAccountUsageClient(string? credentialsPath = null)
    {
        _credentialsPath = credentialsPath ?? Path.Combine(ClaudeCodeIntegration.ResolveClaudeConfigDirectory(), ".credentials.json");
        _client = AccountHttp.Create();
    }

    public async Task<ClaudeAccountFetch> FetchAsync(CancellationToken cancellationToken)
    {
        string credentials;
        try
        {
            credentials = File.Exists(_credentialsPath) ? await File.ReadAllTextAsync(_credentialsPath, cancellationToken).ConfigureAwait(false) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ClaudeAccountFetch(null, "The rail couldn’t read Claude Code’s saved sign-in. Check file access, then retry.", StatusText: "Sign-in unreadable");
        }

        if (!ClaudeAccountUsageParser.TryReadCredentials(credentials, out var token, out var expiresAt))
        {
            return new ClaudeAccountFetch(null, "The rail needs a Claude Code sign-in on this PC. Run claude auth login, finish in your browser, then retry.", Connection: ProviderConnection.SignedOut, StatusText: "Sign-in required");
        }

        if (expiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return new ClaudeAccountFetch(null, "The rail’s Claude Code sign-in has expired. Run claude auth login, finish in your browser, then retry. Claude desktop can stay signed in separately.", Connection: ProviderConnection.SignedOut, StatusText: "Sign-in expired");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new ClaudeAccountFetch(null, "Claude is limiting usage checks. The rail will retry automatically with a longer delay.", RateLimited: true, StatusText: "Too many requests");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ClaudeAccountFetch(null, "Claude rejected the rail’s saved sign-in. Run claude auth login, finish in your browser, then retry.", Connection: ProviderConnection.SignedOut, StatusText: "Sign-in rejected");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ClaudeAccountFetch(null, "Claude denied access to usage (HTTP 403). Check your account’s usage page and the enabled usage source in Settings.", StatusText: "Access denied");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ClaudeAccountFetch(null, $"Claude returned HTTP {(int)response.StatusCode}. The rail will retry automatically.", StatusText: "Service error");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ClaudeAccountUsageParser.TryParseUsage(json, out var limits)
                ? new ClaudeAccountFetch(limits, null)
                : new ClaudeAccountFetch(null, "Claude returned no readable usage limits. This unofficial source may need an update. You can still check Claude’s usage page.", StatusText: "No readable limits");
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new ClaudeAccountFetch(null, "The rail couldn’t reach Claude. Check your internet or VPN connection, then retry. It also retries automatically.", StatusText: "Connection failed");
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Unofficial and opt-in: reads the access token the Cursor editor saved in its local
/// state database. The database is opened read-only and the token is never stored.
/// </summary>
internal static class CursorLocalSession
{
    private const string TokenKey = "cursorAuth/accessToken";

    public static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cursor", "User", "globalStorage", "state.vscdb");

    public static bool TryReadAccessToken(out string token, out string problem)
    {
        token = string.Empty;
        problem = string.Empty;
        if (!File.Exists(DatabasePath))
        {
            problem = "Sign in to the Cursor editor on this PC, then refresh.";
            return false;
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1";
            command.Parameters.AddWithValue("$key", TokenKey);
            var value = command.ExecuteScalar() switch
            {
                string text => text,
                byte[] bytes => Decode(bytes),
                _ => string.Empty
            };
            token = value.Trim().Trim('"');
        }
        catch (SqliteException)
        {
            problem = "Couldn’t read Cursor’s saved sign-in. Retrying…";
            return false;
        }

        if (token.Length == 0)
        {
            problem = "Sign in to the Cursor editor on this PC, then refresh.";
            return false;
        }

        return true;
    }

    // Values may be stored as UTF-16LE or UTF-8 text.
    private static string Decode(byte[] bytes) =>
        bytes.Length > 1 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
}

internal static class AccountHttp
{
    public static HttpClient Create()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UsageOverlay", AppVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
