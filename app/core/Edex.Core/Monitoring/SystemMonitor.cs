// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Edex.Core.Monitoring;

public sealed record CpuSample(double TotalPercent, int LogicalCores, string Model);
public sealed record MemorySample(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}
public sealed record ProcessSample(int Pid, string Name, double CpuPercent, long WorkingSetBytes);
public sealed record DiskSample(string Name, string Mount, string Format, long TotalBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : (TotalBytes - FreeBytes) * 100.0 / TotalBytes;
}
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

/// <summary>Cross-platform system sampler. Call <see cref="Sample"/> periodically (e.g. once per second).</summary>
public sealed class SystemMonitor
{
    private readonly ICpuCounter _cpu = CpuCounters.Create();
    private readonly Dictionary<int, TimeSpan> _lastProcessTimes = [];
    private DateTime _lastProcessSample = DateTime.MinValue;
    private readonly string _cpuModel = CpuInfo.ReadModel();

    public SystemSnapshot Sample(int topProcessCount = 6)
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

    private IReadOnlyList<ProcessSample> SampleProcesses(int count)
    {
        var now = DateTime.UtcNow;
        double elapsedMs = _lastProcessSample == DateTime.MinValue ? 0 : (now - _lastProcessSample).TotalMilliseconds;
        _lastProcessSample = now;
        var results = new List<ProcessSample>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0) continue;
                    var cpu = process.TotalProcessorTime;
                    seen.Add(process.Id);
                    double percent = 0;
                    if (elapsedMs > 0 && _lastProcessTimes.TryGetValue(process.Id, out var previous))
                        percent = (cpu - previous).TotalMilliseconds / elapsedMs / Environment.ProcessorCount * 100.0;
                    _lastProcessTimes[process.Id] = cpu;
                    results.Add(new ProcessSample(process.Id, process.ProcessName, Math.Max(0, percent), process.WorkingSet64));
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
                {
                    // Access denied (system processes) or the process already exited.
                }
            }
        }
        foreach (var pid in _lastProcessTimes.Keys.Where(pid => !seen.Contains(pid)).ToList()) _lastProcessTimes.Remove(pid);
        return results
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.WorkingSetBytes)
            .Take(count)
            .ToList();
    }

    private static readonly HashSet<string> PseudoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "overlay", "squashfs", "autofs",
        "mqueue", "debugfs", "tracefs", "securityfs", "pstore", "bpf", "configfs", "fusectl", "hugetlbfs",
        "binfmt_misc", "devfs", "nullfs", "ramfs", "efivarfs", "rpc_pipefs", "nsfs",
    };

    public static IReadOnlyList<DiskSample> ReadDisks()
    {
        var disks = new List<DiskSample>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.TotalSize <= 0) continue;
                if (PseudoFormats.Contains(drive.DriveFormat)) continue;
                if (!OperatingSystem.IsWindows() && (drive.Name.StartsWith("/snap", StringComparison.Ordinal)
                    || drive.Name.StartsWith("/System/Volumes/", StringComparison.Ordinal) && drive.Name != "/System/Volumes/Data"
                    || drive.Name.StartsWith("/boot/efi", StringComparison.Ordinal) && drive.TotalSize < 2L << 30
                    || drive.Name.StartsWith("/run", StringComparison.Ordinal) || drive.Name.StartsWith("/dev", StringComparison.Ordinal)))
                    continue;
                string label = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})"
                    : drive.Name;
                disks.Add(new DiskSample(label, drive.RootDirectory.FullName, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return disks;
    }
}
