using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using CodexUsage.Core.Settings;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;

namespace UsageOverlay.Infrastructure;

public sealed class ThemeManager
{
    private static ThemeManager? _instance;
    public static ThemeManager Instance => _instance ??= new ThemeManager();

    private AppTheme _currentTheme = AppTheme.System;
    private bool _systemEventsHooked;
    private bool? _lastCodexIsLight;

    public event Action? ThemeApplied;

    public bool IsEffectiveDark { get; private set; } = true;

    public void Initialize(AppTheme theme)
    {
        ApplyTheme(theme);
    }

    public void ApplyTheme(AppTheme theme)
    {
        var themeChanged = _currentTheme != theme;
        _currentTheme = theme;
        UpdateSystemEventsHook(theme == AppTheme.System);

        var isDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => !(_lastCodexIsLight ?? IsSystemInLightTheme())
        };

        if (!themeChanged && IsEffectiveDark == isDark && Application.Current is not null)
        {
            return;
        }

        IsEffectiveDark = isDark;
        ApplyPalette(isDark);
        ThemeApplied?.Invoke();
    }

    public void RefreshSystemThemeIfApplicable()
    {
        if (_currentTheme == AppTheme.System)
        {
            ApplyTheme(AppTheme.System);
        }
    }

    public void RefreshFromCodexTheme(bool isLight)
    {
        _lastCodexIsLight = isLight;
        if (_currentTheme == AppTheme.System && IsEffectiveDark == isLight)
        {
            ApplyTheme(AppTheme.System);
        }
    }

    private void UpdateSystemEventsHook(bool hook)
    {
        if (hook && !_systemEventsHooked)
        {
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _systemEventsHooked = true;
        }
        else if (!hook && _systemEventsHooked)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _systemEventsHooked = false;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_currentTheme == AppTheme.System)
                {
                    ApplyTheme(AppTheme.System);
                }
            });
        }
    }

    private static bool IsSystemInLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLight)
            {
                return appsUseLight == 1;
            }
        }
        catch
        {
            // Fallback to dark if registry cannot be queried
        }

        return false;
    }

    private static void ApplyPalette(bool isDark)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        Color surfaceColor;
        Color surfaceRaisedColor;
        Color borderColor;
        Color textColor;
        Color mutedColor;
        Color trackColor;
        Color normalColor;
        Color warningColor;
        Color criticalColor;
        Color menuHoverColor;
        Color menuCriticalHoverColor;
        Color settingsBgColor;
        Color buttonHoverColor;
        Color buttonPressedColor;
        Color leadingCapColor;
        Color primaryTextColor;

        if (isDark)
        {
            surfaceColor = Color.FromRgb(36, 36, 36);          // #FF242424
            surfaceRaisedColor = Color.FromRgb(42, 42, 42);    // #FF2A2A2A
            borderColor = Color.FromRgb(59, 59, 59);          // #FF3B3B3B
            textColor = Color.FromRgb(242, 242, 242);         // #FFF2F2F2
            mutedColor = Color.FromRgb(160, 160, 160);        // #FFA0A0A0
            trackColor = Color.FromRgb(53, 53, 53);           // #FF353535
            normalColor = Color.FromRgb(85, 200, 120);        // #FF55C878
            warningColor = Color.FromRgb(240, 180, 94);       // #FFF0B45E
            criticalColor = Color.FromRgb(229, 90, 90);       // #FFE55A5A
            menuHoverColor = Color.FromRgb(51, 51, 51);       // #FF333333
            menuCriticalHoverColor = Color.FromRgb(59, 41, 41); // #FF3B2929
            settingsBgColor = Color.FromRgb(23, 23, 23);      // #FF171717
            buttonHoverColor = Color.FromRgb(52, 52, 52);     // #FF343434
            buttonPressedColor = Color.FromRgb(46, 46, 46);   // #FF2E2E2E
            leadingCapColor = Color.FromRgb(242, 255, 245);   // #FFF2FFF5
            primaryTextColor = Color.FromRgb(16, 36, 25);     // #FF102419
        }
        else
        {
            surfaceColor = Color.FromRgb(255, 255, 255);       // #FFFFFFFF
            surfaceRaisedColor = Color.FromRgb(245, 246, 248); // #FFF5F6F8
            borderColor = Color.FromRgb(226, 232, 240);       // #FFE2E8F0
            textColor = Color.FromRgb(24, 24, 27);            // #FF18181B
            mutedColor = Color.FromRgb(107, 114, 128);        // #FF6B7280
            trackColor = Color.FromRgb(229, 231, 235);        // #FFE5E7EB
            normalColor = Color.FromRgb(21, 128, 61);         // #FF15803D
            warningColor = Color.FromRgb(217, 119, 6);        // #FFD97706
            criticalColor = Color.FromRgb(220, 38, 38);       // #FFDC2626
            menuHoverColor = Color.FromRgb(243, 244, 246);    // #FFF3F4F6
            menuCriticalHoverColor = Color.FromRgb(254, 226, 226); // #FFFEE2E2
            settingsBgColor = Color.FromRgb(248, 250, 252);   // #FFF8FAFC
            buttonHoverColor = Color.FromRgb(235, 238, 242);  // #FFEBEFF2
            buttonPressedColor = Color.FromRgb(220, 224, 230);// #FFDCE0E6
            leadingCapColor = Color.FromRgb(255, 255, 255);   // #FFFFFFFF
            primaryTextColor = Color.FromRgb(255, 255, 255);  // #FFFFFFFF
        }

        SetResourceColor(app, "OverlaySurfaceColor", surfaceColor);
        SetResourceColor(app, "OverlaySurfaceRaisedColor", surfaceRaisedColor);
        SetResourceColor(app, "OverlayBorderColor", borderColor);
        SetResourceColor(app, "OverlayTextColor", textColor);
        SetResourceColor(app, "OverlayMutedColor", mutedColor);
        SetResourceColor(app, "OverlayTrackColor", trackColor);
        SetResourceColor(app, "OverlayNormalColor", normalColor);
        SetResourceColor(app, "OverlayWarningColor", warningColor);
        SetResourceColor(app, "OverlayCriticalColor", criticalColor);
        SetResourceColor(app, "OverlayMenuHoverColor", menuHoverColor);
        SetResourceColor(app, "OverlayMenuCriticalHoverColor", menuCriticalHoverColor);
        SetResourceColor(app, "SettingsWindowBackgroundColor", settingsBgColor);
        SetResourceColor(app, "SettingsButtonHoverColor", buttonHoverColor);
        SetResourceColor(app, "SettingsButtonPressedColor", buttonPressedColor);
        SetResourceColor(app, "LeadingCapColor", leadingCapColor);
        SetResourceColor(app, "SettingsPrimaryTextColor", primaryTextColor);

        SetResourceBrush(app, "OverlaySurfaceBrush", surfaceColor);
        SetResourceBrush(app, "OverlaySurfaceRaisedBrush", surfaceRaisedColor);
        SetResourceBrush(app, "OverlayBorderBrush", borderColor);
        SetResourceBrush(app, "OverlayTextBrush", textColor);
        SetResourceBrush(app, "OverlayMutedBrush", mutedColor);
        SetResourceBrush(app, "OverlayTrackBrush", trackColor);
        SetResourceBrush(app, "OverlayNormalBrush", normalColor);
        SetResourceBrush(app, "OverlayWarningBrush", warningColor);
        SetResourceBrush(app, "OverlayCriticalBrush", criticalColor);
        SetResourceBrush(app, "OverlayMenuHoverBrush", menuHoverColor);
        SetResourceBrush(app, "OverlayMenuCriticalHoverBrush", menuCriticalHoverColor);
        SetResourceBrush(app, "SettingsWindowBackgroundBrush", settingsBgColor);
        SetResourceBrush(app, "SettingsButtonHoverBrush", buttonHoverColor);
        SetResourceBrush(app, "SettingsButtonPressedBrush", buttonPressedColor);
        SetResourceBrush(app, "LeadingCapBrush", leadingCapColor);
        SetResourceBrush(app, "SettingsPrimaryTextBrush", primaryTextColor);
    }

    private static void SetResourceColor(System.Windows.Application app, string key, System.Windows.Media.Color color)
    {
        app.Resources[key] = color;
    }

    private static void SetResourceBrush(System.Windows.Application app, string key, System.Windows.Media.Color color)
    {
        if (app.Resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
        }
        else
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }
}
