using System.Net;
using System.Text.Json;
using CodexUsage.Core.Reporting;
using CodexUsage.Core.Settings;

internal static class ReportingSpecs
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "overlay-reporting-spec-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "reporting.json");
            using var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
            var endpoint = new Uri("https://example.invalid/report");
            var reporter = new UsageReporter(client, endpoint, path, "0.3.0", () => now);
            Check(!new OverlaySettings().UsageReportingEnabled, "New installs must default off.");
            Check(!JsonSerializer.Deserialize<OverlaySettings>("{}")!.UsageReportingEnabled, "Upgrades must default off.");
            await reporter.ReportAsync(false, CancellationToken.None);
            Check(handler.Bodies.Count == 0 && !File.Exists(path), "Disabled reporting must not create an ID or send.");
            await reporter.ReportAsync(true, CancellationToken.None);
            Check(handler.Bodies.Count == 1, "First opted-in report is sent.");
            using var first = JsonDocument.Parse(handler.Bodies[0]);
            Check(first.RootElement.EnumerateObject().Count() == 2, "Payload must contain only ID and version.");
            var id = first.RootElement.GetProperty("installationId").GetGuid();
            Check(id != Guid.Empty && first.RootElement.GetProperty("appVersion").GetString() == "0.3.0", "Payload values.");
            await reporter.ReportAsync(true, CancellationToken.None);
            var restarted = new UsageReporter(client, endpoint, path, "0.3.1", () => now);
            await restarted.ReportAsync(true, CancellationToken.None);
            Check(handler.Bodies.Count == 1, "Repeated calls and restarts must not resend that day.");
            now = now.AddDays(1);
            await restarted.ReportAsync(true, CancellationToken.None);
            using var second = JsonDocument.Parse(handler.Bodies[1]);
            Check(second.RootElement.GetProperty("installationId").GetGuid() == id, "Upgrades preserve installation identity.");
            now = now.AddDays(1);
            handler.Fail = true;
            await restarted.ReportAsync(true, CancellationToken.None);
            await restarted.ReportAsync(true, CancellationToken.None);
            Check(handler.Bodies.Count == 3, "Network failure must not escape or retry in a loop.");
            now = now.AddDays(1);
            await restarted.ReportAsync(true, new CancellationToken(canceled: true));
            Check(handler.Bodies.Count == 3, "Canceled reporting must not send.");
            handler.Fail = false;
            handler.WaitForCancellation = true;
            using var cancellation = new CancellationTokenSource();
            var pending = restarted.ReportAsync(true, cancellation.Token);
            await handler.Started.Task;
            cancellation.Cancel();
            await pending;
            Check(handler.Canceled, "Consent withdrawal must cancel an in-flight request.");
            await File.WriteAllTextAsync(path, "broken json");
            now = now.AddDays(1);
            await restarted.ReportAsync(true, CancellationToken.None);
            Check(handler.Bodies.Count == 4, "Corrupt state must fail closed.");
            var invalidPath = new UsageReporter(client, endpoint, Path.Combine(directory, "missing", "state.json"), "0.3.0");
            await invalidPath.ReportAsync(true, CancellationToken.None);
            Check(handler.Bodies.Count == 4, "Storage failures must not send unpersisted IDs.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public bool Fail { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool Canceled { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Fail) throw new HttpRequestException("Offline fixture");
            if (WaitForCancellation)
            {
                Started.SetResult();
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
