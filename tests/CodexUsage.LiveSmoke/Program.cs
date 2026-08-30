using CodexUsage.Core.Models;
using CodexUsage.Core;
using System.Globalization;
using UsageOverlay.Infrastructure;
using UsageOverlay.Services;

var logger = new AppLogger();
await using var client = new AppServerClient(logger);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var firstSession = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
var snapshotReceived = new TaskCompletionSource<UsageSnapshot>(
    TaskCreationOptions.RunContinuationsAsynchronously);

client.StatusChanged += (_, status) => Console.WriteLine($"STATUS {status}");
client.SnapshotChanged += (_, snapshot) => snapshotReceived.TrySetResult(snapshot);

var runTask = client.RunAsync(firstSession.Token);

try
{
    var snapshot = await snapshotReceived.Task.WaitAsync(timeout.Token);
    var used = Math.Round(snapshot.Primary.Primary.UsedPercent);
    var left = Math.Round(CodexUsage.Core.UsageLevelResolver.RemainingFromUsed(used));
    Console.WriteLine(
        $"PASS  Live Codex usage: {left}% left, {used}% used " +
        $"for {snapshot.Primary.Id}; {snapshot.Additional.Count} additional bucket(s).");
    foreach (var row in UsageWindowDisplayBuilder.Build(snapshot))
    {
        Console.WriteLine(
            $"WINDOW {row.Label}: {UsageLevelResolver.RemainingFromUsed(row.Window.UsedPercent):0}% left; " +
            $"duration={row.Window.WindowDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} minutes.");
    }

    firstSession.Cancel();
    await runTask;
    if (client.IsProcessRunning)
    {
        throw new InvalidOperationException("The App Server process remained active after disconnect.");
    }
    Console.WriteLine("PASS  Disconnect released the App Server process.");

    snapshotReceived = new TaskCompletionSource<UsageSnapshot>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var secondSession = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
    runTask = client.RunAsync(secondSession.Token);
    _ = await snapshotReceived.Task.WaitAsync(timeout.Token);
    Console.WriteLine("PASS  Reconnect started a fresh App Server session.");
    secondSession.Cancel();
    await runTask;

    using var updateService = new ReleaseUpdateService();
    var release = await updateService.CheckAsync(timeout.Token);
    if (release.ReleaseUri.Host != "github.com" || string.IsNullOrWhiteSpace(release.LatestVersion))
    {
        throw new InvalidOperationException("The GitHub release check returned invalid metadata.");
    }
    Console.WriteLine(
        $"PASS  GitHub release check returned v{release.LatestVersion}; " +
        $"current build is v{release.CurrentVersion}.");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("FAIL  Live disconnect/reconnect verification did not finish within 30 seconds.");
    Environment.ExitCode = 1;
}
finally
{
    timeout.Cancel();
    await client.DisposeAsync();

    try
    {
        await runTask;
    }
    catch (OperationCanceledException)
    {
        // Expected after the smoke test receives its snapshot.
    }
}
