using System.IO;
using CodexUsage.Core.Providers;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>
/// Claude plan limits recorded by the status-line relay. The limits are account-wide, so
/// the same reading is shown beside the Claude desktop app and Claude Code terminals.
/// </summary>
public sealed class ClaudeUsageProvider : UsageProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RepublishInterval = TimeSpan.FromSeconds(30);

    private readonly AppLogger _logger;
    private readonly string _snapshotPath;
    private readonly Dictionary<int, (bool IsRunning, DateTimeOffset CheckedAt)> _processChecks = new();
    private ClaudeUsageSnapshot _snapshot = ClaudeUsageSnapshot.Empty;

    public ClaudeUsageProvider(AppLogger logger, ClaudeCodeIntegration integration)
        : base(UsageProviderKind.Claude)
    {
        _logger = logger;
        Integration = integration;
        _snapshotPath = AppPaths.ClaudeSnapshotPath;
    }

    public ClaudeCodeIntegration Integration { get; }

    public string IntegrationMessage { get; private set; } = string.Empty;

    /// <summary>Connects Claude Code and starts reading; returns false if setup failed.</summary>
    public bool Enable()
    {
        var result = Integration.Connect();
        IntegrationMessage = result.Message;
        if (!result.Success)
        {
            _logger.Error(result.Message);
            Publish(
                ProviderUsageState.Unavailable(
                    UsageProviderKind.Claude, ProviderConnection.Unavailable, "Not connected", result.Message),
                force: true);
            return false;
        }

        _logger.Info("Claude Code status-line integration is connected.");
        Start();
        return true;
    }

    public async Task DisableAsync()
    {
        await StopAsync().ConfigureAwait(false);
        var result = Integration.Disconnect();
        IntegrationMessage = result.Message;
        _snapshot = ClaudeUsageSnapshot.Empty;
        (result.Success ? (Action<string>)_logger.Info : _logger.Error)(result.Message);
    }

    /// <summary>True when the process owns a window running Claude Code. Called on the UI thread.</summary>
    public bool IsClaudeCodeHost(uint processId) =>
        IsRunning && _snapshot.IsHostProcess(processId, DateTimeOffset.UtcNow, IsProcessRunning);

    // The window tracker asks several times a second, so cache each answer briefly.
    private bool IsProcessRunning(int processId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_processChecks.TryGetValue(processId, out var cached) && now - cached.CheckedAt < TimeSpan.FromSeconds(5))
        {
            return cached.IsRunning;
        }

        bool isRunning;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            isRunning = !process.HasExited;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Access-denied processes still exist; a missing one throws ArgumentException.
            isRunning = exception is not ArgumentException;
        }

        if (_processChecks.Count > 64)
        {
            _processChecks.Clear();
        }

        _processChecks[processId] = (isRunning, now);
        return isRunning;
    }

    public override Task RefreshAsync()
    {
        if (IsRunning)
        {
            ReadSnapshot();
            Publish(_snapshot.ToState(DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        string? revision = null;
        var nextPublish = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            var current = ReadRevision();
            var now = DateTimeOffset.UtcNow;
            if (current != revision)
            {
                revision = current;
                ReadSnapshot();
                nextPublish = DateTimeOffset.MinValue;
            }

            if (now >= nextPublish)
            {
                Publish(_snapshot.ToState(now));
                nextPublish = now + RepublishInterval;
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private string ReadRevision()
    {
        try
        {
            var file = new FileInfo(_snapshotPath);
            return file.Exists ? $"{file.LastWriteTimeUtc.Ticks}:{file.Length}" : "missing";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unavailable";
        }
    }

    private void ReadSnapshot()
    {
        try
        {
            _snapshot = File.Exists(_snapshotPath) &&
                        ClaudeUsageSnapshot.TryDeserialize(File.ReadAllText(_snapshotPath), out var parsed)
                ? parsed
                : ClaudeUsageSnapshot.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The relay may be replacing the file; the next poll reads it again.
        }
    }
}
