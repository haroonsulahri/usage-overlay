using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text.Json;
using CodexUsage.Core;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Providers;
using CodexUsage.Core.Settings;
using CodexUsage.Core.Reporting;
using UsageOverlay.Infrastructure;
using UsageOverlay.Services;

namespace UsageOverlay;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF window lifecycle disposes services and cancellation sources in Closed.")]
public partial class MainWindow : Window
{
    private const double CollapsedWidth = 34;
    private const double CollapsedHeight = 190;
    private const double MinimumExpandedHeight = 218;
    private const double MaximumExpandedHeight = 560;
    private const double ExpandedWidth = 254;
    private const double DetailsColumnWidth = 220;
    // The detail card is drawn 24px lower than its layout slot to line up with the rail.
    private const double DetailsVerticalShift = 24;
    private const double RightInset = 18;
    private const double BottomInset = 46;
    private const double RailUsableHeight = 134;
    private const double WindowPadding = 8;
    private const int HotKeyMessage = 0x0312;
    private const int EscapeHotKeyIdentifier = 0x0C0D;
    private const int VirtualKeyEscape = 0x1B;

    public static readonly DependencyProperty AnimatedRemainingPercentProperty =
        DependencyProperty.Register(
            nameof(AnimatedRemainingPercent),
            typeof(double),
            typeof(MainWindow),
            new PropertyMetadata(0d, AnimatedRemainingPercentChanged));

    private readonly OverlayOptions _options;
    private readonly AppLogger _logger;
    private readonly OverlaySettingsStore _settingsStore;
    private readonly StartupShortcutManager _startupShortcutManager;
    private readonly CodexUsageProvider _codex;
    private readonly ClaudeUsageProvider _claude;
    private readonly CursorUsageProvider _cursor;
    private readonly CursorCredentialStore _cursorCredentials = new();
    private readonly Dictionary<UsageProviderKind, ProviderUsageState> _states = new();
    private readonly ReleaseUpdateService _releaseUpdateService;
    private readonly HttpClient _reportingClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    private readonly UsageReporter _usageReporter;
    private readonly DispatcherTimer _reportingTimer = new() { Interval = TimeSpan.FromHours(1) };
    private CancellationTokenSource? _reportingCancellation;
    private Task _reportingTask = Task.CompletedTask;
    private readonly DispatcherTimer _windowTrackingTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly System.Drawing.Icon _applicationIcon;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private System.Windows.Forms.ToolStripMenuItem _trayVisibilityMenuItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _trayConnectionMenuItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _trayUpdateMenuItem = null!;
    private readonly UsageThresholdTracker _notificationTracker = new();
    private OverlaySettings _settings;
    private SettingsWindow? _settingsWindow;
    private UsageProviderKind _displayedProvider = UsageProviderKind.Codex;
    private UsageProviderKind? _hostProvider;
    private double? _animationTarget;
    private WindowBounds? _fixedPlacementBounds;
    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private bool _isExpanded;
    private bool _isPinned;
    private bool _isManuallyHidden;
    private bool _isClosing;
    private bool _isDragging;
    private bool _hasDragged;
    private bool _escapeHotKeyRegistered;
    private System.Windows.Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;
    private WindowBounds? _lastBounds;
    private Task? _updateCheckTask;
    private Uri? _pendingUpdateUri;

    public MainWindow(OverlayOptions options, AppLogger logger)
    {
        InitializeComponent();
        _options = options;
        _logger = logger;
        _settingsStore = new OverlaySettingsStore(logger);
        _settings = _settingsStore.Load();
        _usageReporter = new UsageReporter(_reportingClient,
            new Uri("https://haroone.com/api/usage-overlay.php"),
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_settingsStore.Path)!, "reporting.json"), AppVersion.Current);
        _reportingTimer.Tick += (_, _) => StartUsageReport();
        ThemeManager.Instance.Initialize(_settings.Theme);
        ThemeManager.Instance.ThemeApplied += ThemeManager_OnThemeApplied;

        _startupShortcutManager = new StartupShortcutManager(logger);
        _codex = new CodexUsageProvider(
            logger,
            _settings.CodexCliPath,
            TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds));
        _claude = new ClaudeUsageProvider(logger, ClaudeCodeIntegration.CreateDefault(), new ClaudeAccountUsageClient());
        _cursor = new CursorUsageProvider(logger, _cursorCredentials, _settings.CursorAccountEmail)
        {
            AccountMode = _settings.CursorAccountUsageEnabled
        };
        foreach (var provider in Providers)
        {
            _states[provider.Kind] = IsProviderEnabled(provider.Kind)
                ? ProviderUsageState.Connecting(provider.Kind, "Loading usage…")
                : provider.State;
            provider.StateChanged += Provider_OnStateChanged;
        }

        _displayedProvider = UsageProviderCatalog.All.FirstOrDefault(IsProviderEnabled, UsageProviderKind.Codex);
        _releaseUpdateService = new ReleaseUpdateService();

        _windowTrackingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _windowTrackingTimer.Tick += WindowTrackingTimer_OnTick;

        _collapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _collapseTimer.Tick += CollapseTimer_OnTick;

        _applicationIcon = LoadApplicationIcon();
        _notifyIcon = CreateNotifyIcon();
        UpdateTrayMenuTheme();

        if (_options.StartHidden)
        {
            _isManuallyHidden = true;
            UpdateVisibilityMenuLabels();
        }
    }

    private IEnumerable<UsageProvider> Providers => [_codex, _claude, _cursor];

    public double AnimatedRemainingPercent
    {
        get => (double)GetValue(AnimatedRemainingPercentProperty);
        set => SetValue(AnimatedRemainingPercentProperty, value);
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        NativeWindowStyle.ApplyNonActivatingToolWindow(this);
        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowProcedure);
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _windowTrackingTimer.Start();

        if (_isManuallyHidden)
        {
            Hide();
        }

        if (_options.DemoPercent is { } demoPercent)
        {
            foreach (var state in CreateDemoStates(demoPercent))
            {
                _states[state.Provider] = state;
            }

            _displayedProvider = _options.DemoProvider ?? UsageProviderKind.Codex;
            Render();
            UpdateTrayText();
            PositionForDemo();
            if (!_isManuallyHidden)
            {
                Show();
            }
            ApplyInitialExpansion();
            ApplyInitialSettings();
            return;
        }

        StartProviders();
        Render();
        UpdateUsageReporting();
        UpdateWindowPosition();
        ApplyInitialExpansion();
        ApplyInitialSettings();
    }

    private async void Window_OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _reportingTimer.Stop();
        _reportingCancellation?.Cancel();
        await _reportingTask;
        _reportingCancellation?.Dispose();
        _reportingClient.Dispose();
        ThemeManager.Instance.ThemeApplied -= ThemeManager_OnThemeApplied;
        _windowTrackingTimer.Stop();
        _collapseTimer.Stop();
        NativeHotKey.Unregister(_windowHandle, EscapeHotKeyIdentifier);
        _windowSource?.RemoveHook(WindowProcedure);
        _settingsWindow?.Close();
        _cancellation.Cancel();
        if (_updateCheckTask is not null)
        {
            try
            {
                await _updateCheckTask;
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown during a manual update check.
            }
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        foreach (var provider in Providers)
        {
            provider.StateChanged -= Provider_OnStateChanged;
            await provider.DisposeAsync();
        }

        _releaseUpdateService.Dispose();
        _cancellation.Dispose();
    }

    private void StartProviders()
    {
        if (_options.DemoPercent is not null)
        {
            return;
        }

        if (_settings.CodexEnabled)
        {
            _codex.Start();
        }

        // Reconnecting is idempotent and repairs the command if this executable moved.
        if (_settings.ClaudeEnabled)
        {
            _ = _claude.ApplyAsync(true, _settings.ClaudeStatusLineEnabled, _settings.ClaudeAccountUsageEnabled);
        }

        if (_settings.CursorEnabled)
        {
            _cursor.Start();
        }

        UpdateConnectionMenuLabels();
    }

    private async Task ToggleCodexConnectionAsync()
    {
        if (_options.DemoPercent is not null || !_settings.CodexEnabled)
        {
            return;
        }

        if (_codex.IsSuspended)
        {
            _logger.Info("Reconnecting to Codex CLI.");
            _codex.Resume();
        }
        else
        {
            await _codex.SuspendAsync();
            _logger.Info("Disconnected from Codex CLI. The CLI can now be updated safely.");
        }

        UpdateConnectionMenuLabels();
    }

    private bool IsProviderEnabled(UsageProviderKind provider) =>
        _options.DemoPercent is not null || provider switch
        {
            UsageProviderKind.Claude => _settings.ClaudeEnabled,
            UsageProviderKind.Cursor => _settings.CursorEnabled,
            _ => _settings.CodexEnabled
        };

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == HotKeyMessage && wordParameter.ToInt32() == EscapeHotKeyIdentifier)
        {
            CloseUsageDetails();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void WindowTrackingTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        if (_isDragging)
        {
            return;
        }

        UpdateWindowPosition();
    }

    private void UpdateWindowPosition()
    {
        if (_settingsWindow is { IsHiddenByUser: false })
        {
            if (!IsVisible)
            {
                Show();
            }

            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            return;
        }

        if (_isManuallyHidden)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        var now = DateTimeOffset.Now;
        if (_settings.PausedUntil is { } pausedUntil && pausedUntil <= now)
        {
            _settings = _settings with { PausedUntil = null };
            PersistSettings();
        }

        if (_settings.IsPaused(now))
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (_options.DemoPercent is not null)
        {
            PositionForDemo();
            return;
        }

        var hasActiveCodex = HostWindowLocator.TryGetActiveHost(IsProviderEnabled, _claude.IsClaudeCodeHost, out var host);
        var bounds = host.Bounds;
        if (hasActiveCodex)
        {
            if (host.IsLightTheme is { } isLightTheme)
            {
                ThemeManager.Instance.RefreshFromCodexTheme(isLightTheme);
            }

            // Follow the app in front; a manual choice in the card lasts until it changes.
            if (host.Provider != _hostProvider)
            {
                _hostProvider = host.Provider;
                ShowProvider(host.Provider);
            }
        }

        if (!hasActiveCodex && _settings.ShowOnlyWhenCodexActive)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!hasActiveCodex)
        {
            bounds = _lastBounds ?? GetCurrentPlacementBounds();
        }

        if (hasActiveCodex && _settings.HideInFullscreen && bounds.IsFullscreen)
        {
            _lastBounds = bounds;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (hasActiveCodex)
        {
            _lastBounds = bounds;
        }

        var placementBounds = bounds;
        if (_settings.FollowCodexAcrossMonitors)
        {
            _fixedPlacementBounds = null;
        }
        else
        {
            _fixedPlacementBounds ??= bounds;
            placementBounds = _fixedPlacementBounds.Value;
        }

        PositionWithin(placementBounds);
        if (!IsVisible)
        {
            Show();
        }
    }

    private void PositionWithin(WindowBounds bounds)
    {
        var baseLeft = _settings.Placement switch
        {
            HorizontalPlacement.LeftEdge => bounds.Left + RightInset,
            HorizontalPlacement.Custom =>
                bounds.Left + (bounds.Right - bounds.Left) * _settings.CustomXRatio -
                (Width - CollapsedWidth / 2),
            _ => bounds.Right - RightInset - Width
        };
        var maximumLeft = Math.Max(bounds.Left + WindowPadding, bounds.Right - Width - WindowPadding);
        Left = Math.Clamp(
            baseLeft + _settings.HorizontalOffset,
            bounds.Left + WindowPadding,
            maximumLeft);

        var baseTop = _settings.Placement == HorizontalPlacement.Custom
            ? OverlayPositionMath.CalculateCustomTop(
                bounds.Top + (bounds.Bottom - bounds.Top) * _settings.CustomYRatio,
                Height,
                CollapsedHeight,
                _settings.VerticalOffset)
            : bounds.Bottom - BottomInset - Height - _settings.VerticalOffset;
        var maximumTop = Math.Max(bounds.Top + WindowPadding, bounds.Bottom - Height - WindowPadding);
        Top = Math.Clamp(baseTop, bounds.Top + WindowPadding, maximumTop);
    }

    private void PositionForDemo()
    {
        var workArea = SystemParameters.WorkArea;
        PositionWithin(new WindowBounds(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom,
            false));
    }

    private void ApplyInitialExpansion()
    {
        if (!_options.StartExpanded)
        {
            return;
        }

        _isPinned = true;
        ExpandDetails();
        UpdateEscapeHotKey();
    }

    private void ApplyInitialSettings()
    {
        if (_options.StartSettings)
        {
            _ = Dispatcher.BeginInvoke(ShowSettings);
        }
    }

    private void OverlayRoot_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs eventArgs)
    {
        _collapseTimer.Stop();
        ExpandDetails();
    }

    private void OverlayRoot_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs eventArgs)
    {
        if (!_isPinned)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void CollapseTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        _collapseTimer.Stop();
        CollapseDetails();
    }

    private void RailHitTarget_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _collapseTimer.Stop();
        _isDragging = true;
        _hasDragged = false;
        _dragStartScreen = RailHitTarget.PointToScreen(eventArgs.GetPosition(RailHitTarget));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _ = RailHitTarget.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void RailHitTarget_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs eventArgs)
    {
        if (!_isDragging || eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentScreen = RailHitTarget.PointToScreen(eventArgs.GetPosition(RailHitTarget));
        var dpi = VisualTreeHelper.GetDpi(this);
        var horizontalChange = (currentScreen.X - _dragStartScreen.X) / dpi.DpiScaleX;
        var verticalChange = (currentScreen.Y - _dragStartScreen.Y) / dpi.DpiScaleY;

        if (!_hasDragged && Math.Sqrt(horizontalChange * horizontalChange + verticalChange * verticalChange) < 4)
        {
            return;
        }

        _hasDragged = true;
        var bounds = GetCurrentPlacementBounds();
        Left = Math.Clamp(
            _dragStartLeft + horizontalChange,
            bounds.Left + WindowPadding,
            Math.Max(bounds.Left + WindowPadding, bounds.Right - Width - WindowPadding));
        Top = Math.Clamp(
            _dragStartTop + verticalChange,
            bounds.Top + WindowPadding,
            Math.Max(bounds.Top + WindowPadding, bounds.Bottom - Height - WindowPadding));
        eventArgs.Handled = true;
    }

    private void RailHitTarget_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_isDragging || eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var wasDragged = _hasDragged;
        _isDragging = false;
        _hasDragged = false;
        RailHitTarget.ReleaseMouseCapture();

        if (wasDragged)
        {
            SaveCustomPosition();
        }
        else
        {
            TogglePinnedDetails();
        }

        eventArgs.Handled = true;
    }

    private void RailHitTarget_OnLostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs eventArgs)
    {
        if (!_isDragging)
        {
            return;
        }

        var wasDragged = _hasDragged;
        _isDragging = false;
        _hasDragged = false;
        if (wasDragged)
        {
            SaveCustomPosition();
        }
    }

    private void TogglePinnedDetails()
    {
        _isPinned = !_isPinned;

        if (_isPinned)
        {
            ExpandDetails();
        }
        else if (!OverlayRoot.IsMouseOver)
        {
            CollapseDetails();
        }
        UpdateEscapeHotKey();
    }

    private void CloseUsageDetailsButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        CloseUsageDetails();
        eventArgs.Handled = true;
    }

    private void CloseUsageDetails()
    {
        _isPinned = false;
        ResetToCollapsedState();
        UpdateEscapeHotKey();
    }

    private void UpdateEscapeHotKey()
    {
        if (_isPinned && _isExpanded)
        {
            if (!_escapeHotKeyRegistered)
            {
                _escapeHotKeyRegistered =
                    NativeHotKey.Register(_windowHandle, EscapeHotKeyIdentifier, VirtualKeyEscape);
                if (!_escapeHotKeyRegistered)
                {
                    _logger.Error("Could not register Escape to close pinned usage details.");
                }
            }

            return;
        }

        if (_escapeHotKeyRegistered)
        {
            NativeHotKey.Unregister(_windowHandle, EscapeHotKeyIdentifier);
            _escapeHotKeyRegistered = false;
        }
    }

    private WindowBounds GetCurrentPlacementBounds()
    {
        if (_lastBounds is { } bounds)
        {
            return bounds;
        }

        var workArea = SystemParameters.WorkArea;
        return new WindowBounds(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom,
            false);
    }

    private void SaveCustomPosition()
    {
        var bounds = GetCurrentPlacementBounds();
        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var railCenterX = Left + Width - CollapsedWidth / 2;
        var customAnchorY = OverlayPositionMath.CalculateCustomAnchorY(Top, Height, CollapsedHeight);

        _settings = _settings with
        {
            Placement = HorizontalPlacement.Custom,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            CustomXRatio = Math.Clamp((railCenterX - bounds.Left) / width, 0, 1),
            CustomYRatio = Math.Clamp((customAnchorY - bounds.Top) / height, 0, 1)
        };
        PersistSettings();
        _logger.Info(
            $"Saved custom overlay position at {_settings.CustomXRatio:P0} x, " +
            $"{_settings.CustomYRatio:P0} y.");
        UpdateWindowPosition();
    }

    private void ExpandDetails()
    {
        if (_isExpanded)
        {
            return;
        }

        _isExpanded = true;
        Width = ExpandedWidth;
        DetailsPanel.Visibility = Visibility.Visible;
        Height = MeasureExpandedHeight();
        RepositionAfterWidthChange();

        if (!SystemParameters.ClientAreaAnimation)
        {
            DetailsPanel.Opacity = 1;
            DetailsTranslate.X = 0;
            return;
        }

        DetailsPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        DetailsTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void CollapseDetails()
    {
        if (!_isExpanded || _isPinned)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            DetailsPanel.Opacity = 0;
            DetailsPanel.Visibility = Visibility.Collapsed;
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            _isExpanded = false;
            RepositionAfterWidthChange();
            return;
        }

        var opacity = new DoubleAnimation(DetailsPanel.Opacity, 0, TimeSpan.FromMilliseconds(120));
        opacity.Completed += (_, _) =>
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            _isExpanded = false;
            RepositionAfterWidthChange();
        };
        DetailsPanel.BeginAnimation(OpacityProperty, opacity);
        DetailsTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(120)));
    }

    private void RepositionAfterWidthChange()
    {
        if (_lastBounds is { } bounds)
        {
            PositionWithin(bounds);
        }
        else if (_options.DemoPercent is not null)
        {
            PositionForDemo();
        }
    }

    private void Provider_OnStateChanged(object? sender, ProviderUsageState state)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_isClosing)
            {
                return;
            }

            _states[state.Provider] = state;
            EvaluateNotifications(state);
            UpdateTrayText();
            Render();
        });
    }

    private ProviderUsageState CurrentState =>
        _states.TryGetValue(_displayedProvider, out var state) ? state : ProviderUsageState.Off(_displayedProvider);

    private void ShowProvider(UsageProviderKind provider)
    {
        if (provider == _displayedProvider)
        {
            return;
        }

        _displayedProvider = provider;
        _animationTarget = null;
        Render();
    }

    private void Render()
    {
        var state = CurrentState;
        HeaderText.Text = state.DisplayName;
        HeaderBadgeHost.Child = ProviderBadge.Create(state.Provider, 18);
        StatusText.Text = _options.DemoPercent is not null ? "Demo" : state.Connection switch
        {
            ProviderConnection.SignedOut => "Sign in",
            ProviderConnection.Unavailable => "Unavailable",
            ProviderConnection.Stale => "Stale",
            _ => state.StatusText
        };
        LiveDot.Fill = new SolidColorBrush((System.Windows.Media.Color)FindResource(state.Connection switch
        {
            ProviderConnection.Live => "OverlayNormalColor",
            ProviderConnection.Stale or ProviderConnection.SignedOut or ProviderConnection.Unavailable => "OverlayWarningColor",
            _ => "OverlayMutedColor"
        }));
        RailRemainingText.Visibility = _settings.ShowCompactPercentage
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(ViewUsageButton, $"View full {state.DisplayName} usage");
        ViewUsageButton.ToolTip = $"Open full {state.DisplayName} usage";

        if (state.HasUsage)
        {
            ApplyUsage(state);
        }
        else
        {
            ApplyConnectionState(state);
        }

        RenderOtherApps();
        var needsHelp = state.Connection is ProviderConnection.SignedOut or ProviderConnection.Unavailable
            or ProviderConnection.Disconnected or ProviderConnection.Off or ProviderConnection.Stale;
        RecoveryActionsPanel.Visibility = needsHelp ? Visibility.Visible : Visibility.Collapsed;
        RecoveryActionButton.Content = state.Provider == UsageProviderKind.Claude && state.Connection == ProviderConnection.SignedOut
            ? "Copy sign-in command" : "Open settings";
        AutomationProperties.SetName(RecoveryActionButton, $"{RecoveryActionButton.Content} for {state.DisplayName}");
        RecoveryActionButton.ToolTip = state.Provider == UsageProviderKind.Claude && state.Connection == ProviderConnection.SignedOut
            ? "Copy claude auth login. Run it in your terminal, then complete sign-in in your browser." : "Open usage source and connection settings";
        RetryUsageButton.Visibility = state.Connection is ProviderConnection.Off or ProviderConnection.Disconnected
            ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(RetryUsageButton, $"Retry {state.DisplayName} usage now");
        UpdateExpandedHeight();
    }

    private void ApplyConnectionState(ProviderUsageState state)
    {
        _animationTarget = null;
        BeginAnimation(AnimatedRemainingPercentProperty, null);
        AnimatedRemainingPercent = 0;
        UsageFillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        UsageFillBrush.Color = (System.Windows.Media.Color)FindResource("OverlayMutedColor");
        LeadingCap.Opacity = 0;
        RailRemainingText.Text = state.Connection is ProviderConnection.SignedOut or ProviderConnection.Unavailable ? "!"
            : state.Connection == ProviderConnection.Connecting ? "…" : "--";
        RailRemainingText.Foreground = new SolidColorBrush((System.Windows.Media.Color)FindResource(
            state.Connection is ProviderConnection.SignedOut or ProviderConnection.Unavailable ? "OverlayWarningColor" : "OverlayMutedColor"));
        BucketNameText.Text = state.DisplayName;
        BucketNameText.Visibility = Visibility.Collapsed;
        AdditionalLimitsPanel.Children.Clear();
        SummaryText.Visibility = Visibility.Collapsed;

        UsagePercentText.Text = state.Connection switch
        {
            ProviderConnection.Live or ProviderConnection.Stale when state.Summary is not null => state.Summary,
            ProviderConnection.Off => $"{state.DisplayName} is off",
            ProviderConnection.SignedOut => state.StatusText,
            ProviderConnection.Disconnected => "CLI disconnected",
            ProviderConnection.Unavailable => state.StatusText,
            _ when state.Provider == UsageProviderKind.Claude && state.StatusText == "Waiting" => "Waiting for Claude Code",
            _ => "Loading usage…"
        };
        ResetText.Text = state.Detail;
        ResetText.Visibility = state.Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var summary = $"{state.DisplayName}: {UsagePercentText.Text}. {state.Detail}".Trim();
        AutomationProperties.SetName(RailHitTarget, summary);
        RailHitTarget.ToolTip = summary;
    }

    private void ApplyUsage(ProviderUsageState state)
    {
        RailRemainingText.Foreground = (System.Windows.Media.Brush)FindResource("OverlayMutedBrush");
        RailHitTarget.ToolTip = state.Connection == ProviderConnection.Stale ? state.Detail : null;
        var lead = state.Windows[0];
        var usedPercent = UsageLevelResolver.Normalize(lead.Window.UsedPercent);
        var remainingPercent = UsageLevelResolver.RemainingFromUsed(usedPercent);
        var showingUsed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used;
        var primaryPercent = showingUsed ? usedPercent : remainingPercent;
        var primaryLabel = showingUsed ? "used" : "left";
        BucketNameText.Text = lead.Label;
        BucketNameText.Visibility = Visibility.Visible;
        ResetText.Text = FormatSecondaryLine(lead.Window);
        ResetText.Visibility = Visibility.Visible;

        var notes = new[] { state.Summary, state.Detail }
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToArray();
        SummaryText.Text = string.Join(Environment.NewLine, notes);
        SummaryText.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        AdditionalLimitsPanel.Children.Clear();
        foreach (var row in state.Windows.Skip(1))
        {
            AdditionalLimitsPanel.Children.Add(CreateLimitRow(row));
        }

        AnimateUsageValue(primaryPercent, usedPercent);
        var summary =
            $"{state.DisplayName} {lead.Label.ToLowerInvariant()}: {Math.Round(primaryPercent)} percent {primaryLabel}. {ResetText.Text}";
        AutomationProperties.SetName(RailHitTarget, summary);
    }

    private void RenderOtherApps()
    {
        OtherAppsPanel.Children.Clear();
        var others = UsageProviderCatalog.All
            .Where(provider => provider != _displayedProvider && IsProviderEnabled(provider))
            .ToArray();
        if (others.Length == 0 || !_settings.ShowOtherApps)
        {
            OtherAppsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        OtherAppsPanel.Visibility = Visibility.Visible;
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Other apps",
            Foreground = (System.Windows.Media.Brush)FindResource("OverlayMutedBrush"),
            FontSize = (double)FindResource("OverlayTextSmall"),
            Margin = new Thickness(0, 0, 0, 2)
        });

        foreach (var provider in others)
        {
            content.Children.Add(CreateOtherAppRow(
                _states.TryGetValue(provider, out var state) ? state : ProviderUsageState.Off(provider)));
        }

        OtherAppsPanel.Children.Add(new Border
        {
            BorderBrush = (System.Windows.Media.Brush)FindResource("OverlayBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 9, 0, 0),
            Child = content
        });
    }

    private System.Windows.Controls.Button CreateOtherAppRow(ProviderUsageState state)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var badge = ProviderBadge.Create(state.Provider, 14);
        badge.Margin = new Thickness(0, 0, 6, 0);
        name.Children.Add(badge);
        name.Children.Add(new TextBlock
        {
            Text = state.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("OverlayTextBrush"),
            FontSize = (double)FindResource("OverlayTextSmall"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Children.Add(name);

        string valueText;
        System.Windows.Media.Brush valueBrush;
        if (state.Lead is { } lead)
        {
            var used = UsageLevelResolver.Normalize(lead.Window.UsedPercent);
            var showingUsed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used;
            valueText = $"{Math.Round(showingUsed ? used : UsageLevelResolver.RemainingFromUsed(used))}% " +
                        (showingUsed ? "used" : "left");
            valueBrush = new SolidColorBrush(LevelColor(used));
        }
        else
        {
            valueText = state.Summary ?? state.StatusText;
            valueBrush = (System.Windows.Media.Brush)FindResource("OverlayMutedBrush");
        }

        var value = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = valueText,
            Foreground = valueBrush,
            FontSize = (double)FindResource("OverlayTextSmall"),
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        var button = new System.Windows.Controls.Button
        {
            Content = row,
            Padding = new Thickness(0, 3, 0, 3),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            ToolTip = $"Show {state.DisplayName} usage",
            Template = CreateFlatButtonTemplate()
        };
        AutomationProperties.SetName(button, $"Show {state.DisplayName} usage: {valueText}");
        var provider = state.Provider;
        button.Click += (_, eventArgs) =>
        {
            ShowProvider(provider);
            eventArgs.Handled = true;
        };
        return button;
    }

    private static ControlTemplate CreateFlatButtonTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(PaddingProperty));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(System.Windows.Controls.Button)) { VisualTree = border };
    }

    private double MeasureExpandedHeight()
    {
        DetailsPanel.Measure(new System.Windows.Size(DetailsColumnWidth, double.PositiveInfinity));
        return Math.Clamp(
            Math.Ceiling(DetailsPanel.DesiredSize.Height + DetailsVerticalShift),
            MinimumExpandedHeight,
            MaximumExpandedHeight);
    }

    private void UpdateExpandedHeight()
    {
        if (!_isExpanded)
        {
            return;
        }

        var height = MeasureExpandedHeight();
        if (Math.Abs(Height - height) > 0.5)
        {
            Height = height;
            RepositionAfterWidthChange();
        }
    }

    private System.Windows.Media.Color LevelColor(double usedPercent) =>
        (System.Windows.Media.Color)FindResource(UsageLevelResolver.FromPercentage(
            usedPercent,
            _settings.WarningThreshold,
            _settings.CriticalThreshold) switch
        {
            UsageLevel.Critical => "OverlayCriticalColor",
            UsageLevel.Warning => "OverlayWarningColor",
            _ => "OverlayNormalColor"
        });

    private Border CreateLimitRow(UsageWindowDisplay row)
    {
        var used = UsageLevelResolver.Normalize(row.Window.UsedPercent);
        var displayed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used
            ? used
            : UsageLevelResolver.RemainingFromUsed(used);
        var suffix = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used ? "used" : "left";

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = row.Label,
            Foreground = (System.Windows.Media.Brush)FindResource("OverlayMutedBrush"),
            FontSize = (double)FindResource("OverlayTextSmall"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var percentage = new TextBlock
        {
            Text = $"{Math.Round(displayed)}% {suffix}",
            Foreground = new SolidColorBrush(LevelColor(used)),
            FontSize = (double)FindResource("OverlayTextSmall"),
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(percentage, 1);
        header.Children.Add(percentage);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = ResetTimeFormatter.Format(row.Window.ResetsAt, DateTimeOffset.Now),
            Foreground = (System.Windows.Media.Brush)FindResource("OverlayMutedBrush"),
            FontSize = (double)FindResource("OverlayTextSmall"),
            Margin = new Thickness(0, 3, 0, 0)
        });

        return new Border
        {
            BorderBrush = (System.Windows.Media.Brush)FindResource("OverlayBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 9, 0, 0),
            Child = content
        };
    }

    private string FormatSecondaryLine(QuotaWindow window)
    {
        var used = UsageLevelResolver.Normalize(window.UsedPercent);
        var secondaryPercent = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used
            ? UsageLevelResolver.RemainingFromUsed(used)
            : used;
        var secondaryLabel = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used ? "left" : "used";
        return $"{Math.Round(secondaryPercent)}% {secondaryLabel}  ·  " +
               ResetTimeFormatter.Format(window.ResetsAt, DateTimeOffset.Now);
    }

    private void EvaluateNotifications(ProviderUsageState state)
    {
        if (!state.HasUsage)
        {
            return;
        }

        var alerts = _notificationTracker.Evaluate(
            state.Windows,
            _settings.WarningThreshold,
            _settings.CriticalThreshold);
        if (!_settings.NotificationsEnabled || _options.DemoPercent is not null)
        {
            return;
        }

        foreach (var alert in alerts)
        {
            var remaining = Math.Round(UsageLevelResolver.RemainingFromUsed(alert.Window.Window.UsedPercent));
            _notifyIcon.BalloonTipTitle = alert.Level == UsageLevel.Critical
                ? $"{state.DisplayName} usage is nearly exhausted"
                : $"{state.DisplayName} usage is running low";
            _notifyIcon.BalloonTipText =
                $"{alert.Window.Label}: {remaining}% left. " +
                ResetTimeFormatter.Format(alert.Window.Window.ResetsAt, DateTimeOffset.Now);
            _notifyIcon.ShowBalloonTip(6000);
            _logger.Info(
                $"Displayed a {alert.Level.ToString().ToLowerInvariant()} usage alert for {alert.Window.Key}.");
        }
    }

    private void UpdateTrayText()
    {
        var showingUsed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used;
        var parts = UsageProviderCatalog.All
            .Where(IsProviderEnabled)
            .Select(provider => _states.TryGetValue(provider, out var state) ? state : null)
            .Where(state => state?.Lead is not null)
            .Select(state =>
            {
                var used = UsageLevelResolver.Normalize(state!.Lead!.Window.UsedPercent);
                var value = showingUsed ? used : UsageLevelResolver.RemainingFromUsed(used);
                return $"{state.DisplayName} {Math.Round(value)}% {(showingUsed ? "used" : "left")}";
            })
            .ToArray();
        var text = parts.Length == 0 ? "Usage Overlay" : "Usage Overlay" + Environment.NewLine + string.Join(" · ", parts);
        // Windows limits notification-area tooltips to 127 characters.
        _notifyIcon.Text = text.Length <= 127 ? text : text[..126] + "…";
    }

    private void AnimateUsageValue(double displayPercentage, double usedPercentage)
    {
        var normalizedUsed = UsageLevelResolver.Normalize(usedPercentage);
        var normalizedDisplay = UsageLevelResolver.Normalize(displayPercentage);
        var color = LevelColor(normalizedUsed);

        // Providers republish unchanged readings; only move the rail for a new value.
        if (_animationTarget is { } target && Math.Abs(target - normalizedDisplay) < 0.05)
        {
            UsageFillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            UsageFillBrush.Color = color;
            UpdatePercentText(normalizedDisplay);
            return;
        }

        _animationTarget = normalizedDisplay;
        UpdatePercentText(AnimatedRemainingPercent);
        if (!_settings.AnimationsEnabled || !SystemParameters.ClientAreaAnimation ||
            Math.Abs(AnimatedRemainingPercent - normalizedDisplay) < 0.05)
        {
            UpdatePercentText(normalizedDisplay);
            BeginAnimation(AnimatedRemainingPercentProperty, null);
            AnimatedRemainingPercent = normalizedDisplay;
            UsageFillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            UsageFillBrush.Color = color;
            LeadingCap.Opacity = 0;
            return;
        }

        var animation = new DoubleAnimation(
            AnimatedRemainingPercent,
            normalizedDisplay,
            TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(AnimatedRemainingPercentProperty, null);
            AnimatedRemainingPercent = normalizedDisplay;
            LeadingCap.Opacity = 0;
        };

        LeadingCap.Opacity = 0.8;
        BeginAnimation(AnimatedRemainingPercentProperty, animation, HandoffBehavior.SnapshotAndReplace);

        UsageFillBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(color, TimeSpan.FromMilliseconds(250)));
    }

    private static void AnimatedRemainingPercentChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not MainWindow window || eventArgs.NewValue is not double percentage)
        {
            return;
        }

        var normalized = UsageLevelResolver.Normalize(percentage);
        window.UsageFillScale.ScaleY = normalized / 100d;
        window.UpdatePercentText(normalized);
        window.LeadingCapTranslate.Y = -(RailUsableHeight * normalized / 100d);
    }

    private void UpdatePercentText(double displayPercentage)
    {
        if (!CurrentState.HasUsage)
        {
            return;
        }

        var normalized = UsageLevelResolver.Normalize(displayPercentage);
        var suffix = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used ? "used" : "left";
        UsagePercentText.Text = $"{Math.Round(normalized)}% {suffix}";
        RailRemainingText.Text = $"{Math.Round(normalized)}%";
    }

    private static IEnumerable<ProviderUsageState> CreateDemoStates(double percentage)
    {
        var now = DateTimeOffset.Now;
        var codex = new RateLimitBucket(
            "codex",
            "All Codex models",
            new QuotaWindow(percentage, 300, now.AddHours(2).AddMinutes(18)),
            new QuotaWindow(Math.Max(0, percentage - 18), 10_080, now.AddDays(3).AddHours(2)),
            "pro");
        yield return CodexStatusMapper.FromSnapshot(
            new UsageSnapshot(codex, Array.Empty<RateLimitBucket>(), now));

        yield return ClaudeUsageSnapshot.Empty
            .Apply(
                new CodexUsage.Core.Protocol.ClaudeRateLimits(
                    new QuotaWindow(Math.Max(0, percentage - 30), 300, now.AddHours(3).AddMinutes(40)),
                    new QuotaWindow(Math.Max(0, percentage - 45), 10_080, now.AddDays(4))),
                "demo",
                Array.Empty<int>(),
                now.AddMinutes(-2))
            .ToState(now);

        yield return CodexUsage.Core.Protocol.CursorSpendParser.ToState(
            new CodexUsage.Core.Protocol.CursorMemberSpend(
                percentage * 40,
                percentage * 60,
                40,
                null,
                now.AddDays(-12)),
            now);
    }

    private System.Windows.Forms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(36, 36, 36),
            ForeColor = System.Drawing.Color.FromArgb(242, 242, 242),
            DropShadowEnabled = false,
            ShowImageMargin = false,
            Padding = new System.Windows.Forms.Padding(6),
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new OverlayMenuColorTable())
        };
        _trayVisibilityMenuItem = new System.Windows.Forms.ToolStripMenuItem("Hide usage rail");
        _trayVisibilityMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleManualVisibility);
        menu.Items.Add(_trayVisibilityMenuItem);
        menu.Items.Add("Open settings…", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Refresh usage", null, (_, _) => Dispatcher.Invoke(RefreshAllProviders));
        _trayConnectionMenuItem = new System.Windows.Forms.ToolStripMenuItem("Disconnect Codex CLI");
        _trayConnectionMenuItem.Click += async (_, _) =>
            await Dispatcher.InvokeAsync(ToggleCodexConnectionAsync).Task.Unwrap();
        menu.Items.Add(_trayConnectionMenuItem);
        _trayUpdateMenuItem = new System.Windows.Forms.ToolStripMenuItem("Check for updates");
        _trayUpdateMenuItem.Click += async (_, _) => await CheckForUpdatesAsync();
        menu.Items.Add(_trayUpdateMenuItem);
        menu.Items.Add("Open log file", null, (_, _) => OpenLog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = menu.Items.Add(
            "Quit Usage Overlay",
            null,
            (_, _) => Dispatcher.Invoke(System.Windows.Application.Current.Shutdown));
        exitItem.ForeColor = System.Drawing.Color.FromArgb(229, 90, 90);
        ApplyTrayMenuSpacing(menu.Items);

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Usage Overlay",
            Icon = _applicationIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_isManuallyHidden)
            {
                ToggleManualVisibility();
                return;
            }

            _isPinned = true;
            ExpandDetails();
            UpdateEscapeHotKey();
        });
        icon.BalloonTipClicked += (_, _) =>
        {
            if (_pendingUpdateUri is { } uri)
            {
                OpenUri(uri);
            }
        };
        return icon;
    }

    private void UpdateConnectionMenuLabels()
    {
        var label = _codex.IsSuspended ? "Reconnect Codex CLI" : "Disconnect Codex CLI";
        var visible = _settings.CodexEnabled && _options.DemoPercent is null;
        if (_trayConnectionMenuItem is not null)
        {
            _trayConnectionMenuItem.Text = label;
            _trayConnectionMenuItem.Visible = visible;
        }

        ConnectionMenuItem.Header = label;
        ConnectionMenuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAllProviders()
    {
        foreach (var provider in Providers.Where(provider => provider.IsRunning))
        {
            _ = provider.RefreshAsync();
        }
    }

    private Task CheckForUpdatesAsync()
    {
        if (_updateCheckTask is { IsCompleted: false })
        {
            return _updateCheckTask;
        }

        _updateCheckTask = CheckForUpdatesCoreAsync();
        return _updateCheckTask;
    }

    private async Task CheckForUpdatesCoreAsync()
    {
        if (_isClosing || !_trayUpdateMenuItem.Enabled)
        {
            return;
        }

        _trayUpdateMenuItem.Enabled = false;
        _trayUpdateMenuItem.Text = "Checking for updates…";
        _pendingUpdateUri = null;

        try
        {
            var result = await _releaseUpdateService.CheckAsync(_cancellation.Token);
            if (_isClosing)
            {
                return;
            }
            if (result.IsUpdateAvailable)
            {
                _pendingUpdateUri = result.ReleaseUri;
                _notifyIcon.BalloonTipTitle = $"Usage Overlay {result.LatestVersion} is available";
                _notifyIcon.BalloonTipText = "Select this notification to open the verified GitHub release.";
                _notifyIcon.ShowBalloonTip(8000);
                _logger.Info($"Update available: {result.LatestVersion}.");
            }
            else
            {
                _notifyIcon.BalloonTipTitle = "Usage Overlay is up to date";
                _notifyIcon.BalloonTipText = $"You are running version {result.CurrentVersion}.";
                _notifyIcon.ShowBalloonTip(5000);
                _logger.Info("Manual update check found no newer release.");
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            _notifyIcon.BalloonTipTitle = "Couldn’t check for updates";
            _notifyIcon.BalloonTipText = "Open the GitHub Releases page or try again later.";
            _notifyIcon.ShowBalloonTip(6000);
            _logger.Error($"Update check failed: {exception.Message}");
        }
        finally
        {
            if (!_isClosing)
            {
                _trayUpdateMenuItem.Text = "Check for updates";
                _trayUpdateMenuItem.Enabled = true;
            }
        }
    }

    private void OpenUri(Uri uri)
    {
        try
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Could not open {uri.Host}: {exception.Message}");
        }
    }

    private void UpdateTrayMenuTheme()
    {
        if (_notifyIcon.ContextMenuStrip is { } menu)
        {
            var isDark = ThemeManager.Instance.IsEffectiveDark;
            menu.BackColor = isDark ? System.Drawing.Color.FromArgb(36, 36, 36) : System.Drawing.Color.FromArgb(255, 255, 255);
            menu.ForeColor = isDark ? System.Drawing.Color.FromArgb(242, 242, 242) : System.Drawing.Color.FromArgb(24, 24, 27);
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new OverlayMenuColorTable(isDark));
            if (menu.Items.Count > 0 && menu.Items[^1] is System.Windows.Forms.ToolStripMenuItem exitItem)
            {
                exitItem.ForeColor = isDark
                    ? System.Drawing.Color.FromArgb(229, 90, 90)
                    : System.Drawing.Color.FromArgb(220, 38, 38);
            }
        }
    }

    private void ThemeManager_OnThemeApplied()
    {
        _logger.Info($"Theme applied: {(ThemeManager.Instance.IsEffectiveDark ? "Dark" : "Light")}.");
        UpdateTrayMenuTheme();
        _animationTarget = null;
        Render();
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            using var extracted = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (extracted is not null)
            {
                return (System.Drawing.Icon)extracted.Clone();
            }
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private static void ApplyTrayMenuSpacing(System.Windows.Forms.ToolStripItemCollection items)
    {
        foreach (System.Windows.Forms.ToolStripItem item in items)
        {
            if (item is System.Windows.Forms.ToolStripSeparator separator)
            {
                separator.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
                continue;
            }

            item.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            item.Margin = new System.Windows.Forms.Padding(0, 1, 0, 1);

            if (item is System.Windows.Forms.ToolStripMenuItem menuItem && menuItem.DropDownItems.Count > 0)
            {
                ApplyTrayMenuSpacing(menuItem.DropDownItems);
            }
        }
    }

    private void ToggleManualVisibility()
    {
        _isManuallyHidden = !_isManuallyHidden;
        UpdateVisibilityMenuLabels();

        if (_isManuallyHidden)
        {
            ResetToCollapsedState();
            Hide();
            _logger.Info("Overlay manually hidden. Usage updates remain active.");
            return;
        }

        if (_settings.PausedUntil is not null)
        {
            _settings = _settings with { PausedUntil = null };
            PersistSettings();
        }

        _logger.Info("Overlay manually enabled. Waiting for a supported app window.");
        UpdateWindowPosition();
    }

    public void ShowFromExternalLaunch()
    {
        _isManuallyHidden = false;
        _settings = _settings with { PausedUntil = null };
        PersistSettings();
        UpdateVisibilityMenuLabels();
        _logger.Info("External launch requested overlay visibility.");
        UpdateWindowPosition();
    }

    public void ShowSettingsFromExternalLaunch()
    {
        _logger.Info("External launch requested Settings.");
        ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            if (!IsVisible)
            {
                Show();
            }

            _settingsWindow.ShowFromLauncher();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        var window = new SettingsWindow(
            _settings,
            _startupShortcutManager.IsEnabled,
            _startupShortcutManager.IsSupported,
            GetProviderStatus(),
            ApplySettingsFromWindowAsync,
            OpenLog)
        {
            Owner = this,
            Topmost = true
        };
        window.Closed += (_, _) =>
        {
            _logger.Info("Settings window closed.");
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
                UpdateWindowPosition();
            }
        };
        _settingsWindow = window;
        window.ShowFromLauncher();
        _logger.Info("Settings window opened.");
    }

    private ProviderSettingsStatus GetProviderStatus()
    {
        string Describe(UsageProviderKind provider)
        {
            if (!IsProviderEnabled(provider))
            {
                return "Off.";
            }

            var state = _states.TryGetValue(provider, out var value) ? value : ProviderUsageState.Off(provider);
            return state.Detail.Length > 0 ? $"{state.StatusText}. {state.Detail}" : $"{state.StatusText}.";
        }

        var codexStatus = _codex.ResolvedCliPath is null
            ? $"Codex CLI wasn’t found. {Describe(UsageProviderKind.Codex)}"
            : $"Codex CLI is ready. {Describe(UsageProviderKind.Codex)}";
        // Setup failures matter more than the usage state; otherwise show the state alone.
        var claudeStatus = _settings.ClaudeEnabled && !_claude.IsRunning && _claude.IntegrationMessage.Length > 0
            ? _claude.IntegrationMessage
            : Describe(UsageProviderKind.Claude);
        return new ProviderSettingsStatus(
            codexStatus,
            claudeStatus,
            Describe(UsageProviderKind.Cursor),
            _cursorCredentials.HasKey,
            _claude.Integration.SettingsPath);
    }

    private async Task<string> ApplySettingsFromWindowAsync(SettingsSaveRequest request)
    {
        var previous = _settings;
        _settings = request.Settings.Normalize();
        ThemeManager.Instance.ApplyTheme(_settings.Theme);
        UpdateTrayMenuTheme();
        _ = _startupShortcutManager.SetEnabled(request.StartAutomatically);
        _fixedPlacementBounds = _settings.FollowCodexAcrossMonitors ? null : _lastBounds;
        PersistSettings();
        UpdateUsageReporting();

        var messages = new List<string>();
        var cursorKeyChanged = false;
        try
        {
            if (request.RemoveCursorKey)
            {
                _cursorCredentials.Delete();
                cursorKeyChanged = true;
            }
            else if (!string.IsNullOrWhiteSpace(request.NewCursorKey))
            {
                _cursorCredentials.Save(request.NewCursorKey);
                cursorKeyChanged = true;
            }
        }
        catch (Exception exception) when (
            exception is System.IO.IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException)
        {
            messages.Add("The Cursor key couldn’t be saved.");
            _logger.Error($"Could not update the Cursor key: {exception.Message}");
        }

        if (_options.DemoPercent is null)
        {
            if (_settings.CodexEnabled && !previous.CodexEnabled)
            {
                _codex.Resume();
            }
            else if (!_settings.CodexEnabled && previous.CodexEnabled)
            {
                await _codex.StopAsync();
            }

            if (_settings.ClaudeEnabled != previous.ClaudeEnabled ||
                _settings.ClaudeStatusLineEnabled != previous.ClaudeStatusLineEnabled ||
                _settings.ClaudeAccountUsageEnabled != previous.ClaudeAccountUsageEnabled)
            {
                messages.Add(await _claude.ApplyAsync(
                    _settings.ClaudeEnabled,
                    _settings.ClaudeStatusLineEnabled,
                    _settings.ClaudeAccountUsageEnabled));
            }

            var cursorChanged = cursorKeyChanged ||
                                _settings.CursorEnabled != previous.CursorEnabled ||
                                _settings.CursorAccountUsageEnabled != previous.CursorAccountUsageEnabled ||
                                !string.Equals(_settings.CursorAccountEmail, previous.CursorAccountEmail, StringComparison.OrdinalIgnoreCase);
            if (cursorChanged)
            {
                await _cursor.StopAsync();
                _cursor.Email = _settings.CursorAccountEmail;
                _cursor.AccountMode = _settings.CursorAccountUsageEnabled;
                if (_settings.CursorEnabled)
                {
                    _cursor.Start();
                }
            }
        }

        if (!IsProviderEnabled(_displayedProvider))
        {
            _displayedProvider = UsageProviderCatalog.All.FirstOrDefault(IsProviderEnabled, UsageProviderKind.Codex);
        }

        _hostProvider = null;
        _animationTarget = null;
        Render();
        UpdateTrayText();
        UpdateConnectionMenuLabels();
        UpdateWindowPosition();
        return string.Join(" ", messages.Where(message => message.Length > 0));
    }

    private void PersistSettings()
    {
        _settings = _settings.Normalize();
        _settingsStore.Save(_settings);
    }

    private void UpdateUsageReporting()
    {
        if (!_settings.UsageReportingEnabled || _options.DemoPercent is not null || _isClosing)
        {
            _reportingTimer.Stop();
            _reportingCancellation?.Cancel();
            return;
        }
        _reportingTimer.Start();
        StartUsageReport();
    }

    private void StartUsageReport()
    {
        if (!_settings.UsageReportingEnabled || _options.DemoPercent is not null || _isClosing || !_reportingTask.IsCompleted)
        {
            return;
        }
        _reportingCancellation?.Dispose();
        _reportingCancellation = new CancellationTokenSource();
        var token = _reportingCancellation.Token;
        _reportingTask = Task.Run(() => _usageReporter.ReportAsync(true, token));
    }

    private void UpdateVisibilityMenuLabels()
    {
        var label = _isManuallyHidden ? "Show usage rail" : "Hide usage rail";
        _trayVisibilityMenuItem.Text = label;
        RailVisibilityMenuItem.Header = label;
    }

    private void ResetToCollapsedState()
    {
        _collapseTimer.Stop();
        _isPinned = false;
        _isExpanded = false;
        DetailsPanel.BeginAnimation(OpacityProperty, null);
        DetailsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        DetailsPanel.Opacity = 0;
        DetailsTranslate.X = 8;
        DetailsPanel.Visibility = Visibility.Collapsed;
        Width = CollapsedWidth;
        Height = CollapsedHeight;
        RepositionAfterWidthChange();
        UpdateEscapeHotKey();
    }

    private void OpenLog()
    {
        try
        {
            _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _logger.Path,
                    UseShellExecute = true
                });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Could not open log: {exception.Message}");
        }
    }

    private void RefreshMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        RefreshAllProviders();
    }

    private async void ConnectionMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await ToggleCodexConnectionAsync();
    }

    private async void CheckForUpdatesMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await CheckForUpdatesAsync();
    }

    private void ViewUsageLink_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        OpenUri(UsageProviderCatalog.UsagePage(_displayedProvider));
        eventArgs.Handled = true;
    }

    private void RecoveryActionButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (CurrentState.Provider != UsageProviderKind.Claude || CurrentState.Connection != ProviderConnection.SignedOut)
        {
            ShowSettings();
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText("claude auth login");
            RecoveryActionButton.Content = "Command copied";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            RecoveryActionButton.Content = "Copy failed. Try again";
        }
    }

    private async void RetryUsageButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        var provider = Providers.FirstOrDefault(provider => provider.Kind == _displayedProvider && provider.IsRunning);
        if (provider is not null)
        {
            await provider.RefreshAsync();
        }
    }

    private void VisibilityMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ToggleManualVisibility();
    }

    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ShowSettings();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
