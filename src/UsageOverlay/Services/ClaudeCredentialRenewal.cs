using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text.Json.Nodes;
using CodexUsage.Core.Providers;

namespace UsageOverlay.Services;

/// <summary>Opt-in renewal. Shares Claude Code's refresh locks and preserves unrelated credential fields.</summary>
internal sealed class ClaudeCredentialRenewal(string credentialsPath, HttpClient client)
{
    private static readonly Uri TokenEndpoint = new("https://platform.claude.com/v1/oauth/token");
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    public async Task<ClaudeAccountFetch?> RenewAsync(string expectedAccessToken, bool force, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(credentialsPath))!;
        var locks = new[] { Path.Combine(directory, ".oauth_refresh.lock"), directory.TrimEnd(Path.DirectorySeparatorChar) + ".lock" };
        var held = new List<(string Path, long CreatedTicks)>();
        string? temporary = null;
        try
        {
            // Claude Code uses atomic mkdir, with a 60-second stale threshold. Never remove another holder's lock.
            foreach (var path in locks)
            {
                ReleaseAbandonedOwnedLock(path);
                if (!CreateDirectory(path, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() != 183) throw new IOException("Refresh lock is not writable.");
                    return Problem("Renewal pending", "Claude Code or another process is renewing this sign-in. The rail will retry automatically.");
                }
                var created = Directory.GetCreationTimeUtc(path).Ticks;
                held.Add((path, created));
                using var process = Process.GetCurrentProcess();
                var owner = new JsonObject { ["pid"] = process.Id, ["startedTicks"] = process.StartTime.ToUniversalTime().Ticks, ["createdTicks"] = created };
                File.WriteAllText(path + ".usage-overlay-owner", owner.ToJsonString());
            }

            var original = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            var root = JsonNode.Parse(original)?.AsObject();
            var oauth = root?["claudeAiOauth"]?.AsObject();
            if (oauth?["accessToken"]?.GetValue<string>() is not { } accessToken)
                return SignIn("No saved Claude Code sign-in is available. Sign in once, then automatic renewal can keep it current.");
            // A concurrent login or renewal wins. The caller rereads the authoritative file.
            if (!string.Equals(accessToken, expectedAccessToken, StringComparison.Ordinal)) return null;
            if (!force && oauth["expiresAt"]?.GetValue<long>() > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return null;
            var refreshToken = oauth["refreshToken"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(refreshToken)) return SignIn("The saved sign-in has no renewal token. Sign in to Claude Code once to restore automatic usage.");
            if (oauth["refreshTokenExpiresAt"]?.GetValue<long>() is { } refreshExpiry && refreshExpiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return SignIn("The saved renewal token has expired. Sign in to Claude Code once to restore automatic usage.");

            // Check write access before consuming a rotating refresh token. Keep the temporary file in the private config directory.
            temporary = Path.Combine(directory, ".credentials-renewal-" + Guid.NewGuid().ToString("N") + ".tmp");
            var permissions = new FileInfo(credentialsPath).GetAccessControl(AccessControlSections.Access);
            permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            using var replacement = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Read | FileSystemRights.Write,
                FileShare.None, 4096, FileOptions.Asynchronous, permissions);
            var scopes = oauth["scopes"]?.AsArray().Select(value => value?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var body = new JsonObject { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = ClientId };
            if (scopes is { Length: > 0 }) body["scope"] = string.Join(" ", scopes);
            cancellationToken.ThrowIfCancellationRequested();
            // Finish saving a rotating token even if the provider stops after the request was issued.
            using var transaction = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var transactionToken = transaction.Token;
            using var response = await client.PostAsJsonAsync(TokenEndpoint, body, transactionToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new(null, "Claude is limiting sign-in renewal. The rail will retry with a longer delay.", true, StatusText: "Renewal delayed");
            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await response.Content.ReadAsStringAsync(transactionToken).ConfigureAwait(false);
                var invalidGrant = response.StatusCode == HttpStatusCode.BadRequest &&
                    JsonNode.Parse(errorPayload)?["error"]?.ToString() is "invalid_grant" or "invalid_refresh_token";
                if (invalidGrant || response.StatusCode == HttpStatusCode.Unauthorized)
                    return SignIn("Claude could not renew the saved sign-in. Sign in to Claude Code once to reconnect.");
                return Problem("Renewal unavailable", $"Claude sign-in renewal returned HTTP {(int)response.StatusCode}. The rail will retry automatically.");
            }

            var payload = await response.Content.ReadAsStringAsync(transactionToken).ConfigureAwait(false);
            if (payload.Length > 65536) return Problem("Renewal unavailable", "Claude returned an unreadable renewal response. The saved sign-in was left unchanged.");
            var renewed = JsonNode.Parse(payload)?.AsObject();
            var nextAccess = renewed?["access_token"]?.GetValue<string>();
            var seconds = renewed?["expires_in"]?.GetValue<long>();
            var nextRefresh = renewed?["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(nextAccess) || seconds is not (> 0 and <= 31_536_000) ||
                (renewed!.ContainsKey("refresh_token") && string.IsNullOrWhiteSpace(nextRefresh)))
                return Problem("Renewal unavailable", "Claude returned an incomplete renewal response. The saved sign-in was left unchanged.");
            // Browser login can replace credentials independently of the refresh lock. Never overwrite that account change.
            if (!string.Equals(original, await ReadCredentialsAsync(transactionToken).ConfigureAwait(false), StringComparison.Ordinal)) return null;
            oauth["accessToken"] = nextAccess;
            oauth["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(seconds.Value).ToUnixTimeMilliseconds();
            if (nextRefresh is not null) oauth["refreshToken"] = nextRefresh;
            if (renewed["scope"]?.GetValue<string>() is { } grantedScope)
                oauth["scopes"] = new JsonArray(grantedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
            if (renewed["refresh_token_expires_in"]?.GetValue<long>() is > 0 and <= 31_536_000 and var refreshSeconds)
                oauth["refreshTokenExpiresAt"] = DateTimeOffset.UtcNow.AddSeconds(refreshSeconds).ToUnixTimeMilliseconds();
            await System.Text.Json.JsonSerializer.SerializeAsync(replacement, root, cancellationToken: transactionToken).ConfigureAwait(false);
            await replacement.FlushAsync(transactionToken).ConfigureAwait(false);
            replacement.Flush(flushToDisk: true);
            await replacement.DisposeAsync().ConfigureAwait(false);
            // ReplaceFile preserves the existing destination's Windows access controls and swaps atomically.
            File.Replace(temporary, credentialsPath, null);
            temporary = null;
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem("Renewal unavailable", "Claude sign-in renewal timed out. The rail will retry automatically.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return Problem("Renewal unavailable", "The rail could not renew or save the Claude sign-in. Check your connection and config-folder access; it will retry automatically.");
        }
        finally
        {
            if (temporary is not null) TryDelete(temporary, false);
            foreach (var (path, created) in held.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(path) && Directory.GetCreationTimeUtc(path).Ticks == created)
                    {
                        TryDelete(path, true);
                        TryDelete(path + ".usage-overlay-owner", false);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private async Task<string> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        if (new FileInfo(credentialsPath).Length > 65536) throw new IOException("Credentials exceed the supported size.");
        return await File.ReadAllTextAsync(credentialsPath, cancellationToken).ConfigureAwait(false);
    }

    private static ClaudeAccountFetch Problem(string title, string detail) => new(null, detail, StatusText: title);
    private static ClaudeAccountFetch SignIn(string detail) => new(null, detail, Connection: ProviderConnection.SignedOut, StatusText: "Sign-in required");

    private static void ReleaseAbandonedOwnedLock(string path)
    {
        var marker = path + ".usage-overlay-owner";
        if (!Directory.Exists(path) || !File.Exists(marker)) return;
        try
        {
            if (new FileInfo(marker).Length > 1024) return;
            var owner = JsonNode.Parse(File.ReadAllText(marker));
            if (owner?["createdTicks"]?.GetValue<long>() != Directory.GetCreationTimeUtc(path).Ticks) return;
            var pid = owner["pid"]?.GetValue<int>();
            var started = owner["startedTicks"]?.GetValue<long>();
            if (pid is not > 0 || started is not > 0) return;
            try
            {
                using var process = Process.GetProcessById(pid.Value);
                if (process.StartTime.ToUniversalTime().Ticks == started.Value) return;
            }
            catch (ArgumentException) { } // The recorded holder no longer exists.
            // Only recover a lock this app created, for a proven dead process and the same directory instance.
            Directory.Delete(path);
            File.Delete(marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or InvalidOperationException) { }
    }

    private static void TryDelete(string path, bool directory)
    {
        try { if (directory) Directory.Delete(path); else File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}
