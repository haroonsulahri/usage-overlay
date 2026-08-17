using System.Runtime.InteropServices;

namespace UsageOverlay.Services;

public static class NativeHotKey
{
    public static bool Register(IntPtr windowHandle, int identifier, int virtualKey) =>
        RegisterHotKey(windowHandle, identifier, 0, virtualKey);

    public static void Unregister(IntPtr windowHandle, int identifier)
    {
        if (windowHandle != IntPtr.Zero)
        {
            _ = UnregisterHotKey(windowHandle, identifier);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int identifier,
        uint modifiers,
        int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int identifier);
}
