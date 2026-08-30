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
using CodexUsage.Core.Settings;
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
    private const double ExpandedWidth = 254;
    private const double RightInset = 18;
    private const double BottomInset = 46;
    private const double RailUsableHeight = 134;
    private const double WindowPadding = 8;
    private const int HotKeyMessage = 0x0312;
    private const int EscapeHotKeyIdentifier = 0x0C0D;
    private const int VirtualKeyEscape = 0x1B;
    private static readonly Uri UsagePageUri = new("https://chatgpt.com/codex/settings/usage");

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
    private readonly AppServerClient _appServerClient;
    private readonly ReleaseUpdateService _releaseUpdateService;
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
    private UsageSnapshot? _lastUsageSnapshot;
    private WindowBounds? _fixedPlacementBounds;
    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private string _appServerStatus = "Connecting";
    private bool _hasCurrentUsage;
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
    private CancellationTokenSource? _connectionCancellation;
    private Task? _appServerTask;
    private Task? _updateCheckTask;
    private Uri? _pendingUpdateUri;
    private double _expandedHeight = MinimumExpandedHeight;
    private bool _isConnectionSuspended;

    public MainWindow(OverlayOptions options, AppLogger logger)
    {
        InitializeComponent();
        _options = options;
        _logger = logger;
        _settingsStore = new OverlaySettingsStore(logger);
        _settings = _settingsStore.Load();
        ThemeManager.Instance.Initialize(_settings.Theme);
        ThemeManager.Instance.ThemeApplied += ThemeManager_OnThemeApplied;

        _startupShortcutManager = new StartupShortcutManager(logger);
        _appServerClient = new AppServerClient(
            logger,
            _settings.CodexCliPath,
            TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds));
        _releaseUpdateService = new ReleaseUpdateService();
        _appServerClient.SnapshotChanged += AppServerClient_OnSnapshotChanged;
        _appServerClient.StatusChanged += AppServerClient_OnStatusChanged;

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
            _hasCurrentUsage = true;
            ApplySnapshot(CreateDemoSnapshot(demoPercent));
            PositionForDemo();
            StatusText.Text = "Demo";
            if (!_isManuallyHidden)
            {
                Show();
            }
            ApplyInitialExpansion();
            ApplyInitialSettings();
            return;
        }

        StartAppServerConnection();
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
        ThemeManager.Instance.ThemeApplied -= ThemeManager_OnThemeApplied;
        _windowTrackingTimer.Stop();
        _collapseTimer.Stop();
        NativeHotKey.Unregister(_windowHandle, EscapeHotKeyIdentifier);
        _windowSource?.RemoveHook(WindowProcedure);
        _settingsWindow?.Close();
        _cancellation.Cancel();
        _connectionCancellation?.Cancel();
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
        if (_appServerTask is not null)
        {
            try
            {
                await _appServerTask;
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        }
        _connectionCancellation?.Dispose();
        await _appServerClient.DisposeAsync();
        _releaseUpdateService.Dispose();
        _cancellation.Dispose();
    }

    private void StartAppServerConnection()
    {
        if (_options.DemoPercent is not null || _appServerTask is { IsCompleted: false })
        {
            return;
        }

        _isConnectionSuspended = false;
        _connectionCancellation?.Dispose();
        _connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        _appServerTask = _appServerClient.RunAsync(_connectionCancellation.Token);
        UpdateConnectionMenuLabels();
    }

    private async Task SetConnectionSuspendedAsync(bool suspended)
    {
        if (_options.DemoPercent is not null || suspended == _isConnectionSuspended)
        {
            return;
        }

        if (suspended)
        {
            _isConnectionSuspended = true;
            _connectionCancellation?.Cancel();
            if (_appServerTask is not null)
            {
                try
                {
                    await _appServerTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected while releasing the CLI process.
                }
            }

            _appServerStatus = "Disconnected";
            _hasCurrentUsage = false;
            StatusText.Text = _appServerStatus;
            ApplyConnectionState(_appServerStatus);
            _logger.Info("Disconnected from Codex CLI. The CLI can now be updated safely.");
            UpdateConnectionMenuLabels();
            return;
        }

        _logger.Info("Reconnecting to Codex CLI.");
        StartAppServerConnection();
    }

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

        var hasActiveCodex = CodexWindowLocator.TryGetActiveBounds(out var bounds, out var codexIsLightTheme);
        if (codexIsLightTheme is { } isLightTheme)
        {
            ThemeManager.Instance.RefreshFromCodexTheme(isLightTheme);
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
            ? bounds.Top + (bounds.Bottom - bounds.Top) * _settings.CustomYRatio - Height / 2 -
              _settings.VerticalOffset
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
        var windowCenterY = Top + Height / 2;

        _settings = _settings with
        {
            Placement = HorizontalPlacement.Custom,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            CustomXRatio = Math.Clamp((railCenterX - bounds.Left) / width, 0, 1),
            CustomYRatio = Math.Clamp((windowCenterY - bounds.Top) / height, 0, 1)
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
        Height = _expandedHeight;
        RepositionAfterWidthChange();
        DetailsPanel.Visibility = Visibility.Visible;

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

    private void AppServerClient_OnSnapshotChanged(object? sender, UsageSnapshot snapshot)
    {
        _lastUsageSnapshot = snapshot;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _hasCurrentUsage = true;
            ApplySnapshot(snapshot);
        });
    }

    private void AppServerClient_OnStatusChanged(object? sender, string status)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _appServerStatus = status;
            StatusText.Text = status;
            LiveDot.Fill = status == "Live"
                ? new SolidColorBrush((System.Windows.Media.Color)FindResource("OverlayNormalColor"))
                : new SolidColorBrush((System.Windows.Media.Color)FindResource("OverlayMutedColor"));

            if (status == "Connecting…" || status == "Trying again…" ||
                status == "Couldn’t connect" || status == "CLI not found")
            {
                _hasCurrentUsage = false;
            }

            if (!_hasCurrentUsage)
            {
                ApplyConnectionState(status);
            }
        });
    }

    private void ApplyConnectionState(string status)
    {
        BeginAnimation(AnimatedRemainingPercentProperty, null);
        AnimatedRemainingPercent = 0;
        UsageFillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        UsageFillBrush.Color = (System.Windows.Media.Color)FindResource("OverlayMutedColor");
        LeadingCap.Opacity = 0;
        RailRemainingText.Text = "--";
        BucketNameText.Text = "Codex usage";
        AdditionalLimitsPanel.Children.Clear();
        _expandedHeight = MinimumExpandedHeight;
        if (_isExpanded)
        {
            Height = MinimumExpandedHeight;
            RepositionAfterWidthChange();
        }

        switch (status)
        {
            case "CLI not found":
                UsagePercentText.Text = "Usage unavailable";
                ResetText.Text = "CLI not found. Check Settings.";
                break;
            case "Couldn’t connect":
            case "Trying again…":
                UsagePercentText.Text = "Usage unavailable";
                ResetText.Text = "Couldn’t load limits. Retrying…";
                break;
            case "Disconnected":
                UsagePercentText.Text = "CLI disconnected";
                ResetText.Text = "Reconnect when the CLI update is finished.";
                break;
            default:
                UsagePercentText.Text = "Loading usage…";
                ResetText.Text = "Connecting to Codex…";
                break;
        }

        AutomationProperties.SetName(
            RailHitTarget,
            $"Codex usage unavailable. {ResetText.Text}");
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        var primary = snapshot.Primary;
        var usedPercent = UsageLevelResolver.Normalize(primary.Primary.UsedPercent);
        var remainingPercent = UsageLevelResolver.RemainingFromUsed(usedPercent);
        var showingUsed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used;
        var primaryPercent = showingUsed ? usedPercent : remainingPercent;
        var secondaryPercent = showingUsed ? remainingPercent : usedPercent;
        var primaryLabel = showingUsed ? "used" : "left";
        var secondaryLabel = showingUsed ? "left" : "used";
        BucketNameText.Text = primary.DisplayName;
        ResetText.Text =
            $"{Math.Round(secondaryPercent)}% {secondaryLabel}  ·  " +
            ResetTimeFormatter.Format(primary.Primary.ResetsAt, DateTimeOffset.Now);
        RailRemainingText.Visibility = _settings.ShowCompactPercentage
            ? Visibility.Visible
            : Visibility.Collapsed;
        RenderLimitRows(snapshot);
        EvaluateNotifications(snapshot);
        AnimateUsageValue(primaryPercent, usedPercent);
        AutomationProperties.SetName(
            RailHitTarget,
            $"Codex limit: {Math.Round(primaryPercent)} percent {primaryLabel}. {ResetText.Text}");
    }

    private void RenderLimitRows(UsageSnapshot snapshot)
    {
        var rows = UsageWindowDisplayBuilder.Build(snapshot);
        AdditionalLimitsPanel.Children.Clear();

        if (rows.Count == 0)
        {
            _expandedHeight = MinimumExpandedHeight;
            return;
        }

        BucketNameText.Text = rows[0].Label;
        ResetText.Text = FormatSecondaryLine(rows[0].Window);

        foreach (var row in rows.Skip(1))
        {
            AdditionalLimitsPanel.Children.Add(CreateLimitRow(row));
        }

        _expandedHeight = Math.Clamp(
            MinimumExpandedHeight + Math.Max(0, rows.Count - 1) * 52,
            MinimumExpandedHeight,
            346);
        if (_isExpanded)
        {
            Height = _expandedHeight;
            RepositionAfterWidthChange();
        }
    }

    private Border CreateLimitRow(UsageWindowDisplay row)
    {
        var used = UsageLevelResolver.Normalize(row.Window.UsedPercent);
        var displayed = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used
            ? used
            : UsageLevelResolver.RemainingFromUsed(used);
        var suffix = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used ? "used" : "left";
        var colour = (System.Windows.Media.Color)FindResource(UsageLevelResolver.FromPercentage(
            used,
            _settings.WarningThreshold,
            _settings.CriticalThreshold) switch
        {
            UsageLevel.Critical => "OverlayCriticalColor",
            UsageLevel.Warning => "OverlayWarningColor",
            _ => "OverlayNormalColor"
        });

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
            Foreground = new SolidColorBrush(colour),
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

    private void EvaluateNotifications(UsageSnapshot snapshot)
    {
        var alerts = _notificationTracker.Evaluate(
            snapshot,
            _settings.WarningThreshold,
            _settings.CriticalThreshold);
        if (!_settings.NotificationsEnabled)
        {
            return;
        }

        foreach (var alert in alerts)
        {
            var remaining = Math.Round(UsageLevelResolver.RemainingFromUsed(alert.Window.Window.UsedPercent));
            _notifyIcon.BalloonTipTitle = alert.Level == UsageLevel.Critical
                ? "Codex usage is nearly exhausted"
                : "Codex usage is running low";
            _notifyIcon.BalloonTipText =
                $"{alert.Window.Label}: {remaining}% left. " +
                ResetTimeFormatter.Format(alert.Window.Window.ResetsAt, DateTimeOffset.Now);
            _notifyIcon.ShowBalloonTip(6000);
            _logger.Info(
                $"Displayed a {alert.Level.ToString().ToLowerInvariant()} usage alert for {alert.Window.Key}.");
        }
    }

    private void AnimateUsageValue(double displayPercentage, double usedPercentage)
    {
        var normalizedUsed = UsageLevelResolver.Normalize(usedPercentage);
        var normalizedDisplay = UsageLevelResolver.Normalize(displayPercentage);
        var color = (System.Windows.Media.Color)FindResource(UsageLevelResolver.FromPercentage(
            normalizedUsed,
            _settings.WarningThreshold,
            _settings.CriticalThreshold) switch
        {
            UsageLevel.Critical => "OverlayCriticalColor",
            UsageLevel.Warning => "OverlayWarningColor",
            _ => "OverlayNormalColor"
        });

        if (!_settings.AnimationsEnabled || !SystemParameters.ClientAreaAnimation)
        {
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
        var suffix = window._settings.PrimaryDisplay == PrimaryUsageDisplay.Used ? "used" : "left";
        window.UsagePercentText.Text = $"{Math.Round(normalized)}% {suffix}";
        window.RailRemainingText.Text = $"{Math.Round(normalized)}%";
        window.LeadingCapTranslate.Y = -(RailUsableHeight * normalized / 100d);
    }

    private static UsageSnapshot CreateDemoSnapshot(double percentage)
    {
        var primary = new RateLimitBucket(
            "codex",
            "All Codex models",
            new QuotaWindow(percentage, 300, DateTimeOffset.Now.AddHours(2).AddMinutes(18)),
            new QuotaWindow(Math.Max(0, percentage - 18), 10_080, DateTimeOffset.Now.AddDays(3).AddHours(2)),
            "pro");
        return new UsageSnapshot(primary, Array.Empty<RateLimitBucket>(), DateTimeOffset.Now);
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
        menu.Items.Add("Refresh usage", null, (_, _) => _ = _appServerClient.RefreshAsync());
        _trayConnectionMenuItem = new System.Windows.Forms.ToolStripMenuItem("Disconnect Codex CLI");
        _trayConnectionMenuItem.Click += async (_, _) =>
            await Dispatcher.InvokeAsync(() => SetConnectionSuspendedAsync(!_isConnectionSuspended)).Task.Unwrap();
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
        var label = _isConnectionSuspended ? "Reconnect Codex CLI" : "Disconnect Codex CLI";
        if (_trayConnectionMenuItem is not null)
        {
            _trayConnectionMenuItem.Text = label;
        }
        ConnectionMenuItem.Header = label;
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
        if (_hasCurrentUsage && _lastUsageSnapshot is not null)
        {
            ApplySnapshot(_lastUsageSnapshot);
        }
        else
        {
            ApplyConnectionState(_appServerStatus);
        }
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

        _logger.Info("Overlay manually enabled. Waiting for the active Codex window.");
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

        var resolvedCliPath = AppServerClient.ResolveCodexCommand(_settings.CodexCliPath);
        var cliStatus = resolvedCliPath is null
            ? $"Codex CLI wasn’t found. Connection: {_appServerStatus}."
            : $"Codex CLI is ready. Connection: {_appServerStatus}.";
        if (!IsVisible)
        {
            Show();
        }

        var window = new SettingsWindow(
            _settings,
            _startupShortcutManager.IsEnabled,
            _startupShortcutManager.IsSupported,
            cliStatus,
            ApplySettingsFromWindow,
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

    private void ApplySettingsFromWindow(OverlaySettings settings, bool startAutomatically)
    {
        _settings = settings.Normalize();
        ThemeManager.Instance.ApplyTheme(_settings.Theme);
        UpdateTrayMenuTheme();
        _ = _startupShortcutManager.SetEnabled(startAutomatically);
        _fixedPlacementBounds = _settings.FollowCodexAcrossMonitors ? null : _lastBounds;
        PersistSettings();
        RailRemainingText.Visibility = _settings.ShowCompactPercentage
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_hasCurrentUsage && _lastUsageSnapshot is not null)
        {
            ApplySnapshot(_lastUsageSnapshot);
        }
        else
        {
            ApplyConnectionState(_appServerStatus);
        }

        UpdateWindowPosition();
    }

    private void PersistSettings()
    {
        _settings = _settings.Normalize();
        _settingsStore.Save(_settings);
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

    private async void RefreshMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await _appServerClient.RefreshAsync();
    }

    private async void ConnectionMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await SetConnectionSuspendedAsync(!_isConnectionSuspended);
    }

    private async void CheckForUpdatesMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await CheckForUpdatesAsync();
    }

    private void ViewUsageLink_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        OpenUri(UsagePageUri);
        eventArgs.Handled = true;
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
