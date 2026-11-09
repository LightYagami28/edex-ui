// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Files;
using Edex.Core.Monitoring;
using Edex.Core.Settings;
using Edex.Core.Themes;

namespace Edex.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("edex-test-").FullName;
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

[TestClass]
public class ThemeTests
{
    [TestMethod]
    public void BuiltInThemesAreValid()
    {
        Assert.Contains(t => t.Name == "tron", ThemeLibrary.BuiltIn);
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.IsNotNull(ThemeLibrary.TryParse(ThemeLibrary.Serialize(theme), "x"));
        }
    }

    [TestMethod]
    public void LoadsCustomThemeAndRejectsInvalid()
    {
        using var dir = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "ocean.json"),
            """{ "accent": "#00AAFF", "background": "#000010", "surface": "#000020", "foreground": "#DDEEFF", "muted": "#334455" }""");
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "broken.json"), """{ "accent": "blue" }""");
        var themes = ThemeLibrary.Load(dir.Path);
        Assert.Contains(t => t.Name == "ocean" && t.Accent == "#00AAFF", themes);
        Assert.DoesNotContain(t => t.Name == "broken", themes);
    }

    [TestMethod]
    public void ParsesColors()
    {
        Assert.AreEqual(((byte)0xAA, (byte)0xCF, (byte)0xD1), ColorUtil.Parse("#AACFD1"));
        Assert.IsFalse(ColorUtil.IsValidHex("#12345"));
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), ColorUtil.Xterm256(231));
        Assert.AreEqual(((byte)8, (byte)8, (byte)8), ColorUtil.Xterm256(232));
    }
}

[TestClass]
public class SettingsTests
{
    [TestMethod]
    public void RoundTrips()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        Assert.AreEqual("tron", store.Load().Theme);
        store.Save(new AppSettings { Theme = "amber", FontSize = 16, Shell = "sh" });
        var loaded = store.Load();
        Assert.AreEqual("amber", loaded.Theme);
        Assert.AreEqual(16, loaded.FontSize);
        Assert.AreEqual("sh", loaded.Shell);
        Assert.IsTrue(Directory.Exists(store.ThemesDirectory));
    }

    [TestMethod]
    public void CorruptFileFallsBackToDefaults()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, "{ not json");
        Assert.AreEqual("tron", store.Load().Theme);
    }
}

[TestClass]
public class FileBrowserTests
{
    [TestMethod]
    public void ListsDirectoriesFirst()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "zdir"));
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "afile.txt"), "12345");
        File.WriteAllText(System.IO.Path.Combine(dir.Path, ".hidden"), "x");
        var listing = FileBrowser.List(dir.Path);
        Assert.IsNull(listing.Error);
        Assert.AreSequenceEqual(["..", "zdir", "afile.txt"], listing.Entries.Select(e => e.Name).ToArray());
        Assert.AreEqual(5, listing.Entries[2].Size);
        Assert.Contains(e => e.Name == ".hidden", FileBrowser.List(dir.Path, showHidden: true).Entries);
    }

    [TestMethod]
    public void MissingDirectoryReportsError()
    {
        var listing = FileBrowser.List(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.IsNotNull(listing.Error);
    }

    [TestMethod]
    [DataRow(0, "0 B")]
    [DataRow(1536, "1.5 KB")]
    [DataRow(1073741824, "1 GB")]
    public void FormatsSizes(long bytes, string expected) =>
        Assert.AreEqual(expected, FileBrowser.FormatSize(bytes).Replace(',', '.'));
}

[TestClass]
public class MonitorTests
{
    public TestContext TestContext { get; set; }

    private const int SampleIntervalMs = 200;

    [TestMethod]
    public async Task SamplesSystem()
    {
        var monitor = new SystemMonitor();
        monitor.Sample();
        await Task.Delay(SampleIntervalMs, TestContext.CancellationToken);
        var snap = monitor.Sample();
        Assert.IsGreaterThan(0UL, snap.Memory.TotalBytes);
        Assert.IsTrue(snap.Cpu.TotalPercent >= 0 && snap.Cpu.TotalPercent <= 100);
        Assert.IsGreaterThan(0, snap.Cpu.LogicalCores);
        Assert.IsNotEmpty(snap.TopProcesses);
    }

    [TestMethod]
    public void ParsesLinuxProcFiles()
    {
        var (idle, total) = LinuxCpuCounter.Parse("cpu  10 0 10 70 10 0 0 0 0 0");
        Assert.AreEqual(80ul, idle);
        Assert.AreEqual(100ul, total);
        var mem = MemoryInfo.ParseMeminfo("MemTotal:       1000 kB\nMemFree:  100 kB\nMemAvailable:   400 kB\n");
        Assert.AreEqual(1000ul * 1024, mem.TotalBytes);
        Assert.AreEqual(400ul * 1024, mem.AvailableBytes);
        Assert.AreEqual(60, mem.UsedPercent, 3);
    }
}
