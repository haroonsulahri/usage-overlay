using CodexUsage.Core.Security;

namespace CodexUsageOverlay.Infrastructure;

public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _path;

    public AppLogger()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = System.IO.Path.Combine(root, "CodexUsageOverlay");
        System.IO.Directory.CreateDirectory(directory);
        _path = System.IO.Path.Combine(directory, "overlay.log");
    }

    public string Path => _path;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var safeMessage = LogSanitizer.Sanitize(message);
        var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{Environment.NewLine}";

        lock (_gate)
        {
            System.IO.File.AppendAllText(_path, line);
        }
    }
}
