using System.Runtime.InteropServices;

namespace UsageOverlay.Services;

internal readonly record struct ProcessIdentity(int ProcessId, string ExecutableName);

/// <summary>Lists a process's ancestors, such as the terminal that hosts Claude Code.</summary>
internal static class ProcessAncestry
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int MaximumDepth = 24;
    private static readonly IntPtr InvalidHandle = new(-1);

    // The Windows shell and system hosts launch everything; walking past them would make
    // unrelated windows, such as File Explorer, look like Claude Code hosts.
    private static readonly HashSet<string> BoundaryProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "svchost.exe", "services.exe", "wininit.exe", "winlogon.exe", "sihost.exe",
        "userinit.exe", "runtimebroker.exe", "dllhost.exe", "smss.exe", "csrss.exe", "taskhostw.exe"
    };

    /// <summary>Returns ancestors nearest first, stopping before the Windows shell.</summary>
    public static IReadOnlyList<ProcessIdentity> GetAncestors(int processId)
    {
        var processes = ReadProcesses();
        var ancestors = new List<ProcessIdentity>();
        var current = processId;
        while (ancestors.Count < MaximumDepth &&
               processes.TryGetValue(current, out var entry) &&
               entry.Parent > 4 &&
               entry.Parent != current &&
               ancestors.All(ancestor => ancestor.ProcessId != entry.Parent) &&
               processes.TryGetValue(entry.Parent, out var parent) &&
               !BoundaryProcesses.Contains(parent.Name))
        {
            ancestors.Add(new ProcessIdentity(entry.Parent, parent.Name));
            current = entry.Parent;
        }

        return ancestors;
    }

    private static Dictionary<int, (int Parent, string Name)> ReadProcesses()
    {
        var parents = new Dictionary<int, (int Parent, string Name)>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero)
        {
            return parents;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry))
            {
                return parents;
            }

            do
            {
                parents[(int)entry.ProcessId] = ((int)entry.ParentProcessId, entry.ExecutableFile ?? string.Empty);
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }

        return parents;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
