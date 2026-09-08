using System.Collections.Concurrent;
using System.Text.Json;
using UsageOverlay.Infrastructure;
using UsageOverlay.Services;

if (args.Contains("--fake"))
{
    var auth = Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME")!, "auth.json");
    var account = File.Exists(auth) ? File.ReadAllText(auth) : null;
    while (Console.ReadLine() is { } line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var requestId)) continue;
        var id = requestId.GetInt32();
        var method = root.GetProperty("method").GetString();
        if (method == "initialize")
            Reply(new { id, result = new { } });
        else if (method == "account/read")
        {
            if (root.GetProperty("params").GetProperty("refreshToken").GetBoolean())
                throw new InvalidOperationException("Routine polling forced a token refresh.");
            Reply(new { id, result = new { account = account is null ? null : new { type = "chatgpt", email = account } } });
        }
        else if (method == "account/rateLimits/read")
        {
            var percent = account == "A" ? 70 : account == "B" ? 20 : 40;
            Reply(new { id, result = new { rateLimits = new { limitId = "codex", primary = new { usedPercent = percent, windowDurationMins = 300, resetsAt = 1900000000 } } } });
            // A delayed reply from an invalidated/unknown request must not repaint usage.
            Reply(new { id = -1, result = new { rateLimits = new { limitId = "codex", primary = new { usedPercent = 99, windowDurationMins = 300, resetsAt = 1900000000 } } } });
        }
    }
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "usage-overlay-switch-spec-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var oldHome = Environment.GetEnvironmentVariable("CODEX_HOME");
Environment.SetEnvironmentVariable("CODEX_HOME", directory);
var authPath = Path.Combine(directory, "auth.json");
var launcher = Path.Combine(directory, "fake-codex.cmd");
File.WriteAllText(launcher, $"@echo off\r\n\"{Environment.ProcessPath}\" --fake\r\n");
File.WriteAllText(authPath, "A");
var snapshots = new ConcurrentQueue<double>();
var statuses = new ConcurrentQueue<string>();
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(75));
await using var client = new AppServerClient(new AppLogger(), launcher, TimeSpan.FromSeconds(2));
client.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot.Primary.Primary.UsedPercent);
client.StatusChanged += (_, status) => statuses.Enqueue(status);
var run = client.RunAsync(cancellation.Token);
try
{
    await Until(() => snapshots.Contains(70), "Initial account A loads");
    File.Delete(authPath);
    await Until(() => statuses.Contains("Signed out"), "External logout detected without restart");
    snapshots.Clear();
    File.WriteAllText(authPath, "B");
    await Until(() => snapshots.Contains(20), "External login B loads automatically");
    Assert(!snapshots.Contains(70), "Previous account usage is not reused");
    snapshots.Clear();
    File.WriteAllText(authPath, "C");
    await Until(() => snapshots.Contains(40), "Direct B to C switch detected");
    Assert(!snapshots.Contains(99), "Uncorrelated late responses are discarded");
    cancellation.Cancel();
    await run.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!client.IsProcessRunning, "Overlay-owned child stops on cancellation");
    Console.WriteLine("All account-switch integration checks passed.");
}
finally
{
    cancellation.Cancel();
    await run;
    Environment.SetEnvironmentVariable("CODEX_HOME", oldHome);
    Directory.Delete(directory, recursive: true);
}

static void Reply(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}
static async Task Until(Func<bool> predicate, string label)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (!predicate()) await Task.Delay(100, timeout.Token);
    Console.WriteLine("PASS " + label);
}
