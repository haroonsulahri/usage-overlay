using System.Threading;
using System.Windows;
using System.Diagnostics.CodeAnalysis;
using CodexUsageOverlay.Infrastructure;

namespace CodexUsageOverlay;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF application lifecycle releases the mutex in OnExit.")]
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private AppLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "Local\\CodexUsageOverlay", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _logger = new AppLogger();
        _logger.Info("Starting Codex Usage Overlay.");

        var options = OverlayOptions.Parse(e.Args);
        var window = new MainWindow(options, _logger);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Stopping Codex Usage Overlay.");
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
