using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics.CodeAnalysis;
using CodexUsage.Core;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Models;
using CodexUsage.Core.Settings;
using CodexUsageOverlay.Infrastructure;
using CodexUsageOverlay.Services;

namespace CodexUsageOverlay;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF window lifecycle disposes services and cancellation sources in Closed.")]
public partial class MainWindow : Window
{
    private const double CollapsedWidth = 34;
    private const double ExpandedWidth = 254;
    private const double RightInset = 18;
    private const double SidebarClearance = 330;
    private const double BottomInset = 46;
    private const double RailUsableHeight = 134;
    private const double PositionNudge = 20;
    private const double WindowPadding = 8;

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
    private readonly DispatcherTimer _windowTrackingTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private System.Windows.Forms.ToolStripMenuItem _trayVisibilityMenuItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _pauseMenuItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _avoidSidebarPlacementItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _rightEdgePlacementItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _leftEdgePlacementItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _hideFullscreenMenuItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _automaticStartupMenuItem = null!;
    private OverlaySettings _settings;
    private bool _isExpanded;
    private bool _isPinned;
    private bool _isManuallyHidden;
    private bool _isClosing;
    private WindowBounds? _lastBounds;

    public MainWindow(OverlayOptions options, AppLogger logger)
    {
        InitializeComponent();
        _options = options;
        _logger = logger;
        _settingsStore = new OverlaySettingsStore(logger);
        _settings = _settingsStore.Load();
        _startupShortcutManager = new StartupShortcutManager(logger);
        _appServerClient = new AppServerClient(logger);
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

        _notifyIcon = CreateNotifyIcon();

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
            ApplySnapshot(CreateDemoSnapshot(demoPercent));
            PositionForDemo();
            StatusText.Text = "Demo";
            if (!_isManuallyHidden)
            {
                Show();
            }
            ApplyInitialExpansion();
            return;
        }

        _ = _appServerClient.RunAsync(_cancellation.Token);
        UpdateWindowPosition();
        ApplyInitialExpansion();
    }

    private async void Window_OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _windowTrackingTimer.Stop();
        _collapseTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _cancellation.Cancel();
        await _appServerClient.DisposeAsync();
        _cancellation.Dispose();
    }

    private void WindowTrackingTimer_OnTick(object? sender, EventArgs eventArgs)
    {
        UpdateWindowPosition();
    }

    private void UpdateWindowPosition()
    {
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

        if (!CodexWindowLocator.TryGetActiveBounds(out var bounds))
        {
            _lastBounds = null;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (_settings.HideInFullscreen && bounds.IsFullscreen)
        {
            _lastBounds = bounds;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        _lastBounds = bounds;
        PositionWithin(bounds);
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
            HorizontalPlacement.RightEdge => bounds.Right - RightInset - Width,
            _ => bounds.Right - SidebarClearance - Width
        };
        var maximumLeft = Math.Max(bounds.Left + WindowPadding, bounds.Right - Width - WindowPadding);
        Left = Math.Clamp(
            baseLeft + _settings.HorizontalOffset,
            bounds.Left + WindowPadding,
            maximumLeft);

        var baseTop = bounds.Bottom - BottomInset - Height - _settings.VerticalOffset;
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
        _isPinned = !_isPinned;

        if (_isPinned)
        {
            ExpandDetails();
        }
        else if (!OverlayRoot.IsMouseOver)
        {
            CollapseDetails();
        }

        eventArgs.Handled = true;
    }

    private void ExpandDetails()
    {
        if (_isExpanded)
        {
            return;
        }

        _isExpanded = true;
        Width = ExpandedWidth;
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
            _isExpanded = false;
            RepositionAfterWidthChange();
            return;
        }

        var opacity = new DoubleAnimation(DetailsPanel.Opacity, 0, TimeSpan.FromMilliseconds(120));
        opacity.Completed += (_, _) =>
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
            Width = CollapsedWidth;
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
        _ = Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private void AppServerClient_OnStatusChanged(object? sender, string status)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = status;
            LiveDot.Fill = status == "Live"
                ? new SolidColorBrush((System.Windows.Media.Color)FindResource("OverlayNormalColor"))
                : new SolidColorBrush((System.Windows.Media.Color)FindResource("OverlayMutedColor"));
        });
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        var primary = snapshot.Primary;
        var usedPercent = UsageLevelResolver.Normalize(primary.Primary.UsedPercent);
        var remainingPercent = UsageLevelResolver.RemainingFromUsed(usedPercent);
        BucketNameText.Text = $"{primary.DisplayName} remaining";
        ResetText.Text =
            $"{Math.Round(usedPercent)}% used  ·  " +
            ResetTimeFormatter.Format(primary.Primary.ResetsAt, DateTimeOffset.Now);
        AdditionalBucketText.Text = snapshot.Additional.Count == 0
            ? "No additional limits"
            : FormatAdditional(snapshot.Additional[0]);

        AnimateRemainingFromUsed(usedPercent);
        AutomationProperties.SetName(
            RailHitTarget,
            $"Codex usage {Math.Round(remainingPercent)} percent remaining. {ResetText.Text}");
    }

    private void AnimateRemainingFromUsed(double usedPercentage)
    {
        var normalizedUsed = UsageLevelResolver.Normalize(usedPercentage);
        var remaining = UsageLevelResolver.RemainingFromUsed(normalizedUsed);
        var color = (System.Windows.Media.Color)FindResource(UsageLevelResolver.FromPercentage(normalizedUsed) switch
        {
            UsageLevel.Critical => "OverlayCriticalColor",
            UsageLevel.Warning => "OverlayWarningColor",
            _ => "OverlayNormalColor"
        });

        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(AnimatedRemainingPercentProperty, null);
            AnimatedRemainingPercent = remaining;
            UsageFillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            UsageFillBrush.Color = color;
            LeadingCap.Opacity = 0;
            return;
        }

        var animation = new DoubleAnimation(
            AnimatedRemainingPercent,
            remaining,
            TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(AnimatedRemainingPercentProperty, null);
            AnimatedRemainingPercent = remaining;
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
        window.UsagePercentText.Text = $"{Math.Round(normalized)}% left";
        window.RailRemainingText.Text = $"{Math.Round(normalized)}%";
        window.LeadingCapTranslate.Y = -(RailUsableHeight * normalized / 100d);
    }

    private static string FormatAdditional(RateLimitBucket bucket)
    {
        var remaining = UsageLevelResolver.RemainingFromUsed(bucket.Primary.UsedPercent);
        return $"{bucket.DisplayName}  {Math.Round(remaining)}% left";
    }

    private static UsageSnapshot CreateDemoSnapshot(double percentage)
    {
        var primary = new RateLimitBucket(
            "codex",
            "All Codex models",
            new QuotaWindow(percentage, 10_080, DateTimeOffset.Now.AddDays(3).AddHours(2)),
            null,
            "pro");
        var spark = new RateLimitBucket(
            "codex_spark",
            "GPT-5.3 Codex Spark",
            new QuotaWindow(0, 10_080, DateTimeOffset.Now.AddDays(7)),
            null,
            "pro");
        return new UsageSnapshot(primary, new[] { spark }, DateTimeOffset.Now);
    }

    private System.Windows.Forms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        _trayVisibilityMenuItem = new System.Windows.Forms.ToolStripMenuItem("Hide overlay");
        _trayVisibilityMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleManualVisibility);
        menu.Items.Add(_trayVisibilityMenuItem);
        _pauseMenuItem = new System.Windows.Forms.ToolStripMenuItem("Pause for 15 minutes");
        _pauseMenuItem.Click += (_, _) => Dispatcher.Invoke(TogglePause);
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(CreateDisplaySettingsMenu());
        _automaticStartupMenuItem = new System.Windows.Forms.ToolStripMenuItem("Start automatically with Codex")
        {
            Enabled = _startupShortcutManager.IsSupported
        };
        _automaticStartupMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleAutomaticStartup);
        menu.Items.Add(_automaticStartupMenuItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (_, _) => _ = _appServerClient.RefreshAsync());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(System.Windows.Application.Current.Shutdown));

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Codex Usage Overlay",
            Icon = System.Drawing.SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
        menu.Opening += (_, _) => Dispatcher.Invoke(UpdateSettingsMenuState);
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_isManuallyHidden)
            {
                ToggleManualVisibility();
                return;
            }

            _isPinned = true;
            ExpandDetails();
        });
        UpdateSettingsMenuState();
        return icon;
    }

    private System.Windows.Forms.ToolStripMenuItem CreateDisplaySettingsMenu()
    {
        var displayMenu = new System.Windows.Forms.ToolStripMenuItem("Display settings");
        var placementMenu = new System.Windows.Forms.ToolStripMenuItem("Position");

        _avoidSidebarPlacementItem = CreatePlacementMenuItem(
            "Avoid right sidebar",
            HorizontalPlacement.AvoidRightSidebar);
        _rightEdgePlacementItem = CreatePlacementMenuItem("Right edge", HorizontalPlacement.RightEdge);
        _leftEdgePlacementItem = CreatePlacementMenuItem("Left edge", HorizontalPlacement.LeftEdge);
        placementMenu.DropDownItems.Add(_avoidSidebarPlacementItem);
        placementMenu.DropDownItems.Add(_rightEdgePlacementItem);
        placementMenu.DropDownItems.Add(_leftEdgePlacementItem);
        displayMenu.DropDownItems.Add(placementMenu);
        displayMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        displayMenu.DropDownItems.Add(
            "Move left 20 px",
            null,
            (_, _) => Dispatcher.Invoke(() => NudgePosition(horizontal: -PositionNudge, vertical: 0)));
        displayMenu.DropDownItems.Add(
            "Move right 20 px",
            null,
            (_, _) => Dispatcher.Invoke(() => NudgePosition(horizontal: PositionNudge, vertical: 0)));
        displayMenu.DropDownItems.Add(
            "Move up 20 px",
            null,
            (_, _) => Dispatcher.Invoke(() => NudgePosition(horizontal: 0, vertical: PositionNudge)));
        displayMenu.DropDownItems.Add(
            "Move down 20 px",
            null,
            (_, _) => Dispatcher.Invoke(() => NudgePosition(horizontal: 0, vertical: -PositionNudge)));
        displayMenu.DropDownItems.Add(
            "Reset offsets",
            null,
            (_, _) => Dispatcher.Invoke(ResetPositionOffsets));
        displayMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        _hideFullscreenMenuItem = new System.Windows.Forms.ToolStripMenuItem("Hide in fullscreen");
        _hideFullscreenMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleHideInFullscreen);
        displayMenu.DropDownItems.Add(_hideFullscreenMenuItem);
        return displayMenu;
    }

    private System.Windows.Forms.ToolStripMenuItem CreatePlacementMenuItem(
        string label,
        HorizontalPlacement placement)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => Dispatcher.Invoke(() => SetPlacement(placement));
        return item;
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

    private void TogglePause()
    {
        _settings = _settings.IsPaused(DateTimeOffset.Now)
            ? _settings with { PausedUntil = null }
            : _settings with { PausedUntil = DateTimeOffset.Now.AddMinutes(15) };
        PersistSettings();
        ResetToCollapsedState();
        UpdateWindowPosition();
    }

    private void SetPlacement(HorizontalPlacement placement)
    {
        _settings = _settings with { Placement = placement };
        PersistSettings();
        UpdateWindowPosition();
    }

    private void NudgePosition(double horizontal, double vertical)
    {
        _settings = _settings with
        {
            HorizontalOffset = _settings.HorizontalOffset + horizontal,
            VerticalOffset = _settings.VerticalOffset + vertical
        };
        PersistSettings();
        UpdateWindowPosition();
    }

    private void ResetPositionOffsets()
    {
        _settings = _settings with { HorizontalOffset = 0, VerticalOffset = 0 };
        PersistSettings();
        UpdateWindowPosition();
    }

    private void ToggleHideInFullscreen()
    {
        _settings = _settings with { HideInFullscreen = !_settings.HideInFullscreen };
        PersistSettings();
        UpdateWindowPosition();
    }

    private void ToggleAutomaticStartup()
    {
        _ = _startupShortcutManager.SetEnabled(!_startupShortcutManager.IsEnabled);
        UpdateSettingsMenuState();
    }

    private void PersistSettings()
    {
        _settings = _settings.Normalize();
        _settingsStore.Save(_settings);
        UpdateSettingsMenuState();
    }

    private void UpdateSettingsMenuState()
    {
        _avoidSidebarPlacementItem.Checked =
            _settings.Placement == HorizontalPlacement.AvoidRightSidebar;
        _rightEdgePlacementItem.Checked = _settings.Placement == HorizontalPlacement.RightEdge;
        _leftEdgePlacementItem.Checked = _settings.Placement == HorizontalPlacement.LeftEdge;
        _hideFullscreenMenuItem.Checked = _settings.HideInFullscreen;
        _automaticStartupMenuItem.Checked = _startupShortcutManager.IsEnabled;
        _pauseMenuItem.Text = _settings.IsPaused(DateTimeOffset.Now)
            ? "Resume now"
            : "Pause for 15 minutes";
    }

    private void UpdateVisibilityMenuLabels()
    {
        var label = _isManuallyHidden ? "Show overlay" : "Hide overlay";
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
        RepositionAfterWidthChange();
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

    private void VisibilityMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ToggleManualVisibility();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
