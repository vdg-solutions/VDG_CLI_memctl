using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Memctl.Hardening;

internal static class AntiDebug
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsDebuggerPresent();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool present);

    // macOS sysctl path. KERN_PROC + KERN_PROC_PID lookup, check kp_proc.p_flag & P_TRACED.
    [DllImport("libc", EntryPoint = "sysctl")]
    private static extern int Sysctl_macOS(int[] name, uint nameLen, IntPtr oldp, ref IntPtr oldlenp, IntPtr newp, IntPtr newlen);

    public static void Check()
    {
        if (Environment.GetEnvironmentVariable("MEMCTL_ALLOW_DEBUG") == "1") return;

        if (Debugger.IsAttached) Environment.FailFast("");

        if (OperatingSystem.IsWindows())   CheckWindows();
        else if (OperatingSystem.IsLinux())  CheckLinux();
        else if (OperatingSystem.IsMacOS())  CheckMacOS();
    }

    private static void CheckWindows()
    {
        try
        {
            if (IsDebuggerPresent()) Environment.FailFast("");
            var present = false;
            CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref present);
            if (present) Environment.FailFast("");
        }
        catch { }
    }

    private static void CheckLinux()
    {
        try
        {
            var status = File.ReadAllText("/proc/self/status");
            foreach (var line in status.Split('\n'))
            {
                if (!line.StartsWith("TracerPid:", StringComparison.Ordinal)) continue;
                var v = line.AsSpan(10).Trim();
                if (v.Length > 0 && v[0] != '0') Environment.FailFast("");
                break;
            }
        }
        catch { }
    }

    private static void CheckMacOS()
    {
        try
        {
            // sysctl path: { CTL_KERN=1, KERN_PROC=14, KERN_PROC_PID=1, pid }
            // kinfo_proc struct → kp_proc.p_flag bit P_TRACED (0x800)
            const int CTL_KERN      = 1;
            const int KERN_PROC     = 14;
            const int KERN_PROC_PID = 1;
            const int P_TRACED      = 0x800;
            // kinfo_proc struct on macOS is ~648 bytes; p_flag offset varies by SDK.
            // Pragmatic: read full struct, scan first 64 bytes for non-zero p_flag with P_TRACED bit.
            var name = new[] { CTL_KERN, KERN_PROC, KERN_PROC_PID, Environment.ProcessId };
            var size = (IntPtr)648;
            var buf  = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Sysctl_macOS(name, 4, buf, ref size, IntPtr.Zero, IntPtr.Zero) != 0) return;
                // Scan bytes 0..64 for any int with P_TRACED bit set.
                var bytes = new byte[64];
                Marshal.Copy(buf, bytes, 0, bytes.Length);
                for (var i = 0; i + 4 <= bytes.Length; i += 4)
                {
                    var v = BitConverter.ToInt32(bytes, i);
                    if ((v & P_TRACED) != 0) Environment.FailFast("");
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
    }
}
