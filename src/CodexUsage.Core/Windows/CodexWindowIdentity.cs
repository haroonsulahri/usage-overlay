namespace CodexUsage.Core.Windows;

public static class CodexWindowIdentity
{
    public static bool IsMainWindow(
        string executablePath,
        string processName,
        string windowTitle)
    {
        if (!string.Equals(windowTitle, "ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (executablePath.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
            executablePath.EndsWith("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase) &&
               executablePath.Contains("Codex", StringComparison.OrdinalIgnoreCase);
    }
}
