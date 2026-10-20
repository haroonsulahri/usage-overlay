using System.IO;
using CodexUsage.Core.Protocol;
using CodexUsage.Core.Providers;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>
/// Read-modify-write access to <c>claude-usage.json</c>, shared by the status-line relay
/// processes and the overlay's account-usage reader.
/// </summary>
internal static class ClaudeSnapshotStore
{
    private const string MutexName = "Local\\UsageOverlay.ClaudeSnapshot";

    public static ClaudeUsageSnapshot Update(
        ClaudeRateLimits limits,
        string? sessionId,
        IReadOnlyCollection<int> processIds,
        int? ownerProcessId)
    {
        using var mutex = new Mutex(false, MutexName);
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
}
