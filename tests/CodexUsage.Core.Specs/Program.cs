using CodexUsage.Core;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Settings;
using CodexUsage.Core.Security;
using System.Text.Json;

var specs = new (string Name, Action Run)[]
{
    ("Parses a multi-bucket response", ParsesMultiBucketResponse),
    ("Parses a rate-limit update notification", ParsesUpdateNotification),
    ("Clamps malformed percentage values", ClampsPercentage),
    ("Merges partial bucket updates", MergesPartialUpdate),
    ("Formats reset countdowns", FormatsResetCountdown),
    ("Resolves warning thresholds", ResolvesUsageLevels),
    ("Calculates remaining quota", CalculatesRemainingQuota),
    ("Normalizes persistent display settings", NormalizesDisplaySettings),
    ("Redacts secrets from diagnostic logs", RedactsDiagnosticSecrets),
    ("Loads legacy settings with new safe defaults", LoadsLegacySettings)
};

var failures = new List<string>();
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine($"PASS  {spec.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL  {spec.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"{specs.Length} specs passed.");

static void ParsesMultiBucketResponse()
{
    const string json = """
        {
          "id": 7,
          "result": {
            "rateLimits": {
              "limitId": "codex",
              "primary": { "usedPercent": 63, "windowDurationMins": 10080, "resetsAt": 1787233304 }
            },
            "rateLimitsByLimitId": {
              "codex": {
                "limitId": "codex",
                "limitName": null,
                "primary": { "usedPercent": 63, "windowDurationMins": 10080, "resetsAt": 1787233304 },
                "secondary": null,
                "planType": "pro"
              },
              "codex_spark": {
                "limitId": "codex_spark",
                "limitName": "GPT-5.3 Codex Spark",
                "primary": { "usedPercent": 0, "windowDurationMins": 10080, "resetsAt": 1787571753 }
              }
            }
          }
        }
        """;

    Assert(RateLimitParser.TryParse(json, out var snapshot), "Expected response to parse.");
    Assert(snapshot is not null, "Snapshot should not be null.");
    AssertEqual("codex", snapshot!.Primary.Id);
    AssertEqual(63d, snapshot.Primary.Primary.UsedPercent);
    AssertEqual("pro", snapshot.Primary.PlanType);
    AssertEqual(1, snapshot.Additional.Count);
    AssertEqual("GPT-5.3 Codex Spark", snapshot.Additional[0].DisplayName);
}

static void ParsesUpdateNotification()
{
    const string json = """
        {
          "method": "account/rateLimits/updated",
          "params": {
            "rateLimits": {
              "limitId": "codex",
              "primary": { "usedPercent": 64, "windowDurationMins": 10080, "resetsAt": 1787233304 }
            }
          }
        }
        """;

    Assert(RateLimitParser.TryParse(json, out var snapshot), "Expected notification to parse.");
    AssertEqual(64d, snapshot!.Primary.Primary.UsedPercent);
}

static void ClampsPercentage()
{
    const string json = """
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": { "usedPercent": 142, "windowDurationMins": 15, "resetsAt": 1787233304 }
          }
        }
        """;

    Assert(RateLimitParser.TryParse(json, out var snapshot), "Expected direct payload to parse.");
    AssertEqual(100d, snapshot!.Primary.Primary.UsedPercent);
}

static void MergesPartialUpdate()
{
    var now = DateTimeOffset.UtcNow;
    var original = new UsageSnapshot(
        Bucket("codex", 63),
        new[] { Bucket("codex_spark", 4) },
        now);
    var update = new UsageSnapshot(Bucket("codex", 65), Array.Empty<RateLimitBucket>(), now.AddSeconds(1));

    var merged = original.MergePartial(update);
    AssertEqual(65d, merged.Primary.Primary.UsedPercent);
    AssertEqual(1, merged.Additional.Count);
    AssertEqual(4d, merged.Additional[0].Primary.UsedPercent);
}

static void FormatsResetCountdown()
{
    var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    AssertEqual("Resets in 3d 2h", ResetTimeFormatter.Format(now.AddDays(3).AddHours(2), now));
    AssertEqual("Resets in 45m", ResetTimeFormatter.Format(now.AddMinutes(45), now));
    AssertEqual("Resetting now", ResetTimeFormatter.Format(now.AddSeconds(-1), now));
}

static void ResolvesUsageLevels()
{
    AssertEqual(UsageLevel.Normal, UsageLevelResolver.FromPercentage(69.9));
    AssertEqual(UsageLevel.Warning, UsageLevelResolver.FromPercentage(70));
    AssertEqual(UsageLevel.Critical, UsageLevelResolver.FromPercentage(90));
    AssertEqual(UsageLevel.Warning, UsageLevelResolver.FromPercentage(65, 60, 85));
    AssertEqual(UsageLevel.Critical, UsageLevelResolver.FromPercentage(85, 60, 85));
}

static void CalculatesRemainingQuota()
{
    AssertEqual(27d, UsageLevelResolver.RemainingFromUsed(73));
    AssertEqual(100d, UsageLevelResolver.RemainingFromUsed(-5));
    AssertEqual(0d, UsageLevelResolver.RemainingFromUsed(125));
}

static void NormalizesDisplaySettings()
{
    var now = DateTimeOffset.UtcNow;
    var settings = new OverlaySettings
    {
        Placement = (HorizontalPlacement)999,
        HorizontalOffset = 900,
        VerticalOffset = -500,
        CustomXRatio = 2,
        CustomYRatio = -1,
        PrimaryDisplay = (PrimaryUsageDisplay)999,
        WarningThreshold = 95,
        CriticalThreshold = 80,
        CodexCliPath = "  C:\\Tools\\codex.cmd  ",
        RefreshIntervalSeconds = 2,
        PausedUntil = now.AddMinutes(15)
    }.Normalize();

    AssertEqual(HorizontalPlacement.RightEdge, settings.Placement);
    AssertEqual(600d, settings.HorizontalOffset);
    AssertEqual(-300d, settings.VerticalOffset);
    AssertEqual(1d, settings.CustomXRatio);
    AssertEqual(0d, settings.CustomYRatio);
    AssertEqual(PrimaryUsageDisplay.Remaining, settings.PrimaryDisplay);
    AssertEqual(79d, settings.WarningThreshold);
    AssertEqual(80d, settings.CriticalThreshold);
    AssertEqual("C:\\Tools\\codex.cmd", settings.CodexCliPath);
    AssertEqual(15, settings.RefreshIntervalSeconds);
    Assert(settings.IsPaused(now), "Expected the future pause to be active.");
    Assert(!settings.IsPaused(now.AddMinutes(16)), "Expected the expired pause to be inactive.");
}

static void RedactsDiagnosticSecrets()
{
    const string message =
        "Authorization: Bearer abc.def.ghi access_token=token-value api_key=key-value sk-example123456789";
    var sanitized = LogSanitizer.Sanitize(message);

    Assert(!sanitized.Contains("abc.def.ghi", StringComparison.Ordinal), "Bearer token was not redacted.");
    Assert(!sanitized.Contains("token-value", StringComparison.Ordinal), "Access token was not redacted.");
    Assert(!sanitized.Contains("key-value", StringComparison.Ordinal), "API key was not redacted.");
    Assert(!sanitized.Contains("sk-example123456789", StringComparison.Ordinal), "OpenAI key was not redacted.");
    Assert(sanitized.Contains("[REDACTED]", StringComparison.Ordinal), "Redaction marker was missing.");
}

static void LoadsLegacySettings()
{
    const string legacyJson = """
        {
          "Placement": 2,
          "HorizontalOffset": 0,
          "VerticalOffset": 0,
          "CustomXRatio": 0.8,
          "CustomYRatio": 0.75,
          "HideInFullscreen": false,
          "PausedUntil": null
        }
        """;

    var settings = JsonSerializer.Deserialize<OverlaySettings>(legacyJson)?.Normalize();
    Assert(settings is not null, "Legacy settings failed to deserialize.");
    Assert(settings!.ShowOnlyWhenCodexActive, "Codex-only visibility default was not preserved.");
    Assert(settings.FollowCodexAcrossMonitors, "Monitor-following default was not preserved.");
    AssertEqual(PrimaryUsageDisplay.Remaining, settings.PrimaryDisplay);
    Assert(settings.AnimationsEnabled, "Animation default was not preserved.");
    Assert(settings.ShowCompactPercentage, "Compact percentage default was not preserved.");
    AssertEqual(60, settings.RefreshIntervalSeconds);
}

static RateLimitBucket Bucket(string id, double usedPercent)
{
    return new RateLimitBucket(
        id,
        id,
        new QuotaWindow(usedPercent, 10_080, DateTimeOffset.UtcNow.AddDays(1)),
        null,
        "pro");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
