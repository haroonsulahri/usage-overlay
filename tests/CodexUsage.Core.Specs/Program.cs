using CodexUsage.Core;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;

var specs = new (string Name, Action Run)[]
{
    ("Parses a multi-bucket response", ParsesMultiBucketResponse),
    ("Parses a rate-limit update notification", ParsesUpdateNotification),
    ("Clamps malformed percentage values", ClampsPercentage),
    ("Merges partial bucket updates", MergesPartialUpdate),
    ("Formats reset countdowns", FormatsResetCountdown),
    ("Resolves warning thresholds", ResolvesUsageLevels)
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
