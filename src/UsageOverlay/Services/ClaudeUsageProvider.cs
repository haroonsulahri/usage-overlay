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
    private static readonly TimeSpan AccountPollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumAccountBackoff = TimeSpan.FromMinutes(15);

    private readonly AppLogger _logger;
    private readonly string _snapshotPath;
    private readonly Dictionary<int, (bool IsRunning, DateTimeOffset CheckedAt)> _processChecks = new();
    private readonly ClaudeAccountUsageClient _accountClient;
    private ClaudeUsageSnapshot _snapshot = ClaudeUsageSnapshot.Empty;
    private volatile ClaudeAccountFetch? _accountProblem;
    private volatile bool _accountUsageEnabled;
    private volatile bool _refreshAccountNow;

    public ClaudeUsageProvider(AppLogger logger, ClaudeCodeIntegration integration, ClaudeAccountUsageClient accountClient)
        : base(UsageProviderKind.Claude)
    {
        _logger = logger;
        Integration = integration;
        _accountClient = accountClient;
        _snapshotPath = AppPaths.ClaudeSnapshotPath;
    }

    public ClaudeCodeIntegration Integration { get; }

    public string IntegrationMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Applies the Claude settings: the official status-line source and the unofficial
    /// account source are independent. Returns a message for Settings.
    /// </summary>
    public async Task<string> ApplyAsync(bool enabled, bool statusLine, bool accountUsage)
    {
        if (!enabled)
        {
            await StopAsync().ConfigureAwait(false);
            _snapshot = ClaudeUsageSnapshot.Empty;
            return Disconnect();
        }

        _accountUsageEnabled = accountUsage;
        _refreshAccountNow = true;
        if (statusLine)
        {
            var result = Integration.Connect();
            IntegrationMessage = result.Message;
            if (!result.Success)
            {
                _logger.Error(result.Message);
                if (!accountUsage)
                {
                    await StopAsync(ProviderUsageState.Unavailable(
                        UsageProviderKind.Claude, ProviderConnection.Unavailable, "Not connected", result.Message))
                        .ConfigureAwait(false);
                    return result.Message;
                }
            }
            else
            {
                _logger.Info("Claude Code status-line integration is connected.");
            }
        }
        else
        {
            Disconnect();
            IntegrationMessage = string.Empty;
        }

        Start();
        return accountUsage && !statusLine
            ? "Reading usage from your Claude sign-in."
            : IntegrationMessage;
    }

    private string Disconnect()
    {
        var result = Integration.Disconnect();
        IntegrationMessage = result.Message;
        (result.Success ? (Action<string>)_logger.Info : _logger.Error)(result.Message);
        return result.Message;
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
            _refreshAccountNow = true;
            ReadSnapshot();
            Publish(_accountUsageEnabled && !_snapshot.HasLimits
                ? ProviderUsageState.Connecting(UsageProviderKind.Claude, "Checking Claude usage…")
                : CurrentState(DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _accountClient.Dispose();
    }

    private ProviderUsageState CurrentState(DateTimeOffset now)
    {
        var state = _snapshot.ToState(now);
        if (_accountUsageEnabled && _accountProblem is { } problem)
        {
            return !state.HasUsage || problem.Connection == ProviderConnection.SignedOut
                ? ProviderUsageState.Unavailable(UsageProviderKind.Claude, problem.Connection, problem.StatusText, problem.Problem ?? string.Empty)
                : state with { Connection = ProviderConnection.Stale, StatusText = problem.StatusText, Detail = $"Showing the last reading. {problem.Problem}" };
        }

        return state;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        string? revision = null;
        var nextPublish = DateTimeOffset.MinValue;
        var nextAccountFetch = DateTimeOffset.MinValue;
        var accountBackoff = AccountPollInterval;
        _accountProblem = null;
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            if (_accountUsageEnabled && (_refreshAccountNow || DateTimeOffset.UtcNow >= nextAccountFetch))
            {
                _refreshAccountNow = false;
                var fetch = await _accountClient.FetchAsync(cancellationToken).ConfigureAwait(false);
                if (fetch.Limits is { } limits)
                {
                    ClaudeSnapshotStore.Update(limits, null, Array.Empty<int>(), null);
                    _accountProblem = null;
                    accountBackoff = AccountPollInterval;
                }
                else
                {
                    if (_accountProblem != fetch)
                    {
                        _logger.Error($"Claude account usage: {fetch.Problem}");
                    }

                    _accountProblem = fetch;
                    accountBackoff = fetch.RateLimited
                        ? TimeSpan.FromTicks(Math.Min(accountBackoff.Ticks * 2, MaximumAccountBackoff.Ticks))
                        : AccountPollInterval;
                }

                nextAccountFetch = DateTimeOffset.UtcNow + accountBackoff;
                nextPublish = DateTimeOffset.MinValue;
            }

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
                Publish(CurrentState(now));
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
