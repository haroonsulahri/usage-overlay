using CodexUsage.Core.Models;
using CodexUsageOverlay.Infrastructure;
using CodexUsageOverlay.Services;

var logger = new AppLogger();
await using var client = new AppServerClient(logger);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var snapshotReceived = new TaskCompletionSource<UsageSnapshot>(
    TaskCreationOptions.RunContinuationsAsynchronously);

client.StatusChanged += (_, status) => Console.WriteLine($"STATUS {status}");
client.SnapshotChanged += (_, snapshot) => snapshotReceived.TrySetResult(snapshot);

var runTask = client.RunAsync(timeout.Token);

try
{
    var snapshot = await snapshotReceived.Task.WaitAsync(timeout.Token);
    var used = Math.Round(snapshot.Primary.Primary.UsedPercent);
    var left = Math.Round(CodexUsage.Core.UsageLevelResolver.RemainingFromUsed(used));
    Console.WriteLine(
        $"PASS  Live Codex usage: {left}% left, {used}% used " +
        $"for {snapshot.Primary.Id}; {snapshot.Additional.Count} additional bucket(s).");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("FAIL  No live usage snapshot arrived within 20 seconds.");
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
