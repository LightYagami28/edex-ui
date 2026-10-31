// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;

namespace Edex.Core.Monitoring;

internal static class MemoryInfo
{
    public static MemorySample Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ParseMeminfo(File.ReadAllText("/proc/meminfo"));
            if (OperatingSystem.IsMacOS()) return ReadMac();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
        var gc = GC.GetGCMemoryInfo();
        return new MemorySample((ulong)gc.TotalAvailableMemoryBytes, 0);
    }

    private static MemorySample ReadWindows()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return new MemorySample(0, 0);
        return new MemorySample(status.ullTotalPhys, status.ullAvailPhys);
    }

    internal static MemorySample ParseMeminfo(string text)
    {
        ulong total = 0, available = 0, free = 0, buffers = 0, cached = 0;
        bool hasAvailable = false;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            ulong kb = ulong.Parse(parts[1]) * 1024;
            switch (parts[0])
            {
                case "MemTotal:": total = kb; break;
                case "MemAvailable:": available = kb; hasAvailable = true; break;
                case "MemFree:": free = kb; break;
                case "Buffers:": buffers = kb; break;
                case "Cached:": cached = kb; break;
            }
        }
        return new MemorySample(total, hasAvailable ? available : free + buffers + cached);
    }

    private static MemorySample ReadMac()
    {
        ulong total = MacSysctl.ReadUInt64("hw.memsize");
        ulong pageSize = MacSysctl.ReadUInt64("hw.pagesize");
        var info = new uint[38];
        int count = info.Length;
        ulong available = 0;
        if (host_statistics64(mach_host_self(), 4 /* HOST_VM_INFO64 */, info, ref count) == 0)
        {
            // vm_statistics64: free_count, active_count, inactive_count, wire_count, ... speculative_count at index 10 (uint32 units)
            ulong free = info[0], inactive = info[2], speculative = info.Length > 10 ? info[10] : 0;
            available = (free + inactive + speculative) * pageSize;
        }
        return new MemorySample(total, Math.Min(available, total));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("libc")]
    private static extern uint mach_host_self();

    [DllImport("libc")]
    private static extern int host_statistics64(uint host, int flavor, [Out] uint[] info, ref int count);
}

internal static class MacSysctl
{
    public static ulong ReadUInt64(string name)
    {
        ulong value = 0;
        var size = (IntPtr)sizeof(ulong);
        return sysctlbyname(name, ref value, ref size, IntPtr.Zero, IntPtr.Zero) == 0 ? value : 0;
    }

    public static string ReadString(string name)
    {
        var size = IntPtr.Zero;
        if (sysctlbyname(name, null, ref size, IntPtr.Zero, IntPtr.Zero) != 0 || size == IntPtr.Zero) return "";
        var buffer = new byte[(int)size];
        if (sysctlbyname(name, buffer, ref size, IntPtr.Zero, IntPtr.Zero) != 0) return "";
        return Encoding.UTF8.GetString(buffer).TrimEnd('\0').Trim();
    }

    [DllImport("libc")]
    private static extern int sysctlbyname(string name, ref ulong value, ref IntPtr size, IntPtr newValue, IntPtr newSize);

    [DllImport("libc")]
    private static extern int sysctlbyname(string name, byte[]? value, ref IntPtr size, IntPtr newValue, IntPtr newSize);
}
