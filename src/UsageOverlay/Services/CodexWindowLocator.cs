using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UsageOverlay.Services;

public sealed class CodexWindowLocator
{
    private const int DwmExtendedFrameBounds = 9;
    private const uint GetAncestorRoot = 2;
    private const uint MonitorDefaultToNearest = 2;

    public static bool TryGetActiveBounds(out WindowBounds bounds)
    {
        bounds = default;
        var window = GetAncestor(GetForegroundWindow(), GetAncestorRoot);

        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window) || !IsCodexWindow(window))
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
        bounds = new WindowBounds(
            rectangle.Left / scale,
            rectangle.Top / scale,
            rectangle.Right / scale,
            rectangle.Bottom / scale,
            isFullscreen);
        return true;
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

    private static bool IsCodexWindow(IntPtr window)
    {
        if (GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executablePath = process.MainModule?.FileName ?? string.Empty;
            if (executablePath.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                executablePath.EndsWith("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var title = GetTitle(window);
            return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(title, "ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                   executablePath.Contains("Codex", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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

public readonly record struct WindowBounds(
    double Left,
    double Top,
    double Right,
    double Bottom,
    bool IsFullscreen);
