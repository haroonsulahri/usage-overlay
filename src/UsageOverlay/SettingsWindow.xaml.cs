using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Navigation;
using CodexUsage.Core.Settings;
using UsageOverlay.Infrastructure;
using UsageOverlay.Services;

namespace UsageOverlay;

public partial class SettingsWindow : Window
{
    private const double NudgeAmount = 20;

    private readonly Action<OverlaySettings, bool> _saveSettings;
    private readonly Action _openLogs;
    private readonly bool _startupSupported;
    private OverlaySettings _settings;
    private bool _startAutomatically;
    private DateTimeOffset? _pendingPausedUntil;
    private double _pendingHorizontalOffset;
    private double _pendingVerticalOffset;

    public bool IsHiddenByUser { get; private set; }

    public SettingsWindow(
        OverlaySettings settings,
        bool startAutomatically,
        bool startupSupported,
        string codexCliStatus,
        Action<OverlaySettings, bool> saveSettings,
        Action openLogs)
    {
        InitializeComponent();
        _settings = settings.Normalize();
        _startAutomatically = startAutomatically;
        _startupSupported = startupSupported;
        _pendingPausedUntil = _settings.PausedUntil;
        _pendingHorizontalOffset = _settings.HorizontalOffset;
        _pendingVerticalOffset = _settings.VerticalOffset;
        _saveSettings = saveSettings;
        _openLogs = openLogs;
        CodexCliStatusText.Text = codexCliStatus;
        LoadControls();
    }

    private void LoadControls()
    {
        StartAutomaticallyCheckBox.IsChecked = _startAutomatically;
        StartAutomaticallyCheckBox.IsEnabled = _startupSupported;
        CodexOnlyVisibilityRadio.IsChecked = _settings.ShowOnlyWhenCodexActive;
        AcrossWindowsVisibilityRadio.IsChecked = !_settings.ShowOnlyWhenCodexActive;
        FollowMonitorsCheckBox.IsChecked = _settings.FollowCodexAcrossMonitors;
        HideFullscreenCheckBox.IsChecked = _settings.HideInFullscreen;
        AnimationsCheckBox.IsChecked = _settings.AnimationsEnabled;
        CompactPercentageCheckBox.IsChecked = _settings.ShowCompactPercentage;
        SystemThemeRadio.IsChecked = _settings.Theme == AppTheme.System;
        DarkThemeRadio.IsChecked = _settings.Theme == AppTheme.Dark;
        LightThemeRadio.IsChecked = _settings.Theme == AppTheme.Light;
        RemainingPrimaryRadio.IsChecked = _settings.PrimaryDisplay == PrimaryUsageDisplay.Remaining;
        UsedPrimaryRadio.IsChecked = _settings.PrimaryDisplay == PrimaryUsageDisplay.Used;
        RightEdgeRadio.IsChecked = _settings.Placement == HorizontalPlacement.RightEdge;
        LeftEdgeRadio.IsChecked = _settings.Placement == HorizontalPlacement.LeftEdge;
        CustomPositionRadio.IsChecked = _settings.Placement == HorizontalPlacement.Custom;
        WarningThresholdTextBox.Text = _settings.WarningThreshold.ToString(CultureInfo.InvariantCulture);
        CriticalThresholdTextBox.Text = _settings.CriticalThreshold.ToString(CultureInfo.InvariantCulture);
        CodexCliPathTextBox.Text = _settings.CodexCliPath;
        RefreshIntervalTextBox.Text = _settings.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        UpdatePauseControls();
        UpdatePositionOffsetText();
    }

    private void ThemeRadio_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        var theme = LightThemeRadio.IsChecked == true
            ? AppTheme.Light
            : DarkThemeRadio.IsChecked == true
                ? AppTheme.Dark
                : AppTheme.System;
        ThemeManager.Instance.ApplyTheme(theme);
        NativeWindowStyle.ApplyTitleBarTheme(this, ThemeManager.Instance.IsEffectiveDark);
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await Task.Delay(100);
        if (!IsLoaded)
        {
            return;
        }

        SettingsScrollViewer.ScrollToTop();
        _ = StartAutomaticallyCheckBox.Focus();
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        NativeWindowStyle.ApplyTitleBarTheme(this, ThemeManager.Instance.IsEffectiveDark);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer was released before the drag operation began.
        }
    }

    public void ShowFromLauncher()
    {
        IsHiddenByUser = false;
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    private void HideSettingsButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        IsHiddenByUser = true;
        Hide();
    }

    private void CloseSettingsButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ThemeManager.Instance.ApplyTheme(_settings.Theme);
        Close();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ValidationText.Text = string.Empty;
        SavedStatusText.Text = string.Empty;

        if (!TryReadDouble(WarningThresholdTextBox.Text, out var warningThreshold) ||
            !TryReadDouble(CriticalThresholdTextBox.Text, out var criticalThreshold))
        {
            ValidationText.Text = "Enter numbers for both colour thresholds.";
            WarningThresholdTextBox.Focus();
            return;
        }

        if (warningThreshold < 0 || criticalThreshold > 100 || warningThreshold >= criticalThreshold)
        {
            ValidationText.Text = "Set amber below red. Both values must be between 0 and 100.";
            WarningThresholdTextBox.Focus();
            return;
        }

        if (!int.TryParse(RefreshIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) ||
            interval is < 15 or > 3_600)
        {
            ValidationText.Text = "Choose a refresh time between 15 and 3600 seconds.";
            SetConnectionSettingsExpanded(true);
            RefreshIntervalTextBox.Focus();
            return;
        }

        var cliPath = CodexCliPathTextBox.Text.Trim();
        if (cliPath.Length > 0 && !File.Exists(cliPath))
        {
            ValidationText.Text = "We couldn’t find the Codex CLI at that path.";
            SetConnectionSettingsExpanded(true);
            CodexCliPathTextBox.Focus();
            return;
        }

        var placement = LeftEdgeRadio.IsChecked == true
            ? HorizontalPlacement.LeftEdge
            : CustomPositionRadio.IsChecked == true
                ? HorizontalPlacement.Custom
                : HorizontalPlacement.RightEdge;
        var primaryDisplay = UsedPrimaryRadio.IsChecked == true
            ? PrimaryUsageDisplay.Used
            : PrimaryUsageDisplay.Remaining;

        var theme = LightThemeRadio.IsChecked == true
            ? AppTheme.Light
            : DarkThemeRadio.IsChecked == true
                ? AppTheme.Dark
                : AppTheme.System;

        var updated = (_settings with
        {
            Theme = theme,
            Placement = placement,
            HorizontalOffset = _pendingHorizontalOffset,
            VerticalOffset = _pendingVerticalOffset,
            HideInFullscreen = HideFullscreenCheckBox.IsChecked == true,
            ShowOnlyWhenCodexActive = CodexOnlyVisibilityRadio.IsChecked == true,
            FollowCodexAcrossMonitors = FollowMonitorsCheckBox.IsChecked == true,
            PrimaryDisplay = primaryDisplay,
            WarningThreshold = warningThreshold,
            CriticalThreshold = criticalThreshold,
            AnimationsEnabled = AnimationsCheckBox.IsChecked == true,
            ShowCompactPercentage = CompactPercentageCheckBox.IsChecked == true,
            CodexCliPath = cliPath,
            RefreshIntervalSeconds = interval,
            PausedUntil = _pendingPausedUntil
        }).Normalize();

        var requiresRestart =
            !string.Equals(updated.CodexCliPath, _settings.CodexCliPath, StringComparison.OrdinalIgnoreCase) ||
            updated.RefreshIntervalSeconds != _settings.RefreshIntervalSeconds;
        _startAutomatically = StartAutomaticallyCheckBox.IsChecked == true;
        _saveSettings(updated, _startAutomatically);
        _settings = updated;
        SavedStatusText.Text = requiresRestart
            ? "Saved. Restart Usage Overlay to use the new CLI path or refresh time."
            : $"Saved at {DateTime.Now:t}.";
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ThemeManager.Instance.ApplyTheme(_settings.Theme);
        Close();
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _pendingPausedUntil = IsPaused()
            ? null
            : DateTimeOffset.Now.AddMinutes(15);
        UpdatePauseControls();
    }

    private void MoveLeftButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _pendingHorizontalOffset -= NudgeAmount;
        UpdatePositionOffsetText();
    }

    private void MoveRightButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _pendingHorizontalOffset += NudgeAmount;
        UpdatePositionOffsetText();
    }

    private void MoveUpButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _pendingVerticalOffset += NudgeAmount;
        UpdatePositionOffsetText();
    }

    private void MoveDownButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _pendingVerticalOffset -= NudgeAmount;
        UpdatePositionOffsetText();
    }

    private void ResetPositionButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        RightEdgeRadio.IsChecked = true;
        _pendingHorizontalOffset = 0;
        _pendingVerticalOffset = 0;
        UpdatePositionOffsetText();
    }

    private void ResetAllButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        _settings = new OverlaySettings();
        _startAutomatically = false;
        _pendingPausedUntil = null;
        _pendingHorizontalOffset = 0;
        _pendingVerticalOffset = 0;
        ValidationText.Text = string.Empty;
        SavedStatusText.Text = "Defaults are ready. Select Save to use them.";
        LoadControls();
        ThemeManager.Instance.ApplyTheme(_settings.Theme);
        NativeWindowStyle.ApplyTitleBarTheme(this, ThemeManager.Instance.IsEffectiveDark);
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs eventArgs) => _openLogs();

    private void ConnectionToggleButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        SetConnectionSettingsExpanded(ConnectionSettingsPanel.Visibility != Visibility.Visible);
    }

    private void SetConnectionSettingsExpanded(bool expanded)
    {
        ConnectionSettingsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ConnectionToggleButton.Content = expanded ? "Hide details" : "Show details";
        AutomationProperties.SetName(
            ConnectionToggleButton,
            expanded ? "Hide connection and diagnostics" : "Show connection and diagnostics");
    }

    private void HarooneLink_OnRequestNavigate(object sender, RequestNavigateEventArgs eventArgs)
    {
        try
        {
            Process.Start(new ProcessStartInfo(eventArgs.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ValidationText.Text = "We couldn’t open Haroone.com in your browser.";
        }

        eventArgs.Handled = true;
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            ThemeManager.Instance.ApplyTheme(_settings.Theme);
            Close();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveButton_OnClick(this, new RoutedEventArgs());
            eventArgs.Handled = true;
        }
    }

    private bool IsPaused() => _pendingPausedUntil is { } pausedUntil && pausedUntil > DateTimeOffset.Now;

    private void UpdatePauseControls()
    {
        if (IsPaused())
        {
            PauseButton.Content = "Resume now";
            PauseStatusText.Text = $"Paused until {_pendingPausedUntil!.Value.LocalDateTime:t}";
        }
        else
        {
            PauseButton.Content = "Pause 15 min";
            PauseStatusText.Text = "Usage Overlay is running.";
        }
    }

    private void UpdatePositionOffsetText()
    {
        PositionOffsetText.Text =
            $"Position offset: X {_pendingHorizontalOffset:+0;-0;0}px, " +
            $"Y {_pendingVerticalOffset:+0;-0;0}px";
    }

    private static bool TryReadDouble(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }
}
