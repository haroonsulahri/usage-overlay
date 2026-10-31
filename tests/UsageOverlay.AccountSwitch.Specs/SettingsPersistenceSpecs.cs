using CodexUsage.Core.Settings;
using UsageOverlay.Infrastructure;

internal static class SettingsPersistenceSpecs
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "usage-overlay-settings-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var canonical = Path.Combine(root, "profile", ".usage-overlay", "settings.json");
            var desktopLegacy = Path.Combine(root, "desktop-settings.json");
            var packagedLegacy = Path.Combine(root, "packaged-settings.json");
            File.WriteAllText(desktopLegacy, "{\"ClaudeEnabled\":true,\"ClaudeAccountUsageEnabled\":true,\"ClaudeAutomaticRenewalEnabled\":true,\"UsageReportingEnabled\":false,\"CustomXRatio\":0.97}");
            File.WriteAllText(packagedLegacy, "{\"ClaudeAutomaticRenewalEnabled\":false,\"UsageReportingEnabled\":true}");
            var logger = new AppLogger();
            var desktop = new OverlaySettingsStore(logger, canonical, [desktopLegacy]);
            var packaged = new OverlaySettingsStore(logger, canonical, [packagedLegacy]);
            var migrated = desktop.Load();
            Check(migrated.ClaudeAutomaticRenewalEnabled && migrated.ClaudeEnabled && migrated.CustomXRatio == 0.97,
                "Migration preserves renewal opt-in, enabled source and placement");
            Check(!migrated.UsageReportingEnabled, "Migration preserves reporting opt-out");
            Check(packaged.Load() == migrated, "Different launch contexts read the same canonical settings");
            packaged.Save(migrated with { ClaudeAutomaticRenewalEnabled = false });
            Check(!desktop.Load().ClaudeAutomaticRenewalEnabled, "Explicit opt-out survives relaunch and older opted-in settings");
            desktop.Save(migrated);
            Check(new OverlaySettingsStore(logger, canonical, [packagedLegacy]).Load().ClaudeAutomaticRenewalEnabled,
                "Saved renewal opt-in survives a fresh store with conflicting legacy settings");
            Check(File.Exists(desktopLegacy) && File.Exists(packagedLegacy), "Migration retains original settings files");
            var clean = new OverlaySettingsStore(logger, Path.Combine(root, "clean", "settings.json"), []);
            Check(!clean.Load().ClaudeAutomaticRenewalEnabled, "New installations do not silently opt in");
            var old = new OverlaySettingsStore(logger, Path.Combine(root, "old", "settings.json"), [packagedLegacy]);
            Check(!old.Load().ClaudeAutomaticRenewalEnabled, "Migration preserves explicit legacy opt-out");
            File.WriteAllText(canonical, "invalid json");
            Check(!desktop.Load().ClaudeAutomaticRenewalEnabled, "Invalid canonical settings do not resurrect old opt-ins");
            Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "No migration or save temporary files remain");
            Check(AppPaths.SettingsPath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".usage-overlay", "settings.json"),
                "Canonical settings path is outside virtualized AppData");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
