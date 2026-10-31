// SPDX-License-Identifier: GPL-3.0-only
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

public class ThemeTests
{
    [Fact]
    public void BuiltInThemesAreValid()
    {
        Assert.Contains(ThemeLibrary.BuiltIn, t => t.Name == "tron");
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.NotNull(ThemeLibrary.TryParse(ThemeLibrary.Serialize(theme), "x"));
        }
    }

    [Fact]
    public void LoadsCustomThemeAndRejectsInvalid()
    {
        using var dir = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "ocean.json"),
            """{ "accent": "#00AAFF", "background": "#000010", "surface": "#000020", "foreground": "#DDEEFF", "muted": "#334455" }""");
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "broken.json"), """{ "accent": "blue" }""");
        var themes = ThemeLibrary.Load(dir.Path);
        Assert.Contains(themes, t => t.Name == "ocean" && t.Accent == "#00AAFF");
        Assert.DoesNotContain(themes, t => t.Name == "broken");
    }

    [Fact]
    public void ParsesColors()
    {
        Assert.Equal(((byte)0xAA, (byte)0xCF, (byte)0xD1), ColorUtil.Parse("#AACFD1"));
        Assert.False(ColorUtil.IsValidHex("#12345"));
        Assert.Equal(((byte)255, (byte)255, (byte)255), ColorUtil.Xterm256(231));
        Assert.Equal(((byte)8, (byte)8, (byte)8), ColorUtil.Xterm256(232));
    }
}

public class SettingsTests
{
    [Fact]
    public void RoundTrips()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        Assert.Equal("tron", store.Load().Theme);
        store.Save(new AppSettings { Theme = "amber", FontSize = 16, Shell = "sh" });
        var loaded = store.Load();
        Assert.Equal("amber", loaded.Theme);
        Assert.Equal(16, loaded.FontSize);
        Assert.Equal("sh", loaded.Shell);
        Assert.True(Directory.Exists(store.ThemesDirectory));
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Equal("tron", store.Load().Theme);
    }
}

public class FileBrowserTests
{
    [Fact]
    public void ListsDirectoriesFirst()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "zdir"));
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "afile.txt"), "12345");
        File.WriteAllText(System.IO.Path.Combine(dir.Path, ".hidden"), "x");
        var listing = FileBrowser.List(dir.Path);
        Assert.Null(listing.Error);
        Assert.Equal(["..", "zdir", "afile.txt"], listing.Entries.Select(e => e.Name).ToArray());
        Assert.Equal(5, listing.Entries[2].Size);
        Assert.Contains(FileBrowser.List(dir.Path, showHidden: true).Entries, e => e.Name == ".hidden");
    }

    [Fact]
    public void MissingDirectoryReportsError()
    {
        var listing = FileBrowser.List(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.NotNull(listing.Error);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatsSizes(long bytes, string expected) =>
        Assert.Equal(expected, FileBrowser.FormatSize(bytes).Replace(',', '.'));
}

public class MonitorTests
{
    [Fact]
    public void SamplesSystem()
    {
        var monitor = new SystemMonitor();
        monitor.Sample();
        Thread.Sleep(200);
        var snap = monitor.Sample();
        Assert.True(snap.Memory.TotalBytes > 0);
        Assert.InRange(snap.Cpu.TotalPercent, 0, 100);
        Assert.True(snap.Cpu.LogicalCores > 0);
        Assert.NotEmpty(snap.TopProcesses);
    }

    [Fact]
    public void ParsesLinuxProcFiles()
    {
        var (idle, total) = LinuxCpuCounter.Parse("cpu  10 0 10 70 10 0 0 0 0 0");
        Assert.Equal(80ul, idle);
        Assert.Equal(100ul, total);
        var mem = MemoryInfo.ParseMeminfo("MemTotal:       1000 kB\nMemFree:  100 kB\nMemAvailable:   400 kB\n");
        Assert.Equal(1000ul * 1024, mem.TotalBytes);
        Assert.Equal(400ul * 1024, mem.AvailableBytes);
        Assert.Equal(60, mem.UsedPercent, 3);
    }
}
