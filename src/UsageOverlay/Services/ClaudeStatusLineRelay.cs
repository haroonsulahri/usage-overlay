using System.IO;
using System.Text;
using CodexUsage.Core;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Providers;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>
/// Runs when Claude Code invokes <c>UsageOverlay.exe --claude-statusline</c>. It records the
/// plan limits from stdin and, unless it is wrapping another status line, prints a compact
/// usage line for Claude Code to display. It never fails Claude Code's status line.
/// </summary>
internal static class ClaudeStatusLineRelay
{
    public const string Argument = "--claude-statusline";
    public const string QuietArgument = "--quiet";
    private const string SnapshotMutexName = "Local\\UsageOverlay.ClaudeSnapshot";

    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            var input = ReadInput();
            if (!ClaudeStatusLineParser.TryParse(input, out var limits, out var sessionId))
            {
                return 0;
            }

            var ancestors = ProcessAncestry.GetAncestors(Environment.ProcessId);
            var snapshot = Record(
                limits,
                sessionId,
                ancestors.Select(ancestor => ancestor.ProcessId).ToArray(),
                FindClaudeCodeProcess(ancestors));
            if (!arguments.Contains(QuietArgument, StringComparer.OrdinalIgnoreCase))
            {
                WriteOutput(FormatStatusLine(snapshot, DateTimeOffset.UtcNow));
            }
        }
#pragma warning disable CA1031 // Any failure must stay silent: this runs on every status-line refresh.
        catch (Exception)
        {
            // A crash here would show a Windows error dialog each time Claude Code
            // refreshes its status line, so recording usage is strictly best effort.
        }
#pragma warning restore CA1031

        return 0;
    }

    internal static string FormatStatusLine(ClaudeUsageSnapshot snapshot, DateTimeOffset now)
    {
        var state = snapshot.ToState(now);
        if (!state.HasUsage)
        {
            return string.Empty;
        }

        return string.Join(
            " · ",
            state.Windows
                .OrderBy(window => window.Window.WindowDurationMinutes)
                .Select(window =>
                {
                    var name = window.Window.WindowDurationMinutes == ClaudeStatusLineParser.FiveHourMinutes ? "5h" : "7d";
                    return $"{name} {Math.Round(UsageLevelResolver.RemainingFromUsed(window.Window.UsedPercent))}% left";
                }));
    }

    // Native installs run claude.exe; npm installs run Claude Code inside node.exe.
    private static int? FindClaudeCodeProcess(IReadOnlyList<ProcessIdentity> ancestors) =>
        ancestors.FirstOrDefault(ancestor =>
                string.Equals(ancestor.ExecutableName, "claude.exe", StringComparison.OrdinalIgnoreCase)) is { ProcessId: > 0 } claude
            ? claude.ProcessId
            : ancestors.FirstOrDefault(ancestor =>
                string.Equals(ancestor.ExecutableName, "node.exe", StringComparison.OrdinalIgnoreCase)) is { ProcessId: > 0 } node
                ? node.ProcessId
                : null;

    private static ClaudeUsageSnapshot Record(
        ClaudeRateLimits limits,
        string? sessionId,
        IReadOnlyCollection<int> processIds,
        int? ownerProcessId)
    {
        using var mutex = new Mutex(false, SnapshotMutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            var path = AppPaths.ClaudeSnapshotPath;
            var current = File.Exists(path) &&
                          ClaudeUsageSnapshot.TryDeserialize(File.ReadAllText(path), out var existing)
                ? existing
                : ClaudeUsageSnapshot.Empty;
            var updated = current.Apply(limits, sessionId, processIds, DateTimeOffset.UtcNow, ownerProcessId);
            if (ownsMutex)
            {
                AppPaths.WriteAtomically(path, updated.Serialize());
            }

            return updated;
        }
        finally
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static string ReadInput()
    {
        using var stream = Console.OpenStandardInput();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var buffer = new char[ClaudeStatusLineParser.MaximumInputLength + 1];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        return length > ClaudeStatusLineParser.MaximumInputLength ? string.Empty : new string(buffer, 0, length);
    }

    private static void WriteOutput(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        using var stream = Console.OpenStandardOutput();
        var bytes = new UTF8Encoding(false).GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
    }
}
