using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using CodexUsage.Core.Models;
using CodexUsage.Core.Protocol;
using CodexUsageOverlay.Infrastructure;

namespace CodexUsageOverlay.Services;

public sealed class AppServerClient : IAsyncDisposable
{
    private readonly AppLogger _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Process? _process;
    private UsageSnapshot? _lastSnapshot;
    private int _nextRequestId;
    private int _initializeRequestId;
    private bool _initialized;
    private CancellationTokenSource? _sessionCancellation;

    public AppServerClient(AppLogger logger)
    {
        _logger = logger;
    }

    public event EventHandler<UsageSnapshot>? SnapshotChanged;

    public event EventHandler<string>? StatusChanged;

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
                StatusChanged?.Invoke(this, "Reconnecting");
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
        return _initialized
            ? SendRequestAsync("account/rateLimits/read", null, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var codexCommand = FindCodexCommand();
        if (codexCommand is null)
        {
            StatusChanged?.Invoke(this, "Codex CLI not found");
            throw new FileNotFoundException(
                "Could not find codex.cmd or codex.exe on PATH. Set CODEX_USAGE_CODEX_PATH to override.");
        }

        _logger.Info($"Starting App Server through {codexCommand}.");
        StatusChanged?.Invoke(this, "Connecting");
        var process = StartProcess(codexCommand);
        _process = process;
        _initialized = false;
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
                        name = "codex_usage_overlay",
                        title = "Codex Usage Overlay",
                        version = "0.1.0"
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
                StatusChanged?.Invoke(this, "Live");
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? "Unknown App Server error"
                    : "Unknown App Server error";
                _logger.Error(message);
                StatusChanged?.Invoke(this, message);
                return;
            }

            if (RateLimitParser.TryParse(root, out var parsed) && parsed is not null)
            {
                _lastSnapshot = _lastSnapshot?.MergePartial(parsed) ?? parsed;
                _logger.Info(
                    $"Usage updated: {Math.Round(_lastSnapshot.Primary.Primary.UsedPercent)}% " +
                    $"for {_lastSnapshot.Primary.Id}.");
                SnapshotChanged?.Invoke(this, _lastSnapshot);

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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

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

    private Task SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        return SendAsync(
            new
            {
                method,
                id = NextRequestId(),
                @params = parameters
            },
            cancellationToken);
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

    private static string? FindCodexCommand()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_USAGE_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
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

    private void StopProcess()
    {
        _initialized = false;
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
