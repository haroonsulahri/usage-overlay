using System.Net;
using System.Text.Json.Nodes;
using CodexUsage.Core.Providers;
using CodexUsage.Core.Settings;
using UsageOverlay.Services;

internal static class ClaudeRenewalSpecs
{
    private const string Limits = "{\"five_hour\":{\"utilization\":12,\"resets_at\":\"2030-01-01T00:00:00Z\"},\"seven_day\":{\"utilization\":31,\"resets_at\":\"2030-01-02T00:00:00Z\"}}";
    private const string Renewed = "{\"access_token\":\"renewed-access\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":28800,\"scope\":\"user:profile user:inference\"}";

    public static async Task RunAsync()
    {
        Check(!new OverlaySettings().ClaudeAutomaticRenewalEnabled &&
              !OverlaySettings.FromJson("{\"ClaudeAccountUsageEnabled\":true}").ClaudeAutomaticRenewalEnabled,
            "Automatic credential renewal requires a separate opt-in on new and upgraded installations");

        await WithFixture(async f =>
        {
            using var client = f.Client(renew: false);
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Connection == ProviderConnection.SignedOut && f.Handler.Requests.Count == 0 && f.Read() == f.Original,
                "Disabled renewal neither sends a refresh token nor edits credentials");
        });
        await WithFixture(async f =>
        {
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            var saved = JsonNode.Parse(f.Read())!;
            Check(result.Limits is not null && f.Handler.Requests.Count == 2, "Expired access token renews and loads usage without a CLI or model request");
            Check(f.Handler.Requests[0] == "POST https://platform.claude.com/v1/oauth/token" &&
                  f.Handler.Requests[1] == "GET https://api.anthropic.com/api/oauth/usage",
                "Credentials go only to the fixed Anthropic token and usage endpoints");
            Check(saved["claudeAiOauth"]!["refreshToken"]!.GetValue<string>() == "rotated-refresh" &&
                  saved["claudeAiOauth"]!["expiresAt"]!.GetValue<long>() > DateTimeOffset.UtcNow.AddHours(7).ToUnixTimeMilliseconds(),
                "Rotated refresh token and fresh expiry persist across process restarts");
            Check(saved["untouched"]!.GetValue<string>() == "keep-root" && saved["claudeAiOauth"]!["subscriptionType"]!.GetValue<string>() == "pro",
                "Renewal preserves unrelated account and root properties");
            Check(!Directory.Exists(f.Lock) && !Directory.Exists(f.LegacyLock) && Directory.GetFiles(f.Directory, "*.tmp").Length == 0,
                "Renewal releases both Claude Code locks and removes temporary credential files");
            using var restarted = f.Client();
            await restarted.FetchAsync(CancellationToken.None);
            Check(f.Handler.Requests.Count(request => request.StartsWith("POST", StringComparison.Ordinal)) == 1,
                "A restarted client reuses saved renewed credentials without another login or refresh");
        });
        await WithFixture(async f =>
        {
            f.SetExpiry(DateTimeOffset.UtcNow.AddHours(1));
            f.Handler.RejectFirstUsage = true;
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is not null && f.Handler.Requests.Count == 3, "An unexpected usage 401 renews once and retries with the new access token");
        });
        await WithFixture(async f =>
        {
            f.SetExpiry(DateTimeOffset.UtcNow.AddHours(1));
            f.Handler.DenyUsage = true;
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.StatusText == "Access denied" && f.Handler.Requests.Count == 1, "Usage 403 is not misclassified as token expiry or retried through renewal");
        });
        await WithFixture(async f =>
        {
            f.Handler.TokenStatus = HttpStatusCode.BadRequest;
            f.Handler.TokenPayload = "{\"error\":\"invalid_grant\"}";
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Connection == ProviderConnection.SignedOut && f.Read() == f.Original && f.Handler.Requests.Count == 1,
                "Revoked renewal requires login without deleting or overwriting the saved sign-in");
        });
        await WithFixture(async f =>
        {
            f.Handler.TokenStatus = HttpStatusCode.TooManyRequests;
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.RateLimited && result.Connection != ProviderConnection.SignedOut && f.Read() == f.Original,
                "Renewal rate limiting backs off without forcing login or changing credentials");
        });
        await WithFixture(async f =>
        {
            f.Handler.Timeout = true;
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Connection == ProviderConnection.Unavailable && f.Read() == f.Original && !Directory.Exists(f.Lock),
                "Offline or timed-out renewal preserves credentials and releases the lock");
        });
        foreach (var legacy in new[] { false, true })
        {
            await WithFixture(async f =>
            {
                var locked = legacy ? f.LegacyLock : f.Lock;
                Directory.CreateDirectory(locked);
                using var client = f.Client();
                var result = await client.FetchAsync(CancellationToken.None);
                Check(result.StatusText == "Renewal pending" && f.Handler.Requests.Count == 0 && Directory.Exists(locked),
                    legacy ? "A legacy Claude Code refresh lock is respected" : "An unknown active refresh lock is never removed");
            });
        }
        await WithFixture(async f =>
        {
            Directory.CreateDirectory(f.Lock);
            var marker = new JsonObject { ["pid"] = int.MaxValue, ["startedTicks"] = 1, ["createdTicks"] = Directory.GetCreationTimeUtc(f.Lock).Ticks };
            await File.WriteAllTextAsync(f.Lock + ".usage-overlay-owner", marker.ToJsonString());
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is not null && !Directory.Exists(f.Lock), "A lock this app left during a crash is recovered only after its holder is proven dead");
        });
        await WithFixture(async f =>
        {
            using var client = f.Client();
            var results = await Task.WhenAll(client.FetchAsync(CancellationToken.None), client.FetchAsync(CancellationToken.None));
            Check(results.All(result => result.Limits is not null) && f.Handler.Requests.Count(request => request.StartsWith("POST", StringComparison.Ordinal)) == 1,
                "Concurrent refresh requests consume a rotating token only once");
        });
        await WithFixture(async f =>
        {
            f.Handler.OnRenew = () => f.ReplaceAccount();
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is not null && JsonNode.Parse(f.Read())!["untouched"]!.GetValue<string>() == "external-login" &&
                  f.Handler.LastUsageToken == "external-account-access", "A browser account change during renewal wins over the old renewal response");
        });
        await WithFixture(async f =>
        {
            f.Handler.TokenPayload = "{\"access_token\":\"new\"}";
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is null && f.Read() == f.Original, "Incomplete renewal responses never overwrite existing credentials");
        });
        await WithFixture(async f =>
        {
            f.SetExpiry(DateTimeOffset.UtcNow.AddHours(1));
            f.Handler.OnUsage = f.ReplaceAccount;
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is null && result.StatusText == "Sign-in changed", "A late usage response from a replaced account is discarded");
            result = await client.FetchAsync(CancellationToken.None);
            Check(result.Limits is not null && f.Handler.LastUsageToken == "external-account-access", "Usage recovers with the newly saved account on the next check");
        });
        await WithFixture(async f =>
        {
            using var cancellation = new CancellationTokenSource();
            f.Handler.OnRenew = cancellation.Cancel;
            using var client = f.Client();
            try { await client.FetchAsync(cancellation.Token); }
            catch (OperationCanceledException) { }
            Check(JsonNode.Parse(f.Read())!["claudeAiOauth"]!["refreshToken"]!.GetValue<string>() == "rotated-refresh",
                "Stopping a provider finishes saving an already-issued rotating refresh token");
        });
        await WithFixture(async f =>
        {
            var root = JsonNode.Parse(f.Read())!;
            root["claudeAiOauth"]!["refreshTokenExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
            await File.WriteAllTextAsync(f.Path, root.ToJsonString());
            using var client = f.Client();
            var result = await client.FetchAsync(CancellationToken.None);
            Check(result.Connection == ProviderConnection.SignedOut && f.Handler.Requests.Count == 0, "Expired refresh tokens request login without futile network retries");
        });
    }

    private static async Task WithFixture(Func<Fixture, Task> run)
    {
        using var fixture = new Fixture();
        await run(fixture);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "usage-renewal-spec-" + Guid.NewGuid().ToString("N"));
        public string Directory => System.IO.Path.Combine(_root, "claude");
        public string Path => System.IO.Path.Combine(Directory, ".credentials.json");
        public string Lock => System.IO.Path.Combine(Directory, ".oauth_refresh.lock");
        public string LegacyLock => Directory + ".lock";
        public Handler Handler { get; } = new();
        public string Original { get; }

        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Original = new JsonObject
            {
                ["untouched"] = "keep-root",
                ["claudeAiOauth"] = new JsonObject
                {
                    ["accessToken"] = "old-access", ["refreshToken"] = "old-refresh", ["subscriptionType"] = "pro",
                    ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                    ["refreshTokenExpiresAt"] = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeMilliseconds(),
                    ["scopes"] = new JsonArray("user:profile", "user:inference")
                }
            }.ToJsonString();
            File.WriteAllText(Path, Original);
        }

        public ClaudeAccountUsageClient Client(bool renew = true) => new(Path, Handler) { AutomaticRenewalEnabled = renew };
        public string Read() => File.ReadAllText(Path);
        public void SetExpiry(DateTimeOffset expiry)
        {
            var root = JsonNode.Parse(Read())!;
            root["claudeAiOauth"]!["expiresAt"] = expiry.ToUnixTimeMilliseconds();
            File.WriteAllText(Path, root.ToJsonString());
        }
        public void ReplaceAccount()
        {
            var root = JsonNode.Parse(Read())!;
            root["untouched"] = "external-login";
            root["claudeAiOauth"]!["accessToken"] = "external-account-access";
            root["claudeAiOauth"]!["expiresAt"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
            File.WriteAllText(Path, root.ToJsonString());
        }
        public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool RejectFirstUsage { get; set; }
        public bool DenyUsage { get; set; }
        public bool Timeout { get; set; }
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public string TokenPayload { get; set; } = Renewed;
        public Action? OnRenew { get; set; }
        public Action? OnUsage { get; set; }
        public string? LastUsageToken { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            if (request.Method == HttpMethod.Post)
            {
                if (Timeout) throw new TaskCanceledException();
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                Check(body["grant_type"]!.GetValue<string>() == "refresh_token" &&
                      body["scope"]!.GetValue<string>() == "user:profile user:inference", "Refresh preserves the existing grant and scopes");
                OnRenew?.Invoke();
                return new HttpResponseMessage(TokenStatus) { Content = new StringContent(TokenPayload) };
            }
            LastUsageToken = request.Headers.Authorization?.Parameter;
            var change = OnUsage;
            OnUsage = null;
            change?.Invoke();
            if (DenyUsage) return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (RejectFirstUsage) { RejectFirstUsage = false; return new HttpResponseMessage(HttpStatusCode.Unauthorized); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Limits) };
        }
    }
}
