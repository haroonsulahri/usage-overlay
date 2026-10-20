using CodexUsage.Core.Providers;

namespace UsageOverlay.Services;

/// <summary>
/// A background source of usage for one AI app. State changes may be raised on any
/// thread; consumers marshal to the UI themselves.
/// </summary>
public abstract class UsageProvider : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _runCancellation;
    private Task _runTask = Task.CompletedTask;

    protected UsageProvider(UsageProviderKind kind)
    {
        Kind = kind;
        State = ProviderUsageState.Off(kind);
    }

    public event EventHandler<ProviderUsageState>? StateChanged;

    public UsageProviderKind Kind { get; }

    public ProviderUsageState State { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runCancellation is { IsCancellationRequested: false };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_runCancellation is { IsCancellationRequested: false })
            {
                return;
            }

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            var token = _runCancellation.Token;
            _runTask = Task.Run(() => RunGuardedAsync(token), CancellationToken.None);
        }
    }

    public async Task StopAsync(ProviderUsageState? finalState = null)
    {
        Task runTask;
        lock (_gate)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        await runTask.ConfigureAwait(false);
        Publish(finalState ?? ProviderUsageState.Off(Kind), force: true);
    }

    public virtual Task RefreshAsync() => Task.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
        }

        GC.SuppressFinalize(this);
    }

    protected abstract Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Publishes a state unless the provider has been stopped.</summary>
    protected void Publish(ProviderUsageState state, bool force = false)
    {
        lock (_gate)
        {
            if (!force && _runCancellation is not { IsCancellationRequested: false })
            {
                return;
            }

            State = state;
        }

        StateChanged?.Invoke(this, state);
    }

    private async Task RunGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop.
        }
    }
}
