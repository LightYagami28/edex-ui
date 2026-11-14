// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;

namespace Edex.Core.Monitoring;

internal static partial class MemoryInfo
{
    private const int HostVmInfo64 = 4;
    private const int VmInfo64Count = 38;
    private const int FreeCountIndex = 0;
    private const int InactiveCountIndex = 2;
    private const int SpeculativeCountIndex = 10;

    public static MemorySample Read() => Safe.Run(ReadSystem, new MemorySample((ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0));

    private static MemorySample ReadSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindows();
        }

        if (OperatingSystem.IsMacOS())
        {
            return ReadMac();
        }

        return ParseMeminfo(File.ReadAllText("/proc/meminfo"));
    }

    private static MemorySample ReadWindows()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return new MemorySample(0, 0);
        }

        return new MemorySample(status.ullTotalPhys, status.ullAvailPhys);
    }

    internal static MemorySample ParseMeminfo(string text)
    {
        ulong total = 0, available = 0, free = 0, buffers = 0, cached = 0;
        bool hasAvailable = false;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            ulong kb = ulong.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * 1024;
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
        var info = new uint[VmInfo64Count];
        int count = info.Length;
        ulong available = 0;
        if (host_statistics64(mach_host_self(), HostVmInfo64, info, ref count) == 0)
        {
            ulong free = info[FreeCountIndex];
            ulong inactive = info[InactiveCountIndex];
            ulong speculative = info.Length > SpeculativeCountIndex ? info[SpeculativeCountIndex] : 0;
            available = (free + inactive + speculative) * pageSize;
        }
        return new MemorySample(total, Math.Min(available, total));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("libc", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern uint mach_host_self();

    [DllImport("libc", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
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
        if (sysctlbyname(name, null, ref size, IntPtr.Zero, IntPtr.Zero) != 0 || size == IntPtr.Zero)
        {
            return "";
        }

        var buffer = new byte[(int)size];
        if (sysctlbyname(name, buffer, ref size, IntPtr.Zero, IntPtr.Zero) != 0)
        {
            return "";
        }

        return Encoding.UTF8.GetString(buffer).TrimEnd('\0').Trim();
    }

    [DllImport("libc", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int sysctlbyname(string name, ref ulong value, ref IntPtr size, IntPtr newValue, IntPtr newSize);

    [DllImport("libc", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int sysctlbyname(string name, byte[]? value, ref IntPtr size, IntPtr newValue, IntPtr newSize);
}
