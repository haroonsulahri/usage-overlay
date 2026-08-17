using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuotaRail.Services;

public static class NativeWindowStyle
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateStyle = 0x08000000L;
    private const long ToolWindowStyle = 0x00000080L;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    public static void ApplyNonActivatingToolWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var current = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        _ = SetWindowLongPtr(
            handle,
            ExtendedStyleIndex,
            new IntPtr(current | NoActivateStyle | ToolWindowStyle));
    }

    public static void ApplyDarkTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = 1;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
