using CodexUsage.Core.Models;
using CodexUsage.Core.Providers;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>Codex usage through the overlay-owned <c>codex app-server</c> session.</summary>
public sealed class CodexUsageProvider : UsageProvider
{
    private readonly AppServerClient _client;

    public CodexUsageProvider(AppLogger logger, string cliPath, TimeSpan pollInterval)
        : base(UsageProviderKind.Codex)
    {
        CliPath = cliPath;
        _client = new AppServerClient(logger, cliPath, pollInterval);
        _client.SnapshotChanged += Client_OnSnapshotChanged;
        _client.StatusChanged += Client_OnStatusChanged;
    }

    public string CliPath { get; }

    public bool IsSuspended { get; private set; }

    public string? ResolvedCliPath => AppServerClient.ResolveCodexCommand(CliPath);

    public void Resume()
    {
        IsSuspended = false;
        Publish(CodexStatusMapper.FromStatus("Connecting…"), force: true);
        Start();
    }

    /// <summary>Releases the CLI process so an npm-installed Codex CLI can be upgraded.</summary>
    public async Task SuspendAsync()
    {
        IsSuspended = true;
        await StopAsync(CodexStatusMapper.FromStatus(CodexStatusMapper.Disconnected)).ConfigureAwait(false);
    }

    public override Task RefreshAsync() => _client.RefreshAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        Publish(CodexStatusMapper.FromStatus("Connecting…"));
        return _client.RunAsync(cancellationToken);
    }

    private void Client_OnSnapshotChanged(object? sender, UsageSnapshot snapshot) =>
        Publish(CodexStatusMapper.FromSnapshot(snapshot));

    private void Client_OnStatusChanged(object? sender, string status)
    {
        // "Live" always accompanies a snapshot, which carries the usage itself.
        if (status != "Live")
        {
            Publish(CodexStatusMapper.FromStatus(status));
        }
    }
}
