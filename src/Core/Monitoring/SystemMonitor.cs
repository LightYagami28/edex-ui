// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Edex.Core.Monitoring;

internal static class Percentages
{
    internal const double Scale = 100.0;

    internal static double Of(double part, double whole) => whole <= 0 ? 0 : part * Scale / whole;
}

public sealed record CpuSample(double TotalPercent, int LogicalCores, string Model);
[CLSCompliant(false)]
public sealed record MemorySample(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public double UsedPercent => Percentages.Of(UsedBytes, TotalBytes);
}
public sealed record ProcessSample(int Pid, string Name, double CpuPercent, long WorkingSetBytes);
public sealed record DiskSample(string Name, string Mount, string Format, long TotalBytes, long FreeBytes)
{
    public double UsedPercent => Percentages.Of(TotalBytes - FreeBytes, TotalBytes);
}
[CLSCompliant(false)]
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    string HostName,
    string OsDescription,
    string Architecture,
    TimeSpan Uptime,
    CpuSample Cpu,
    MemorySample Memory,
    IReadOnlyList<ProcessSample> TopProcesses,
    IReadOnlyList<DiskSample> Disks);

public sealed class SystemMonitor
{
    private const int DefaultTopProcessCount = 6;
    private const long SmallBootPartitionBytes = 2L << 30;
    private static readonly string[] HiddenMountPrefixes = ["/snap", "/run", "/dev"];

    private readonly ICpuCounter _cpu = CpuCounters.Create();
    private readonly Dictionary<int, TimeSpan> _lastProcessTimes = [];
    private DateTime _lastProcessSample = DateTime.MinValue;
    private readonly string _cpuModel = CpuInfo.ReadModel();

    [CLSCompliant(false)]
    public SystemSnapshot Sample() => Sample(DefaultTopProcessCount);

    [CLSCompliant(false)]
    public SystemSnapshot Sample(int topProcessCount)
    {
        return new SystemSnapshot(
            DateTimeOffset.Now,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            new CpuSample(_cpu.SampleTotalPercent(), Environment.ProcessorCount, _cpuModel),
            MemoryInfo.Read(),
            SampleProcesses(topProcessCount),
            ReadDisks());
    }

    private List<ProcessSample> SampleProcesses(int count)
    {
        double elapsedMs = ElapsedSinceLastSample();
        var results = new List<ProcessSample>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (Safe.Run(() => SampleProcess(process, elapsedMs, seen), null) is { } sample)
                {
                    results.Add(sample);
                }
            }
        }
        _lastProcessTimes.Keys.Where(pid => !seen.Contains(pid)).ToList().ForEach(pid => _lastProcessTimes.Remove(pid));
        return Busiest(results, count);
    }

    private static List<ProcessSample> Busiest(List<ProcessSample> samples, int count) =>
        samples.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).Take(count).ToList();

    private double ElapsedSinceLastSample()
    {
        var now = DateTime.UtcNow;
        double elapsedMs = _lastProcessSample == DateTime.MinValue ? 0 : (now - _lastProcessSample).TotalMilliseconds;
        _lastProcessSample = now;
        return elapsedMs;
    }

    private ProcessSample? SampleProcess(Process process, double elapsedMs, HashSet<int> seen)
    {
        if (process.Id == 0)
        {
            return null;
        }

        var cpu = process.TotalProcessorTime;
        seen.Add(process.Id);
        double percent = elapsedMs > 0 && _lastProcessTimes.TryGetValue(process.Id, out var previous)
            ? (cpu - previous).TotalMilliseconds / elapsedMs / Environment.ProcessorCount * Percentages.Scale
            : 0;
        _lastProcessTimes[process.Id] = cpu;
        return new ProcessSample(process.Id, process.ProcessName, Math.Max(0, percent), process.WorkingSet64);
    }

    private static readonly HashSet<string> PseudoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "overlay", "squashfs", "autofs",
        "mqueue", "debugfs", "tracefs", "securityfs", "pstore", "bpf", "configfs", "fusectl", "hugetlbfs",
        "binfmt_misc", "devfs", "nullfs", "ramfs", "efivarfs", "rpc_pipefs", "nsfs",
    };

    public static IReadOnlyList<DiskSample> ReadDisks() =>
        DriveInfo.GetDrives().Select(d => Safe.Run(() => Describe(d), null)).OfType<DiskSample>().ToList();

    private static DiskSample? Describe(DriveInfo drive) =>
        IsUsable(drive) && !IsHiddenOnThisSystem(drive)
            ? new DiskSample(Label(drive), drive.RootDirectory.FullName, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace)
            : null;

    private static bool IsUsable(DriveInfo drive) =>
        drive.IsReady && drive.TotalSize > 0 && !PseudoFormats.Contains(drive.DriveFormat);

    private static bool IsHiddenOnThisSystem(DriveInfo drive) =>
        !OperatingSystem.IsWindows() && IsHiddenMount(drive.Name, drive.TotalSize);

    private static string Label(DriveInfo drive) =>
        OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? $"{drive.VolumeLabel} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})"
            : drive.Name;

    internal static bool IsHiddenMount(string name, long totalSize) =>
        Array.Exists(HiddenMountPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal))
        || IsSystemVolume(name)
        || IsSmallBootPartition(name, totalSize);

    private static bool IsSystemVolume(string name) =>
        name.StartsWith("/System/Volumes/", StringComparison.Ordinal) && name != "/System/Volumes/Data";

    private static bool IsSmallBootPartition(string name, long totalSize) =>
        name.StartsWith("/boot/efi", StringComparison.Ordinal) && totalSize < SmallBootPartitionBytes;
}
