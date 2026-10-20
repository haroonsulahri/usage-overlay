using CodexUsage.Core.Providers;

namespace CodexUsage.Core.Windows;

/// <summary>Recognises the desktop apps whose usage the rail can show.</summary>
public static class HostApplicationIdentity
{
    public static UsageProviderKind? Classify(
        string executablePath,
        string processName,
        string windowTitle)
    {
        if (CodexWindowIdentity.IsMainWindow(executablePath, processName, windowTitle))
        {
            return UsageProviderKind.Codex;
        }

        if (IsClaudeApp(executablePath, processName))
        {
            return UsageProviderKind.Claude;
        }

        if (IsCursorApp(executablePath, processName))
        {
            return UsageProviderKind.Cursor;
        }

        return null;
    }

    // Covers the Claude desktop app (Squirrel and MSIX installs) and a classic console
    // window owned by the native Claude Code executable.
    private static bool IsClaudeApp(string executablePath, string processName) =>
        string.Equals(processName, "claude", StringComparison.OrdinalIgnoreCase) &&
        executablePath.EndsWith("claude.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsCursorApp(string executablePath, string processName) =>
        string.Equals(processName, "Cursor", StringComparison.OrdinalIgnoreCase) &&
        executablePath.EndsWith("Cursor.exe", StringComparison.OrdinalIgnoreCase);
}
