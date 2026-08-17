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
    private const string SingleInstanceMutexName = "Local\\CodexUsageOverlay";
    private const string ShowOverlayEventName = "Local\\CodexUsageOverlay.Show";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showOverlayEvent;
    private Thread? _showSignalThread;
    private AppLogger? _logger;
    private volatile bool _isExiting;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _ownsMutex = true;
        _showOverlayEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowOverlayEventName);
        StartShowSignalListener();

        _logger = new AppLogger();
        _logger.Info("Starting Codex Usage Overlay.");

        var options = OverlayOptions.Parse(e.Args);
        var window = new MainWindow(options, _logger);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        _showOverlayEvent?.Set();
        _showSignalThread?.Join(TimeSpan.FromSeconds(1));
        _showOverlayEvent?.Dispose();
        _logger?.Info("Stopping Codex Usage Overlay.");

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void StartShowSignalListener()
    {
        _showSignalThread = new Thread(() =>
        {
            while (!_isExiting)
            {
                _showOverlayEvent!.WaitOne();
                if (_isExiting)
                {
                    return;
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is CodexUsageOverlay.MainWindow overlayWindow)
                    {
                        overlayWindow.ShowFromExternalLaunch();
                    }
                });
            }
        })
        {
            IsBackground = true,
            Name = "CodexUsageOverlay.ShowSignal"
        };
        _showSignalThread.Start();
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowOverlayEventName);
                showEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
