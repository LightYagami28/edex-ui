// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Edex.Core.Monitoring;

internal interface ICpuCounter
{
    /// <summary>Overall CPU usage since the previous call, in percent.</summary>
    double SampleTotalPercent();
}

internal static class CpuCounters
{
    public static ICpuCounter Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCpuCounter();
        if (OperatingSystem.IsLinux()) return new LinuxCpuCounter();
        if (OperatingSystem.IsMacOS()) return new MacCpuCounter();
        return new NullCpuCounter();
    }

    internal static double Percent(ulong idleDelta, ulong totalDelta) =>
        totalDelta == 0 ? 0 : Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
}

internal sealed class NullCpuCounter : ICpuCounter
{
    public double SampleTotalPercent() => 0;
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCpuCounter : ICpuCounter
{
    private ulong _idle, _total;

    public double SampleTotalPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        // Kernel time already includes idle time.
        ulong total = kernel + user;
        double result = _total == 0 ? 0 : CpuCounters.Percent(idle - _idle, total - _total);
        _idle = idle;
        _total = total;
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}

internal sealed class LinuxCpuCounter : ICpuCounter
{
    private ulong _idle, _total;

    public double SampleTotalPercent()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            var (idle, total) = Parse(line);
            double result = _total == 0 ? 0 : CpuCounters.Percent(idle - _idle, total - _total);
            _idle = idle;
            _total = total;
            return result;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or FormatException)
        {
            return 0;
        }
    }

    internal static (ulong Idle, ulong Total) Parse(string line)
    {
        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();
        ulong total = 0;
        for (int i = 0; i < Math.Min(values.Length, 8); i++) total += values[i];
        ulong idle = values[3] + (values.Length > 4 ? values[4] : 0);
        return (idle, total);
    }
}

[SupportedOSPlatform("macos")]
internal sealed class MacCpuCounter : ICpuCounter
{
    private ulong _idle, _total;

    public double SampleTotalPercent()
    {
        var info = new uint[4]; // user, system, idle, nice
        int count = 4;
        if (host_statistics(mach_host_self(), 3 /* HOST_CPU_LOAD_INFO */, info, ref count) != 0) return 0;
        ulong idle = info[2];
        ulong total = (ulong)info[0] + info[1] + info[2] + info[3];
        double result = _total == 0 ? 0 : CpuCounters.Percent(idle - _idle, total - _total);
        _idle = idle;
        _total = total;
        return result;
    }

    [DllImport("libc")]
    private static extern uint mach_host_self();

    [DllImport("libc")]
    private static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref int count);
}

internal static class CpuInfo
{
    public static string ReadModel()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var name = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            else if (OperatingSystem.IsLinux())
            {
                var line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line is not null) return line[(line.IndexOf(':') + 1)..].Trim();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var brand = MacSysctl.ReadString("machdep.cpu.brand_string");
                if (!string.IsNullOrWhiteSpace(brand)) return brand;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return RuntimeInformation.ProcessArchitecture.ToString();
    }
}
