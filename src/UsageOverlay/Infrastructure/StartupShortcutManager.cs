using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;

namespace UsageOverlay.Infrastructure;

public sealed class StartupShortcutManager
{
    private const string ShortcutName = "Usage Overlay.lnk";
    private static readonly string[] LegacyShortcutNames = ["QuotaRail for Codex.lnk", "Codex Usage Overlay.lnk"];

    private readonly AppLogger _logger;
    private readonly string _executablePath;
    private readonly string _shortcutPath;
    private readonly string[] _legacyShortcutPaths;

    public StartupShortcutManager(AppLogger logger)
    {
        _logger = logger;
        _executablePath = Environment.ProcessPath ?? string.Empty;
        var startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        _shortcutPath = System.IO.Path.Combine(startupDirectory, ShortcutName);
        _legacyShortcutPaths = LegacyShortcutNames
            .Select(name => System.IO.Path.Combine(startupDirectory, name))
            .ToArray();
    }

    public bool IsSupported =>
        !string.IsNullOrWhiteSpace(_executablePath) &&
        string.Equals(
            System.IO.Path.GetFileName(_executablePath),
            "UsageOverlay.exe",
            StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled => File.Exists(_shortcutPath) || _legacyShortcutPaths.Any(File.Exists);

    public bool SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            _logger.Error("Automatic startup is available only from the packaged executable.");
            return false;
        }

        try
        {
            if (enabled)
            {
                CreateShortcut();
                RemoveLegacyShortcuts();
            }
            else
            {
                if (File.Exists(_shortcutPath))
                {
                    File.Delete(_shortcutPath);
                }

                RemoveLegacyShortcuts();
            }

            _logger.Info($"Automatic startup {(enabled ? "enabled" : "disabled")}.");
            return IsEnabled == enabled;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            _logger.Error($"Could not update automatic startup: {exception.Message}");
            return false;
        }
    }

    private void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
                        throw new InvalidOperationException("Windows Script Host is unavailable.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType) ??
                    throw new InvalidOperationException("Could not start Windows Script Host.");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { _shortcutPath },
                CultureInfo.InvariantCulture);

            if (shortcut is null)
            {
                throw new InvalidOperationException("Could not create the startup shortcut.");
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { _executablePath },
                CultureInfo.InvariantCulture);
            shortcutType.InvokeMember(
                "WorkingDirectory",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { System.IO.Path.GetDirectoryName(_executablePath) ?? string.Empty },
                CultureInfo.InvariantCulture);
            shortcutType.InvokeMember(
                "Description",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { "Start Usage Overlay with Windows" },
                CultureInfo.InvariantCulture);
            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shortcut,
                null,
                CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                _ = Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private void RemoveLegacyShortcuts()
    {
        foreach (var legacyShortcutPath in _legacyShortcutPaths)
        {
            if (File.Exists(legacyShortcutPath))
            {
                File.Delete(legacyShortcutPath);
            }
        }
    }
}
