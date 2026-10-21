using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexUsage.Core.Providers;
using CodexUsage.Core.Windows;

namespace UsageOverlay.Services;

/// <summary>Finds the foreground window of a supported AI app and measures it.</summary>
public static class HostWindowLocator
{
    private const int DwmExtendedFrameBounds = 9;
    private const uint GetAncestorRoot = 2;
    private const uint MonitorDefaultToNearest = 2;

    /// <param name="isEnabled">Whether the rail should attach to a provider's app.</param>
    /// <param name="isClaudeCodeHost">
    /// Recognises terminals and editors running Claude Code, which have no fixed identity.
    /// </param>
    public static bool TryGetActiveHost(
        Func<UsageProviderKind, bool> isEnabled,
        Func<uint, bool> isClaudeCodeHost,
        out ActiveHost host)
    {
        host = default;
        var window = GetAncestor(GetForegroundWindow(), GetAncestorRoot);

        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window) ||
            !TryClassify(window, isEnabled, isClaudeCodeHost, out var provider))
        {
            return false;
        }

        if (DwmGetWindowAttribute(
                window,
                DwmExtendedFrameBounds,
                out var rectangle,
                Marshal.SizeOf<NativeRectangle>()) != 0 &&
            !GetWindowRect(window, out rectangle))
        {
            return false;
        }

        var isFullscreen = IsFullscreen(window, rectangle);
        var dpi = GetDpiForWindow(window);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        host = new ActiveHost(
            provider,
            new WindowBounds(
                rectangle.Left / scale,
                rectangle.Top / scale,
                rectangle.Right / scale,
                rectangle.Bottom / scale,
                isFullscreen),
            TryDetectLightTheme(rectangle));
        return true;
    }

    private static bool? TryDetectLightTheme(NativeRectangle rectangle)
    {
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width < 200 || height < 120)
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var samplePoints = new (int X, int Y)[]
            {
                (rectangle.Left + width / 2, rectangle.Top + 18),
                (rectangle.Left + width / 2, rectangle.Top + 55),
                (rectangle.Left + width * 3 / 4, rectangle.Top + 55),
                (rectangle.Left + width / 4, rectangle.Top + 55),
                (rectangle.Left + width / 2, rectangle.Top + Math.Min(110, height / 4))
            };
            var luminanceSamples = new List<double>(samplePoints.Length);
            foreach (var point in samplePoints)
            {
                var color = GetPixel(screen, point.X, point.Y);
                if (color == uint.MaxValue)
                {
                    continue;
                }

                var red = color & 0xFF;
                var green = (color >> 8) & 0xFF;
                var blue = (color >> 16) & 0xFF;
                luminanceSamples.Add(0.2126 * red + 0.7152 * green + 0.0722 * blue);
            }

            if (luminanceSamples.Count < 3)
            {
                return null;
            }

            luminanceSamples.Sort();
            return luminanceSamples[luminanceSamples.Count / 2] >= 160;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static bool IsFullscreen(IntPtr window, NativeRectangle windowRectangle)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInformation
        {
            Size = Marshal.SizeOf<MonitorInformation>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(windowRectangle.Left - monitorInfo.Monitor.Left) <= tolerance &&
               Math.Abs(windowRectangle.Top - monitorInfo.Monitor.Top) <= tolerance &&
               Math.Abs(windowRectangle.Right - monitorInfo.Monitor.Right) <= tolerance &&
               Math.Abs(windowRectangle.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
    }

    private static bool TryClassify(
        IntPtr window,
        Func<UsageProviderKind, bool> isEnabled,
        Func<uint, bool> isClaudeCodeHost,
        out UsageProviderKind provider)
    {
        provider = UsageProviderKind.Codex;
        if (GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executablePath = process.MainModule?.FileName ?? string.Empty;
            var title = GetTitle(window);
            if (HostApplicationIdentity.Classify(executablePath, process.ProcessName, title) is { } app &&
                isEnabled(app))
            {
                provider = app;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Protected or exited processes can still be Claude Code hosts below.
        }

        if (isEnabled(UsageProviderKind.Claude) && isClaudeCodeHost(processId))
        {
            provider = UsageProviderKind.Claude;
            return true;
        }

        return false;
    }

    private static string GetTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowText(window, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation monitorInformation);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out NativeRectangle value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInformation
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}

public readonly record struct ActiveHost(
    UsageProviderKind Provider,
    WindowBounds Bounds,
    bool? IsLightTheme);

public readonly record struct WindowBounds(
    double Left,
    double Top,
    double Right,
    double Bottom,
    bool IsFullscreen);
