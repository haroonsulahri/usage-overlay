using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

public sealed class AppServerClient : IAsyncDisposable
{
    private readonly AppLogger _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _configuredCodexPath;
    private readonly TimeSpan _pollInterval;
    private readonly ConcurrentDictionary<int, byte> _accountReadRequestIds = new();
    private readonly ConcurrentDictionary<int, byte> _rateReadRequestIds = new();
    private readonly string _authPath = AuthFileRevision.ResolvePath();
    private bool _accountValidated;
    private string? _accountIdentity;
    private int _refreshPending;
    private DateTimeOffset _requestStarted;
    private Process? _process;
    private UsageSnapshot? _lastSnapshot;
    private int _nextRequestId;
    private int _initializeRequestId;
    private bool _initialized;

    public AppServerClient(
        AppLogger logger,
        string? configuredCodexPath = null,
        TimeSpan? pollInterval = null)
    {
        _logger = logger;
        _configuredCodexPath = configuredCodexPath;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
    }

    public event EventHandler<UsageSnapshot>? SnapshotChanged;

    public event EventHandler<string>? StatusChanged;

    public bool IsProcessRunning
    {
        get
        {
            var process = _process;
            if (process is null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error($"App Server session failed: {exception.Message}");
                StatusChanged?.Invoke(this, "Trying again…");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                    StatusChanged?.Invoke(this, "Connecting…");
                StopProcess();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return _initialized ? RefreshAccountAsync(cancellationToken) : Task.CompletedTask;
    }

    private Task RefreshAccountAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _refreshPending, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        _requestStarted = DateTimeOffset.UtcNow;
        var requestId = NextRequestId();
        _accountReadRequestIds.TryAdd(requestId, 0);
        return SendRequestAsync(
            requestId,
            "account/read",
            new { refreshToken = false },
            cancellationToken);
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var codexCommand = ResolveCodexCommand(_configuredCodexPath);
        if (codexCommand is null)
        {
            StatusChanged?.Invoke(this, "CLI not found");
            throw new FileNotFoundException(
                "Could not find codex.cmd or codex.exe on PATH. Set USAGE_OVERLAY_CODEX_PATH to override.");
        }

        _logger.Info($"Starting App Server through {codexCommand}.");
        StatusChanged?.Invoke(this, "Connecting…");
        var authRevision = AuthFileRevision.Read(_authPath);
        var sessionStarted = DateTimeOffset.UtcNow;
        var process = StartProcess(codexCommand);
        _process = process;
        _initialized = false;
        _accountReadRequestIds.Clear();
        _rateReadRequestIds.Clear();
        _accountValidated = false;
        _lastSnapshot = null;
        _accountIdentity = null;
        Interlocked.Exchange(ref _refreshPending, 0);

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                _logger.Error($"App Server: {eventArgs.Data}");
            }
        };
        process.BeginErrorReadLine();

        _initializeRequestId = NextRequestId();
        await SendAsync(
            new
            {
                method = "initialize",
                id = _initializeRequestId,
                @params = new
                {
                    clientInfo = new
                    {
                        name = "usage_overlay",
                        title = "Usage Overlay",
                        version = AppVersion.Current
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var read = process.StandardOutput.ReadLineAsync(readCancellation.Token).AsTask();
        var tick = timer.WaitForNextTickAsync(readCancellation.Token).AsTask();
        var nextPoll = DateTimeOffset.UtcNow + _pollInterval;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                await Task.WhenAny(read, tick).ConfigureAwait(false);
                // Check before processing stdout as well as on the timer: an old-account
                // response must never repaint the rail after credentials change.
                if (AuthFileRevision.Read(_authPath) != authRevision)
                {
                    _logger.Info("Codex credentials changed. Reconnecting the usage session.");
                    return;
                }

                if (tick.IsCompleted)
                {
                    await tick.ConfigureAwait(false);
                    var now = DateTimeOffset.UtcNow;
                    if ((!_initialized && now - sessionStarted > TimeSpan.FromSeconds(30)) ||
                        (_refreshPending != 0 && now - _requestStarted > TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException("Codex usage request timed out.");
                    }

                    // A credential manager may not write auth.json. Reload its state
                    // periodically by renewing only the overlay-owned app server.
                    if ((authRevision is "missing" or "unavailable") &&
                        now - sessionStarted >= TimeSpan.FromSeconds(60))
                    {
                        return;
                    }

                    if (now >= nextPoll)
                    {
                        await RefreshAsync(cancellationToken).ConfigureAwait(false);
                        nextPoll = now + _pollInterval;
                    }

                    tick = timer.WaitForNextTickAsync(readCancellation.Token).AsTask();
                }

                if (read.IsCompleted)
                {
                    var line = await read.ConfigureAwait(false);
                    if (line is null) break;
                    await HandleMessageAsync(line, cancellationToken).ConfigureAwait(false);
                    read = process.StandardOutput.ReadLineAsync(readCancellation.Token).AsTask();
                }
            }
        }
        finally
        {
            readCancellation.Cancel();
            try { await Task.WhenAll(read, tick).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Codex App Server stopped unexpectedly.");
        }
    }

    private async Task HandleMessageAsync(string line, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            _logger.Error("Ignored a non-JSON App Server message.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement) &&
                idElement.TryGetInt32(out var id) &&
                id == _initializeRequestId &&
                root.TryGetProperty("result", out _))
            {
                _initialized = true;
                await SendAsync(new { method = "initialized", @params = new { } }, cancellationToken)
                    .ConfigureAwait(false);
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (root.TryGetProperty("id", out var accountReadIdElement) &&
                accountReadIdElement.TryGetInt32(out var accountReadId) &&
                _accountReadRequestIds.TryRemove(accountReadId, out _))
            {
                if (root.TryGetProperty("error", out var accountError))
                {
                    LogError(accountError);
                    _lastSnapshot = null;
                    _accountValidated = false;
                    Interlocked.Exchange(ref _refreshPending, 0);
                    StatusChanged?.Invoke(this, "Couldn’t connect");
                    return;
                }

                if (AccountStateParser.TryParseReadResponse(root, out var accountState))
                {
                    if (accountState == CodexAccountState.SignedOut)
                    {
                        _lastSnapshot = null;
                        _accountValidated = false;
                        _rateReadRequestIds.Clear();
                        Interlocked.Exchange(ref _refreshPending, 0);
                        StatusChanged?.Invoke(this, "Signed out");
                        return;
                    }

                    var identity = root.GetProperty("result").GetProperty("account").GetRawText();
                    if (_accountIdentity != identity)
                    {
                        _lastSnapshot = null;
                        _accountIdentity = identity;
                        StatusChanged?.Invoke(this, "Connecting…");
                    }
                    _accountValidated = true;
                    var rateRequestId = NextRequestId();
                    _rateReadRequestIds.TryAdd(rateRequestId, 0);
                    await SendRequestAsync(
                        rateRequestId,
                        "account/rateLimits/read",
                        null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                Interlocked.Exchange(ref _refreshPending, 0);
                return;
            }

            if (AccountStateParser.TryParseUpdatedNotification(root, out var updatedState))
            {
                _lastSnapshot = null;
                _accountValidated = false;
                _accountReadRequestIds.Clear();
                _rateReadRequestIds.Clear();
                Interlocked.Exchange(ref _refreshPending, 0);
                StatusChanged?.Invoke(
                    this,
                    updatedState == CodexAccountState.SignedOut ? "Signed out" : "Connecting…");
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Notifications do not identify their account. Request a correlated
            // snapshot rather than applying a potentially stale notification.
            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "account/rateLimits/updated")
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Ignore late replies invalidated by logout/account updates.
            if (!root.TryGetProperty("id", out var rateId) || !rateId.TryGetInt32(out var rateRequest) ||
                !_rateReadRequestIds.TryRemove(rateRequest, out _))
            {
                return;
            }
            Interlocked.Exchange(ref _refreshPending, 0);
            if (!_accountValidated) return;

            if (root.TryGetProperty("error", out var error))
            {
                LogError(error);
                StatusChanged?.Invoke(this, "Couldn’t connect");
                return;
            }

            if (RateLimitParser.TryParse(root, out var parsed) && parsed is not null)
            {
                _lastSnapshot = _lastSnapshot?.MergePartial(parsed) ?? parsed;
                _logger.Info(
                    $"Usage updated: {Math.Round(_lastSnapshot.Primary.Primary.UsedPercent)}% " +
                    $"for {_lastSnapshot.Primary.Id}.");
                SnapshotChanged?.Invoke(this, _lastSnapshot);
                StatusChanged?.Invoke(this, "Live");

            }
        }
    }

    private Task SendRequestAsync(
        int requestId,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        return SendAsync(
            new
            {
                method,
                id = requestId,
                @params = parameters
            },
            cancellationToken);
    }

    private void LogError(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "Unknown App Server error"
            : "Unknown App Server error";
        _logger.Error(message);
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private int NextRequestId() => Interlocked.Increment(ref _nextRequestId);

    private static Process StartProcess(string codexCommand)
    {
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var escapedCommand = codexCommand.Replace("\"", "\"\"");
        var startInfo = new ProcessStartInfo
        {
            FileName = commandProcessor,
            Arguments = $"/d /s /c \"\"{escapedCommand}\" app-server --stdio\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        return Process.Start(startInfo) ??
               throw new InvalidOperationException("Failed to start Codex App Server.");
    }

    public static string? ResolveCodexCommand(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        }

        var configured = Environment.GetEnvironmentVariable("USAGE_OVERLAY_CODEX_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("QUOTARAIL_CODEX_PATH");
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("CODEX_USAGE_CODEX_PATH");
        }
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var desktopCommand = FindDesktopCodexCommand();
        if (desktopCommand is not null)
        {
            return desktopCommand;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            foreach (var filename in new[] { "codex.cmd", "codex.exe", "codex.bat" })
            {
                var candidate = Path.Combine(trimmed, filename);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? FindDesktopCodexCommand()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private void StopProcess()
    {
        _initialized = false;
        _accountReadRequestIds.Clear();
        _rateReadRequestIds.Clear();
        _accountValidated = false;
        _lastSnapshot = null;
        Interlocked.Exchange(ref _refreshPending, 0);
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The child already exited.
        }
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        StopProcess();
        _writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
