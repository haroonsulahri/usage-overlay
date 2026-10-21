using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Providers;
using CodexUsage.Core.Settings;
using CodexUsage.Core.Windows;
using System.Text.Json;

internal static class ProviderSpecs
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    public static void ParsesClaudeStatusLine()
    {
        var json = $$"""
            {
              "session_id": "abc-123",
              "model": { "display_name": "Opus" },
              "rate_limits": {
                "five_hour": { "used_percentage": 42.5, "resets_at": {{Now.AddHours(2).ToUnixTimeSeconds()}} },
                "seven_day": { "used_percentage": 140, "resets_at": "2026-10-23T09:00:00Z" }
              }
            }
            """;

        Assert(ClaudeStatusLineParser.TryParse(json, out var limits, out var sessionId), "Status line should parse.");
        AssertEqual("abc-123", sessionId);
        AssertEqual(42.5, limits.FiveHour!.UsedPercent);
        AssertEqual(Now.AddHours(2), limits.FiveHour.ResetsAt);
        AssertEqual(100d, limits.SevenDay!.UsedPercent);
        AssertEqual(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero), limits.SevenDay.ResetsAt);
    }

    public static void ToleratesClaudeStatusLineWithoutLimits()
    {
        Assert(
            ClaudeStatusLineParser.TryParse("""{ "session_id": "s1", "model": {} }""", out var limits, out _),
            "A status line without plan limits is still valid input.");
        Assert(!limits.HasAny, "No limits should be reported.");
        Assert(!ClaudeStatusLineParser.TryParse("not json", out _, out _), "Malformed input must be rejected.");
        Assert(!ClaudeStatusLineParser.TryParse("[]", out _, out _), "Non-object input must be rejected.");
    }

    public static void KeepsLastClaudeLimitsAndTracksHosts()
    {
        var limits = new ClaudeRateLimits(
            new QuotaWindow(30, 300, Now.AddHours(1)),
            new QuotaWindow(80, 10_080, Now.AddDays(2)));
        var snapshot = ClaudeUsageSnapshot.Empty
            .Apply(limits, "session-a", [100, 200, 300], Now, ownerProcessId: 100)
            .Apply(new ClaudeRateLimits(null, null), "session-b", [400, 500], Now.AddMinutes(1), ownerProcessId: 400);
        static bool AllRunning(int processId) => true;

        AssertEqual(Now, snapshot.UpdatedAt);
        AssertEqual(30d, snapshot.FiveHour!.UsedPercent);
        Assert(snapshot.IsHostProcess(200, Now.AddMinutes(2), AllRunning), "First session's terminal should be recognised.");
        Assert(snapshot.IsHostProcess(500, Now.AddMinutes(2), AllRunning), "Second session's terminal should be recognised.");
        Assert(!snapshot.IsHostProcess(999, Now.AddMinutes(2), AllRunning), "Unrelated processes are not Claude hosts.");
        Assert(!snapshot.IsHostProcess(200, Now.AddDays(2), AllRunning), "Old sessions expire.");
        Assert(
            !snapshot.IsHostProcess(200, Now.AddMinutes(2), processId => processId != 100),
            "A terminal stops being a Claude host when its Claude Code process exits.");
        Assert(
            snapshot.IsHostProcess(500, Now.AddMinutes(2), processId => processId != 100),
            "Other live sessions are unaffected.");

        Assert(ClaudeUsageSnapshot.TryDeserialize(snapshot.Serialize(), out var roundTripped), "Snapshot should round-trip.");
        AssertEqual(2, roundTripped.Hosts.Length);
        AssertEqual(400, roundTripped.Hosts[0].OwnerProcessId);
        AssertEqual(80d, roundTripped.SevenDay!.UsedPercent);
    }

    public static void BuildsClaudeStateWithFiveHourLead()
    {
        var snapshot = ClaudeUsageSnapshot.Empty.Apply(
            new ClaudeRateLimits(
                new QuotaWindow(30, 300, Now.AddHours(1)),
                new QuotaWindow(80, 10_080, Now.AddDays(2))),
            "s",
            [1],
            Now);

        var live = snapshot.ToState(Now.AddMinutes(3));
        AssertEqual(ProviderConnection.Live, live.Connection);
        AssertEqual("claude:five_hour", live.Lead!.Key);
        AssertEqual("claude:seven_day", live.Windows[1].Key);
        var weeklyOnly = (snapshot with { FiveHour = null }).ToState(Now);
        AssertEqual("claude:seven_day", weeklyOnly.Lead!.Key);
        AssertEqual("Updated 3m ago", live.StatusText);

        var stale = snapshot.ToState(Now.AddHours(3));
        AssertEqual(ProviderConnection.Stale, stale.Connection);
        var fiveHour = stale.Windows.Single(window => window.Key == "claude:five_hour");
        AssertEqual(0d, fiveHour.Window.UsedPercent);
        Assert(fiveHour.Window.ResetsAt is null, "A window past its reset time has no known next reset.");

        var waiting = ClaudeUsageSnapshot.Empty.ToState(Now);
        Assert(!waiting.HasUsage, "An empty snapshot has no usage.");
        AssertEqual(ProviderConnection.Connecting, waiting.Connection);

        var withoutLimits = ClaudeUsageSnapshot.Empty
            .Apply(new ClaudeRateLimits(null, null), "s", [1, 2], Now)
            .ToState(Now.AddMinutes(1));
        AssertEqual("No plan limits", withoutLimits.StatusText);
        Assert(
            !snapshot.Serialize().Contains("HasLimits", StringComparison.Ordinal),
            "Computed properties are not written to the snapshot file.");
    }

    public static void ParsesCursorTeamSpend()
    {
        const string json = """
            {
              "teamMemberSpend": [
                { "email": "other@example.com", "spendCents": 9000 },
                { "email": "Dev@Example.com", "spendCents": 2500, "overallSpendCents": 4500,
                  "hardLimitOverrideDollars": 0, "monthlyLimitDollars": 100, "fastPremiumRequests": 12 }
              ],
              "subscriptionCycleStart": 1789900000000
            }
            """;

        Assert(CursorSpendParser.TryParse(json, " dev@example.com ", out var spend), "Member spend should parse.");
        AssertEqual(2500d, spend.SpendCents);
        AssertEqual(100d, spend.LimitDollars);
        var state = CursorSpendParser.ToState(spend, Now);
        AssertEqual(25d, state.Lead!.Window.UsedPercent);
        AssertEqual("$25.00 of $100 on-demand  ·  $45.00 total", state.Summary);
        Assert(state.Lead.Window.ResetsAt > Now, "The next billing cycle should be in the future.");

        Assert(!CursorSpendParser.TryParse(json, "missing@example.com", out _), "Unknown members must not match.");
        Assert(!CursorSpendParser.TryParse("{}", "dev@example.com", out _), "Unexpected payloads must be rejected.");
    }

    public static void ShowsCursorSpendWithoutLimit()
    {
        const string json = """{ "teamMemberSpend": [ { "email": "dev@example.com", "spendCents": 1234 } ] }""";
        Assert(CursorSpendParser.TryParse(json, "dev@example.com", out var spend), "Spend without limits parses.");
        var state = CursorSpendParser.ToState(spend, Now);
        Assert(!state.HasUsage, "Without a limit there is no percentage.");
        AssertEqual("$12.34 on-demand", state.Summary);
    }

    public static void MapsCodexStatuses()
    {
        AssertEqual(ProviderConnection.SignedOut, CodexStatusMapper.FromStatus("Signed out").Connection);
        AssertEqual(ProviderConnection.Unavailable, CodexStatusMapper.FromStatus("CLI not found").Connection);
        AssertEqual(ProviderConnection.Disconnected, CodexStatusMapper.FromStatus(CodexStatusMapper.Disconnected).Connection);
        AssertEqual(ProviderConnection.Connecting, CodexStatusMapper.FromStatus("Connecting…").Connection);

        var snapshot = new UsageSnapshot(
            new RateLimitBucket(
                "codex",
                "All Codex models",
                new QuotaWindow(10, 300, Now.AddHours(1)),
                new QuotaWindow(96, 10_080, Now.AddDays(1)),
                "pro"),
            Array.Empty<RateLimitBucket>(),
            Now);
        var state = CodexStatusMapper.FromSnapshot(snapshot);
        AssertEqual("Weekly limit", state.Lead!.Label);
        Assert(state.Windows.All(window => window.Key.StartsWith("codex:", StringComparison.Ordinal)),
            "Codex window keys must be namespaced.");
    }

    public static void RecognizesSupportedApps()
    {
        AssertEqual(
            UsageProviderKind.Codex,
            HostApplicationIdentity.Classify(
                @"C:\Program Files\WindowsApps\OpenAI.Codex_26.830.1000.0_x64__2p2nqsd0c76g0\ChatGPT.exe",
                "ChatGPT",
                "ChatGPT"));
        AssertEqual(
            UsageProviderKind.Claude,
            HostApplicationIdentity.Classify(
                @"C:\Users\dev\AppData\Local\AnthropicClaude\app-1.2.3\claude.exe",
                "claude",
                "Claude"));
        AssertEqual(
            UsageProviderKind.Cursor,
            HostApplicationIdentity.Classify(
                @"C:\Users\dev\AppData\Local\Programs\cursor\Cursor.exe",
                "Cursor",
                "main.cs - project - Cursor"));
        AssertEqual<UsageProviderKind?>(
            null,
            HostApplicationIdentity.Classify(@"C:\Windows\explorer.exe", "explorer", "Claude"));
    }

    public static void LoadsProviderSettingsSafely()
    {
        var legacy = JsonSerializer.Deserialize<OverlaySettings>("""{ "Theme": 1 }""")!.Normalize();
        Assert(legacy.CodexEnabled, "Codex stays on for existing users.");
        Assert(!legacy.ClaudeEnabled, "Claude Code integration must be opt-in.");
        Assert(!legacy.CursorEnabled, "Cursor must be opt-in.");
        Assert(!legacy.ShowOtherApps, "The card shows only the app in front by default.");
        Assert(legacy.ClaudeStatusLineEnabled, "The official Claude source stays the default.");
        Assert(!legacy.ClaudeAccountUsageEnabled, "Unofficial Claude usage must be opt-in.");
        Assert(!legacy.CursorAccountUsageEnabled, "Unofficial Cursor usage must be opt-in.");
        AssertEqual(string.Empty, legacy.CursorAccountEmail);

        var padded = new OverlaySettings { CursorAccountEmail = "  dev@example.com  " }.Normalize();
        AssertEqual("dev@example.com", padded.CursorAccountEmail);
    }

    public static void TracksThresholdsPerProvider()
    {
        var tracker = new UsageThresholdTracker();
        UsageWindowDisplay Window(string key, double used) => new(key, key, new QuotaWindow(used, 300, null));

        tracker.Evaluate([Window("codex:a", 10), Window("claude:a", 10)], 70, 90);
        var alerts = tracker.Evaluate([Window("codex:a", 10), Window("claude:a", 75)], 70, 90);
        AssertEqual(1, alerts.Count);
        AssertEqual("claude:a", alerts[0].Window.Key);
    }

    public static void ParsesClaudeAccountUsage()
    {
        Assert(
            ClaudeAccountUsageParser.TryReadCredentials(
                """{ "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc", "refreshToken": "r", "expiresAt": 1789900000000 } }""",
                out var token,
                out var expiresAt),
            "Claude Code credentials should parse.");
        AssertEqual("sk-ant-oat01-abc", token);
        AssertEqual(DateTimeOffset.FromUnixTimeMilliseconds(1789900000000), expiresAt);
        Assert(!ClaudeAccountUsageParser.TryReadCredentials("{}", out _, out _), "Missing tokens are rejected.");

        const string usage = """
            {
              "five_hour": { "utilization": 33.0, "resets_at": "2026-10-20T15:00:00.528743+00:00" },
              "seven_day": { "utilization": 13.0, "resets_at": "2026-10-24T00:59:59.951713+00:00" },
              "seven_day_opus": null,
              "extra_usage": { "is_enabled": false, "utilization": null }
            }
            """;
        Assert(ClaudeAccountUsageParser.TryParseUsage(usage, out var limits), "Account usage should parse.");
        AssertEqual(33d, limits.FiveHour!.UsedPercent);
        AssertEqual(13d, limits.SevenDay!.UsedPercent);
        AssertEqual(new DateTimeOffset(2026, 10, 24, 0, 59, 59, 951, TimeSpan.Zero).AddTicks(7130), limits.SevenDay.ResetsAt);
        Assert(!ClaudeAccountUsageParser.TryParseUsage("{}", out _), "Responses without limits are rejected.");
    }

    public static void ParsesCursorAccountUsage()
    {
        const string summary = """
            {
              "billingCycleStart": "2026-10-01T00:00:00.000Z",
              "billingCycleEnd": "2026-11-01T00:00:00.000Z",
              "membershipType": "pro",
              "isUnlimited": false,
              "individualUsage": {
                "plan": { "enabled": true, "used": 1200, "limit": 2000, "autoPercentUsed": 30, "apiPercentUsed": 12, "totalPercentUsed": 42 },
                "onDemand": { "enabled": false, "used": 0, "limit": null }
              }
            }
            """;
        Assert(CursorAccountUsageParser.TryParse(summary, Now, out var state), "Usage summary should parse.");
        AssertEqual(42d, state.Lead!.Window.UsedPercent);
        AssertEqual(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), state.Lead.Window.ResetsAt);
        AssertEqual("Auto 30%  ·  API 12%", state.Summary);

        const string editor = """{ "billingCycleStart": "1790000000000", "planUsage": { "totalSpend": 900, "limit": 2000, "used": 900 } }""";
        Assert(CursorAccountUsageParser.TryParse(editor, Now, out var fallback), "Editor usage should parse.");
        AssertEqual(45d, fallback.Lead!.Window.UsedPercent);
        Assert(!CursorAccountUsageParser.TryParse("{}", Now, out _), "Unexpected payloads are rejected.");
    }

    public static void ReadsJwtClaims()
    {
        static string Encode(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{Encode("{\"alg\":\"none\"}")}.{Encode("{\"sub\":\"auth0|user_01ABC\",\"exp\":1790000000}")}.sig";
        Assert(JwtClaims.TryRead(token, out var subject, out var expiresAt), "JWT claims should parse.");
        AssertEqual("auth0|user_01ABC", subject);
        AssertEqual(DateTimeOffset.FromUnixTimeSeconds(1790000000), expiresAt);
        Assert(!JwtClaims.TryRead("not-a-jwt", out _, out _), "Malformed tokens are rejected.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
