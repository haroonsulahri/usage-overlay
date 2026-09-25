param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath
)

$ErrorActionPreference = 'Stop'
$json = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($json) -or $json.Length -gt 1048576) {
    exit 0
}

try {
    $data = $json | ConvertFrom-Json
    $rateLimits = $data.rate_limits
    $limits = [ordered]@{
        five_hour = $null
        seven_day = $null
    }
    foreach ($windowName in @('five_hour', 'seven_day')) {
        $window = $rateLimits.$windowName
        if ($null -ne $window -and
            $null -ne $window.used_percentage -and
            $null -ne $window.resets_at) {
            $limits[$windowName] = [ordered]@{
                used_percentage = [double]$window.used_percentage
                resets_at = [long]$window.resets_at
            }
        }
    }
    $processIds = [System.Collections.Generic.List[int]]::new()
    try {
        if (-not ("UsageOverlay.ProcessTree" -as [type])) {
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections.Generic;
            using System.Runtime.InteropServices;

            namespace UsageOverlay {
                public static class ProcessTree {
                    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                    private struct ProcessEntry {
                        public uint Size;
                        public uint Usage;
                        public uint ProcessId;
                        public IntPtr DefaultHeapId;
                        public uint ModuleId;
                        public uint Threads;
                        public uint ParentProcessId;
                        public int PriorityBase;
                        public uint Flags;
                        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
                    }

                    [DllImport("kernel32.dll", SetLastError = true)]
                    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
                    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                    [return: MarshalAs(UnmanagedType.Bool)]
                    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
                    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                    [return: MarshalAs(UnmanagedType.Bool)]
                    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
                    [DllImport("kernel32.dll", SetLastError = true)]
                    [return: MarshalAs(UnmanagedType.Bool)]
                    private static extern bool CloseHandle(IntPtr handle);

                    public static int[] GetAncestors(int processId, int maximum) {
                        var snapshot = CreateToolhelp32Snapshot(2, 0);
                        if (snapshot == new IntPtr(-1)) return Array.Empty<int>();
                        try {
                            var parents = new Dictionary<int, int>();
                            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                            if (Process32First(snapshot, ref entry)) {
                                do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
                                while (Process32Next(snapshot, ref entry));
                            }
                            var result = new List<int>();
                            var current = processId;
                            while (result.Count < maximum && parents.TryGetValue(current, out var parent) &&
                                   parent > 0 && !result.Contains(parent)) {
                                result.Add(parent);
                                current = parent;
                            }
                            return result.ToArray();
                        } finally { CloseHandle(snapshot); }
                    }
                }
            }
'@ -ErrorAction Stop
        }

        foreach ($parentId in [UsageOverlay.ProcessTree]::GetAncestors($PID, 24)) {
            $processIds.Add([int]$parentId)
        }
    } catch {
        $processIds.Clear()
    }

    $snapshot = [ordered]@{
        rate_limits = $limits
        received_at = [DateTimeOffset]::UtcNow.ToString('O')
        process_ids = @($processIds)
    }
    $directory = Split-Path -Parent $SnapshotPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = "$SnapshotPath.$PID.tmp"
    $serialized = $snapshot | ConvertTo-Json -Depth 6 -Compress
    [System.IO.File]::WriteAllText($temporaryPath, $serialized, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $SnapshotPath -Force

    $parts = [System.Collections.Generic.List[string]]::new()
    if ($null -ne $limits.five_hour.used_percentage) {
        $parts.Add("5h $([Math]::Round([double]$limits.five_hour.used_percentage))%")
    }
    if ($null -ne $limits.seven_day.used_percentage) {
        $parts.Add("7d $([Math]::Round([double]$limits.seven_day.used_percentage))%")
    }
    if ($parts.Count -gt 0) {
        $parts -join ' | '
    } else {
        'Plan usage unavailable'
    }
} catch {
    exit 0
}
