using System.IO;

namespace UsageOverlay.Services;

// Observe file metadata only. Credential contents remain owned by Codex.
internal static class AuthFileRevision
{
    internal static string ResolvePath()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        return Path.Combine(home, "auth.json");
    }

    internal static string Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? $"{file.CreationTimeUtc.Ticks}:{file.LastWriteTimeUtc.Ticks}:{file.Length}"
                : "missing";
        }
        catch (FileNotFoundException) { return "missing"; }
        catch (DirectoryNotFoundException) { return "missing"; }
        catch (IOException) { return "unavailable"; }
        catch (UnauthorizedAccessException) { return "unavailable"; }
    }
}
