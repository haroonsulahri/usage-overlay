using System.Collections.Concurrent;
using System.Diagnostics;
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

var claudeTestRoot = Path.Combine(Path.GetTempPath(), "usage-overlay-claude-spec-" + Guid.NewGuid().ToString("N"));
var claudeAppData = Path.Combine(claudeTestRoot, "appdata");
var claudeConfig = Path.Combine(claudeTestRoot, "claude");
Directory.CreateDirectory(claudeConfig);
var claudeSettingsPath = Path.Combine(claudeConfig, "settings.json");
File.WriteAllText(
    claudeSettingsPath,
    """
    { "env": { "CUSTOM": "keep" }, "statusLine": { "type": "command", "command": "printf 'my status'", "padding": 2 } }
    """);
try
{
    var claudeIntegration = new ClaudeUsageStatusLineIntegration(claudeAppData, claudeConfig);
    var enableResult = claudeIntegration.SetEnabled(true);
    Assert(enableResult.Success, "Claude status-line integration enables successfully.");
    using (var installedSettings = JsonDocument.Parse(File.ReadAllText(claudeSettingsPath)))
    {
        var installed = installedSettings.RootElement;
        Assert(installed.GetProperty("env").GetProperty("CUSTOM").GetString() == "keep", "Unrelated Claude environment settings are preserved.");
        Assert(installed.GetProperty("statusLine").GetProperty("command").GetString()!.Contains("claude-statusline.sh", StringComparison.Ordinal), "Claude command points to the managed relay.");
    }

    var relayScript = File.ReadAllText(Path.Combine(claudeAppData, "claude-statusline.sh"));
    Assert(relayScript.Contains("my status", StringComparison.Ordinal), "The prior custom status-line command is retained by the wrapper.");

    var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
    if (File.Exists(gitBash))
    {
        var relayProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = gitBash,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        relayProcess.StartInfo.ArgumentList.Add(Path.Combine(claudeAppData, "claude-statusline.sh"));
        Assert(relayProcess.Start(), "Claude status-line relay starts.");
        var relayOutputTask = relayProcess.StandardOutput.ReadToEndAsync();
        var relayErrorTask = relayProcess.StandardError.ReadToEndAsync();
        await relayProcess.StandardInput.WriteAsync(
            """
            {"session_id":"private-session","transcript_path":"private-transcript","rate_limits":{"five_hour":{"used_percentage":63.2,"resets_at":1900000000},"seven_day":{"used_percentage":22,"resets_at":1900500000}}}
            """);
        relayProcess.StandardInput.Close();
        await relayProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var relayOutput = await relayOutputTask;
        var relayError = await relayErrorTask;
        Assert(relayProcess.ExitCode == 0, "Claude status-line relay exits successfully: " + relayError);
        Assert(relayOutput.Contains("my status | 5h 63% | 7d 22%", StringComparison.Ordinal), $"Custom status-line output and Claude limits are combined. Output was: {relayOutput}");
        var claudeClient = new ClaudeUsageClient(claudeIntegration.SnapshotPath);
        Assert(claudeClient.TryReadSnapshot(out var claudeSnapshot) && claudeSnapshot is not null, "Claude status-line rate-limit snapshot is readable.");
        Assert(claudeSnapshot!.FiveHour!.UsedPercent == 63.2d && claudeSnapshot.SevenDay!.UsedPercent == 22d, "Five-hour and seven-day percentages are stored correctly.");
        using var savedSnapshot = JsonDocument.Parse(File.ReadAllText(claudeIntegration.SnapshotPath));
        Assert(!savedSnapshot.RootElement.TryGetProperty("session_id", out _) &&
               !savedSnapshot.RootElement.TryGetProperty("transcript_path", out _),
            "Session IDs and transcript paths are not persisted.");
    }
    else
    {
        Console.WriteLine("SKIP Git Bash unavailable; Claude relay process check not run.");
    }

    var disableResult = claudeIntegration.SetEnabled(false);
    Assert(disableResult.Success, "Claude status-line integration disables successfully.");
    using (var restoredSettings = JsonDocument.Parse(File.ReadAllText(claudeSettingsPath)))
    {
        var restored = restoredSettings.RootElement;
        Assert(restored.GetProperty("env").GetProperty("CUSTOM").GetString() == "keep", "Unrelated Claude settings stay unchanged after disconnect.");
        Assert(restored.GetProperty("statusLine").GetProperty("command").GetString() == "printf 'my status'", "The original custom status-line command is restored.");
        Assert(restored.GetProperty("statusLine").GetProperty("padding").GetInt32() == 2, "The original status-line options are restored.");
    }

    Console.WriteLine("PASS Claude status-line setup preserves and restores existing settings.");
}
finally
{
    if (Directory.Exists(claudeTestRoot)) Directory.Delete(claudeTestRoot, recursive: true);
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
