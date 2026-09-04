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
    private Process? _process;
    private UsageSnapshot? _lastSnapshot;
    private int _nextRequestId;
    private int _initializeRequestId;
    private bool _initialized;
    private CancellationTokenSource? _sessionCancellation;

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
        var requestId = NextRequestId();
        _accountReadRequestIds.TryAdd(requestId, 0);
        return SendRequestAsync(
            requestId,
            "account/read",
            new { refreshToken = true },
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
        var process = StartProcess(codexCommand);
        _process = process;
        _initialized = false;
        _accountReadRequestIds.Clear();
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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

        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            await HandleMessageAsync(line, cancellationToken).ConfigureAwait(false);
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
                _ = PollAsync(_sessionCancellation!.Token);
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
                    StatusChanged?.Invoke(this, "Couldn’t connect");
                    return;
                }

                if (AccountStateParser.TryParseReadResponse(root, out var accountState))
                {
                    if (accountState == CodexAccountState.SignedOut)
                    {
                        _lastSnapshot = null;
                        StatusChanged?.Invoke(this, "Signed out");
                        return;
                    }

                    await SendRequestAsync(
                        NextRequestId(),
                        "account/rateLimits/read",
                        null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            if (AccountStateParser.TryParseUpdatedNotification(root, out var updatedState))
            {
                _lastSnapshot = null;
                StatusChanged?.Invoke(
                    this,
                    updatedState == CodexAccountState.SignedOut ? "Signed out" : "Connecting…");
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

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

                if (root.TryGetProperty("method", out var methodElement) &&
                    string.Equals(
                        methodElement.GetString(),
                        "account/rateLimits/updated",
                        StringComparison.Ordinal))
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal session shutdown.
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
        var sessionCancellation = Interlocked.Exchange(ref _sessionCancellation, null);
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();

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
