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

SettingsPersistenceSpecs.Run();
var projectStateFile = Path.GetTempFileName();
try
{
    var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    File.WriteAllText(projectStateFile, JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["selected-project"] = new { type = "local", projectId = "project-1" },
        ["local-projects"] = new Dictionary<string, object> { ["project-1"] = new { name = "Overlay", rootPaths = new[] { repository } } }
    }));
    var selected = ProjectGitStatus.ReadSelectedProject(projectStateFile);
    Assert(selected?.Root == repository, "Resolves the selected project's single root");
    var status = await ProjectGitStatus.ReadAsync(selected!);
    Assert(status.IsRepository && status.Branches >= 1 && status.LastCommit is not null, "Reads local branch count and last commit");
    Assert(ProjectGitStatus.Format(status with { LastCommit = DateTimeOffset.Now.AddHours(1) }, DateTimeOffset.Now).Contains("in the future"), "Distinguishes a future commit");
    var fixture = Path.Combine(Path.GetTempPath(), "usage-overlay-project-spec-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixture);
    try
    {
        var nonRepo = await ProjectGitStatus.ReadAsync(new SelectedProject("none", "Temp", fixture));
        Assert(!nonRepo.IsRepository && nonRepo.Branches is null && nonRepo.LastCommit is null, "Does not invent Git values for a non-repository");
        Directory.CreateDirectory(Path.Combine(fixture, "one", ".git"));
        var single = await ProjectGitStatus.ReadAsync(new SelectedProject("single", "Single", fixture));
        Assert(single.RepositoryRoot == Path.Combine(fixture, "one") && !single.MultipleRepositories, "Finds a single nested repository");
        Directory.CreateDirectory(Path.Combine(fixture, "two", ".git"));
        var ambiguous = await ProjectGitStatus.ReadAsync(new SelectedProject("multi", "Multiple", fixture));
        Assert(!ambiguous.IsRepository && ambiguous.MultipleRepositories, "Reports ambiguous nested repositories without choosing one");
        Directory.CreateDirectory(Path.Combine(fixture, "app", ".git"));
        var appRepo = await ProjectGitStatus.ReadAsync(new SelectedProject("app", "App", fixture));
        Assert(appRepo.RepositoryRoot == Path.Combine(fixture, "app") && appRepo.MultipleRepositories && ProjectGitStatus.Format(appRepo, DateTimeOffset.Now).Contains("Repository: app (of several)"), "Labels app when several nested repositories exist");
    }
    finally { Directory.Delete(fixture, true); }
    File.WriteAllText(projectStateFile, "{invalid");
    Assert(ProjectGitStatus.ReadSelectedProject(projectStateFile) is null, "Ignores malformed project state");
    File.WriteAllText(projectStateFile, """{"selected-project":{"type":"local","projectId":"p"},"local-projects":{"p":{"name":"multi","rootPaths":["C:/one","C:/two"]}}}""");
    Assert(ProjectGitStatus.ReadSelectedProject(projectStateFile) is null, "Does not choose an arbitrary root in a multi-root project");
}
finally { File.Delete(projectStateFile); }
ClaudeIntegrationSpecs.Run();
await ClaudeSignInSpecs.RunAsync();
await ClaudeRenewalSpecs.RunAsync();

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
