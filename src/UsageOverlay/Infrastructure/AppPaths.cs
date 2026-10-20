using System.IO;

namespace UsageOverlay.Infrastructure;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsageOverlay");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string ClaudeSnapshotPath => Path.Combine(DataDirectory, "claude-usage.json");

    public static string ClaudeIntegrationStatePath => Path.Combine(DataDirectory, "claude-statusline.json");

    public static string CursorKeyPath => Path.Combine(DataDirectory, "cursor-admin-key.bin");

    /// <summary>Writes through a unique temporary file so readers never see partial content.</summary>
    public static void WriteAtomically(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
