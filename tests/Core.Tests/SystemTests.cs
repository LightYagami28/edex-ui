// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Net;
using System.Text;
using Edex.Core.Files;
using Edex.Core.Geo;
using Edex.Core.Input;
using Edex.Core.Monitoring;
using Edex.Core.Pty;
using Edex.Core.Settings;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Edex.Core.Tests;

[TestClass]
public class SafeTests
{
    [TestMethod]
    public void ReturnsValueOrFallback()
    {
        Assert.AreEqual(5, Safe.Run(() => 5, -1));
        Assert.AreEqual(-1, Safe.Run<int>(() => throw new IOException(), -1));
        Assert.AreEqual(-1, Safe.Run<int>(() => throw new FormatException(), -1));
        Assert.ThrowsExactly<ArgumentNullException>(() => Safe.Run<int>(() => throw new ArgumentNullException("x"), -1));
    }
}

[TestClass]
public class MonitoringTests
{
    public TestContext TestContext { get; set; }

    private const int sampleGapMs = 30;
    private const int topProcessLimit = 3;
    private const int fullCpuPercent = 100;

    [TestMethod]
    public async Task CpuCounterSamples()
    {
        var counter = CpuCounters.Create();
        Assert.IsGreaterThanOrEqualTo(0, counter.SampleTotalPercent());
        await Task.Delay(sampleGapMs, TestContext.CancellationToken);
        Assert.IsInRange(0, fullCpuPercent, counter.SampleTotalPercent());
        Assert.IsFalse(string.IsNullOrWhiteSpace(CpuInfo.ReadModel()));
        Assert.AreEqual(0, CpuCounters.Percent(5, 0));
        Assert.AreEqual(50, CpuCounters.Percent(5, 10));
    }

    [TestMethod]
    public void LinuxCounterReadsStatFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "stat");
        File.WriteAllText(path, "cpu  100 0 100 800 0 0 0 0\ncpu0 1 1 1 1\n");
        var counter = new LinuxCpuCounter(path);
        Assert.AreEqual(0, counter.SampleTotalPercent());
        File.WriteAllText(path, "cpu  200 0 200 900 0 0 0 0\n");
        Assert.AreEqual(66.7, Math.Round(counter.SampleTotalPercent(), 1));
        File.WriteAllText(path, "intr 1 2 3\n");
        Assert.AreEqual(0, counter.SampleTotalPercent());
        File.WriteAllText(path, "cpu  a b c d\n");
        Assert.AreEqual(0, counter.SampleTotalPercent());
        Assert.AreEqual(0, new LinuxCpuCounter(Path.Combine(dir.Path, "missing")).SampleTotalPercent());
        Assert.AreEqual((800UL, 1000UL), LinuxCpuCounter.Parse("cpu 100 0 100 800"));
    }

    [TestMethod]
    public void ParsesMeminfo()
    {
        var withAvailable = MemoryInfo.ParseMeminfo("MemTotal: 1000 kB\nMemFree: 100 kB\nMemAvailable: 400 kB\nBuffers: 1 kB\nCached: 1 kB\nshort\n");
        Assert.AreEqual(1000UL * 1024, withAvailable.TotalBytes);
        Assert.AreEqual(400UL * 1024, withAvailable.AvailableBytes);
        Assert.AreEqual(600UL * 1024, withAvailable.UsedBytes);
        var without = MemoryInfo.ParseMeminfo("MemTotal: 1000 kB\nMemFree: 100 kB\nBuffers: 50 kB\nCached: 50 kB\n");
        Assert.AreEqual(200UL * 1024, without.AvailableBytes);
        Assert.AreEqual(0, new MemorySample(0, 0).UsedPercent);
        Assert.AreEqual(0UL, new MemorySample(10, 20).UsedBytes);
        Assert.AreEqual(75, new MemorySample(100, 25).UsedPercent);
        var live = MemoryInfo.Read();
        Assert.IsGreaterThan(0UL, live.TotalBytes);
    }

    [TestMethod]
    public void DisksAndMounts()
    {
        Assert.IsTrue(SystemMonitor.IsHiddenMount("/snap/core", 1));
        Assert.IsTrue(SystemMonitor.IsHiddenMount("/System/Volumes/VM", 1));
        Assert.IsFalse(SystemMonitor.IsHiddenMount("/System/Volumes/Data", 1));
        Assert.IsTrue(SystemMonitor.IsHiddenMount("/boot/efi", 1));
        Assert.IsFalse(SystemMonitor.IsHiddenMount("/boot/efi", 4L << 30));
        Assert.IsTrue(SystemMonitor.IsHiddenMount("/run/user", 1));
        Assert.IsTrue(SystemMonitor.IsHiddenMount("/dev/shm", 1));
        Assert.IsFalse(SystemMonitor.IsHiddenMount("/home", 1));
        Assert.AreEqual(0, new DiskSample("d", "/", "ext4", 0, 0).UsedPercent);
        Assert.AreEqual(25, new DiskSample("d", "/", "ext4", 100, 75).UsedPercent);
        Assert.IsNotEmpty(SystemMonitor.ReadDisks());
    }

    [TestMethod]
    public async Task SamplesTwice()
    {
        var monitor = new SystemMonitor();
        monitor.Sample();
        await Task.Delay(sampleGapMs, TestContext.CancellationToken);
        var snapshot = monitor.Sample(topProcessLimit);
        Assert.IsLessThanOrEqualTo(topProcessLimit, snapshot.TopProcesses.Count);
        Assert.IsFalse(string.IsNullOrEmpty(snapshot.HostName));
    }
}

[TestClass]
public class FilesTests
{
    [TestMethod]
    public void BrowserHandlesBadPathsAndLinks()
    {
        var bad = FileBrowser.List("bad\0path");
        Assert.IsNotNull(bad.Error);

        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(dir.Path, ".hidden"), "");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        var listing = FileBrowser.List(dir.Path);
        Assert.IsNull(listing.Error);
        Assert.AreSequenceEqual(["..", "sub", "a.txt"], listing.Entries.Select(e => e.Name));
        Assert.AreEqual(5, listing.Entries.Single(e => e.Name == "a.txt").Size);
        const int entriesWithHidden = 4;
        Assert.HasCount(entriesWithHidden, FileBrowser.List(dir.Path, showHidden: true).Entries);

        // A file is not a folder.
        Assert.IsNotNull(FileBrowser.List(Path.Combine(dir.Path, "a.txt")).Error);

        try
        {
            File.CreateSymbolicLink(Path.Combine(dir.Path, "link"), Path.Combine(dir.Path, "a.txt"));
            File.CreateSymbolicLink(Path.Combine(dir.Path, "dangling"), Path.Combine(dir.Path, "nowhere"));
            var links = FileBrowser.List(dir.Path).Entries;
            Assert.AreEqual(FileEntryKind.Link, links.Single(e => e.Name == "link").Kind);
            Assert.AreEqual(FileEntryKind.Link, links.Single(e => e.Name == "dangling").Kind);
        }
        catch (IOException) { /* no symlink privilege on this machine */ }
        catch (UnauthorizedAccessException) { /* no symlink privilege on this machine */ }
    }

    [TestMethod]
    public void BrowserRootHasNoParentEntry()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.DoesNotContain(e => e.Name == "..", FileBrowser.List(root).Entries);
    }

    [TestMethod]
    public void ExpandsHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.AreEqual(home, FileBrowser.ExpandHome("~"));
        Assert.AreEqual(Path.Combine(home, "x"), FileBrowser.ExpandHome("~/x"));
        Assert.AreEqual(Path.Combine(home, "x"), FileBrowser.ExpandHome("~" + (char)92 + "x"));
        Assert.AreEqual("/tmp", FileBrowser.ExpandHome("/tmp"));
    }

    [TestMethod]
    public void ClassifiesSpecialNames()
    {
        Assert.AreEqual(FileCategory.Code, FileTypes.Classify("Makefile", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Text, FileTypes.Classify("LICENSE", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Data, FileTypes.Classify(".env", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Link, FileTypes.Classify("thing", FileEntryKind.Link));
        Assert.AreEqual(FileCategory.Other, FileTypes.Classify("thing", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Data, FileTypes.Classify(".zzz", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Other, FileTypes.Classify(".weird.zzz", FileEntryKind.File));
        Assert.AreEqual(FileCategory.Link, FileTypes.Classify("x.zzz", FileEntryKind.Link));
        Assert.AreEqual(FileCategory.Other, FileTypes.Classify("x.zzz", FileEntryKind.File));
        Assert.AreEqual("", FileTypes.Label("noext"));
        Assert.AreEqual("", FileTypes.Label("x.verylong"));
        Assert.AreEqual("MD", FileTypes.Label("x.md"));
        Assert.IsTrue(FileTypes.IsTextLike(FileCategory.Code));
        Assert.IsFalse(FileTypes.IsTextLike(FileCategory.Folder));
    }
}

[TestClass]
public class ThemeExtraTests
{
    [TestMethod]
    public void EffectiveColorsFallBack()
    {
        var plain = ThemeLibrary.BuiltIn[0];
        Assert.AreEqual(plain.Foreground, plain.EffectiveTerminalForeground);
        Assert.AreEqual(plain.Background, plain.EffectiveTerminalBackground);
        Assert.AreEqual(plain.Accent, plain.EffectiveCursor);
        var custom = plain with { TerminalForeground = "#111111", TerminalBackground = "#222222", Cursor = "#333333" };
        Assert.AreEqual("#111111", custom.EffectiveTerminalForeground);
        Assert.AreEqual("#222222", custom.EffectiveTerminalBackground);
        Assert.AreEqual("#333333", custom.EffectiveCursor);
    }

    [TestMethod]
    public void LoadAndFind()
    {
        Assert.HasCount(ThemeLibrary.BuiltIn.Count, ThemeLibrary.Load(null));
        Assert.HasCount(ThemeLibrary.BuiltIn.Count, ThemeLibrary.Load("/does/not/exist"));
        Assert.IsNull(ThemeLibrary.TryParse("null", "x"));
        Assert.AreEqual("fallback", ThemeLibrary.TryParse(ThemeLibrary.Serialize(ThemeLibrary.BuiltIn[0] with { Name = " " }), "fallback")?.Name);
        Assert.IsNull(ThemeLibrary.TryParse(ThemeLibrary.Serialize(ThemeLibrary.BuiltIn[0] with { Ansi = ["#000000"] }), "x"));
        Assert.IsNull(ThemeLibrary.TryParse("{", "x"));
        Assert.AreEqual("amber", ThemeLibrary.Find(ThemeLibrary.BuiltIn, "AMBER").Name);
        Assert.AreEqual("tron", ThemeLibrary.Find(ThemeLibrary.BuiltIn, "nope").Name);
        Assert.IsFalse(ColorUtil.IsValidHex(null));
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), ColorUtil.Parse("bad"));
        Assert.AreEqual(((byte)0, (byte)0, (byte)0), ColorUtil.Xterm256(3));
        Assert.AreEqual(((byte)0, (byte)0, (byte)95), ColorUtil.Xterm256(17));
    }
}

[TestClass]
public class TerminalExtraTests
{
    [TestMethod]
    public void CellPositionOrdering()
    {
        var a = new CellPosition(1, 1);
        var b = new CellPosition(2, 1);
        var c = new CellPosition(0, 2);
        Assert.IsTrue(a < b && b < c);
        Assert.IsTrue(c > b && b > a);
        Assert.IsTrue(a <= new CellPosition(1, 1) && a >= new CellPosition(1, 1));
        Assert.IsTrue(a <= b && c >= b);
        Assert.IsFalse(a > b);
        var selection = new TextSelection(c, a);
        Assert.AreEqual(a, selection.Start);
        Assert.AreEqual(c, selection.End);
        Assert.IsFalse(selection.IsEmpty);
        Assert.IsTrue(new TextSelection(a, a).IsEmpty);
        Assert.IsTrue(selection.Contains(5, 1));
        Assert.IsFalse(selection.Contains(0, 0));
    }

    [TestMethod]
    public void SelectionWordsAndText()
    {
        var buffer = new TerminalBuffer(12, 3);
        buffer.Feed("ab $$ cd/e\r\nxyz");
        var snapshot = buffer.Snapshot();
        Assert.AreEqual("ab", TextSelection.Word(snapshot, new CellPosition(0, 0)).GetText(snapshot));
        Assert.AreEqual("$", TextSelection.Word(snapshot, new CellPosition(4, 0)).GetText(snapshot));
        Assert.AreEqual("cd/e", TextSelection.Word(snapshot, new CellPosition(8, 0)).GetText(snapshot));
        var blank = TextSelection.Word(snapshot, new CellPosition(11, 0));
        Assert.AreEqual(11, blank.End.Column);
        var outside = TextSelection.Word(snapshot, new CellPosition(3, 9));
        Assert.IsEmpty(outside.GetText(snapshot));
        Assert.AreEqual("\nab $$ cd/e\nxyz\n", new TextSelection(new CellPosition(-3, -2), new CellPosition(40, 5)).GetText(snapshot));
        Assert.AreEqual("xyz", TextSelection.Line(snapshot, 1).GetText(snapshot));
    }

    [TestMethod]
    public void MouseEdgeCases()
    {
        Assert.AreEqual("\x1b[<3;1;1m", MouseEncoder.Encode(MouseTrackingMode.AnyEvent, true, MouseEventKind.Release, MouseButton.None, 0, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Press, MouseButton.Left, 300, 0));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.Normal, false, MouseEventKind.Press, MouseButton.Left, 0, 300));
        Assert.IsNull(MouseEncoder.Encode(MouseTrackingMode.Normal, true, MouseEventKind.Release, MouseButton.WheelUp, 0, 0));
    }
}

[TestClass]
public class KeyboardExtraTests
{
    private static KeyDefinition K(KeyAction action) => new("k", null, action);

    [TestMethod]
    public void EveryActionProducesOutput()
    {
        var state = new KeyboardState();
        var expected = new Dictionary<KeyAction, string>
        {
            [KeyAction.Space] = " ", [KeyAction.Enter] = "\r", [KeyAction.Backspace] = "\x7f", [KeyAction.Tab] = "\t",
            [KeyAction.Escape] = "\x1b", [KeyAction.Delete] = "\x1b[3~", [KeyAction.Up] = "\x1b[A", [KeyAction.Down] = "\x1b[B",
            [KeyAction.Right] = "\x1b[C", [KeyAction.Left] = "\x1b[D", [KeyAction.Home] = "\x1b[H", [KeyAction.End] = "\x1b[F",
            [KeyAction.PageUp] = "\x1b[5~", [KeyAction.PageDown] = "\x1b[6~",
        };
        foreach (var (action, text) in expected)
        {
            Assert.AreEqual(text, state.Press(K(action)), action.ToString());
        }

        Assert.AreEqual("\x1bOA", state.Press(K(KeyAction.Up), applicationCursorKeys: true));
        Assert.AreEqual("", state.Press(new KeyDefinition("?", null, (KeyAction)99)));

        state.Press(K(KeyAction.Shift));
        Assert.AreEqual("\x1b[Z", state.Press(K(KeyAction.Tab)));
        state.Press(K(KeyAction.Alt));
        Assert.AreEqual("\x1b ", state.Press(K(KeyAction.Space)));
        state.Press(K(KeyAction.Alt));
        Assert.AreEqual("\x1b\r", state.Press(K(KeyAction.Enter)));
        state.Press(K(KeyAction.Alt));
        Assert.AreEqual("\x1b\x7f", state.Press(K(KeyAction.Backspace)));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\0", state.Press(K(KeyAction.Space)));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\x08", state.Press(K(KeyAction.Backspace)));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\x1b[1;5A", state.Press(K(KeyAction.Up)));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\x1b[3;5~", state.Press(K(KeyAction.Delete)));
    }

    [TestMethod]
    public void CharactersWithModifiers()
    {
        var state = new KeyboardState();
        var letter = new KeyDefinition("a", null);
        var digit = new KeyDefinition("1", "!");
        var multi = new KeyDefinition("ab", null);
        Assert.AreEqual("a", state.Press(letter));
        state.Press(K(KeyAction.CapsLock));
        Assert.AreEqual("A", state.Press(letter));
        Assert.AreEqual("1", state.Press(digit));
        state.Press(K(KeyAction.Shift));
        Assert.AreEqual("a", state.Press(letter));
        state.Press(K(KeyAction.Shift));
        Assert.AreEqual("!", state.Press(digit));
        state.Press(K(KeyAction.CapsLock));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\x01", state.Press(letter));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("1", state.Press(digit));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("\x7f", state.Press(new KeyDefinition("?", null)));
        state.Press(K(KeyAction.Control));
        Assert.AreEqual("ab", state.Press(multi));
        state.Press(K(KeyAction.Alt));
        Assert.AreEqual("\x1b" + "a", state.Press(letter));
        Assert.AreEqual("Shift", state.Display(new KeyDefinition("Shift", null, KeyAction.Shift)));
        Assert.IsTrue(K(KeyAction.Shift).IsModifier);
        Assert.IsFalse(K(KeyAction.Enter).IsModifier);
    }

    [TestMethod]
    public void FindsLayoutsByNameAndCulture()
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            Assert.AreSame(KeyboardLayout.ItIt, KeyboardLayout.Find("IT-it"));
            CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
            Assert.AreSame(KeyboardLayout.ItIt, KeyboardLayout.Find("auto"));
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.AreSame(KeyboardLayout.EnUs, KeyboardLayout.Find(null));
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            Assert.AreEqual("de-DE", KeyboardLayout.Find("auto").Name);
            CultureInfo.CurrentUICulture = new CultureInfo("it-CH");
            Assert.AreSame(KeyboardLayout.ItIt, KeyboardLayout.Find("auto"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }
}

[TestClass]
public class SettingsExtraTests
{
    [TestMethod]
    public void DefaultShellFromEnvironment()
    {
        if (OperatingSystem.IsWindows())
        {
            using var dir = new TempDir();
            Assert.AreEqual("powershell.exe", AppSettings.DefaultShell(null, null));
            Assert.AreEqual("powershell.exe", AppSettings.DefaultShell(dir.Path, null));
            File.WriteAllText(Path.Combine(dir.Path, "pwsh.exe"), "");
            Assert.AreEqual("pwsh.exe", AppSettings.DefaultShell(dir.Path, null));
        }
        else
        {
            Assert.AreEqual("/bin/custom", AppSettings.DefaultShell(null, "/bin/custom"));
            Assert.AreEqual(OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash", AppSettings.DefaultShell(null, " "));
        }
        Assert.IsFalse(string.IsNullOrEmpty(AppSettings.DefaultShell()));
    }

    [TestMethod]
    public void StoreUsesEnvironmentDirectoryAndSurvivesBadFiles()
    {
        using var dir = new TempDir();
        var previous = Environment.GetEnvironmentVariable("EDEX_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("EDEX_CONFIG_DIR", dir.Path);
            Assert.AreEqual(dir.Path, new SettingsStore().Directory);
            Environment.SetEnvironmentVariable("EDEX_CONFIG_DIR", null);
            Assert.IsTrue(new SettingsStore().Directory.EndsWith("eDEX-UI-Next", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDEX_CONFIG_DIR", previous);
        }
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, "null");
        Assert.AreEqual("tron", store.Load().Theme);
        File.Delete(store.FilePath);
        Directory.CreateDirectory(store.FilePath);
        Assert.AreEqual("tron", store.Load().Theme);
    }
}

[TestClass]
public class GeoLocatorTests
{
    private sealed class Routes(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!.Host));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Good = """{"latitude":45.4,"longitude":9.2,"city":"Milano","country":"Italy","country_name":"Italy","ip":"1.2.3.4"}""";

    [TestMethod]
    public async Task FirstServiceWins()
    {
        using var http = new HttpClient(new Routes(_ => Json(Good)));
        var location = await GeoLocator.LocateAsync(http, CancellationToken.None);
        Assert.AreEqual("ipwho.is", location?.Source);
    }

    [TestMethod]
    public async Task FallsBackToSecondService()
    {
        using var http = new HttpClient(new Routes(host => host == "ipwho.is" ? Json("""{"success":false}""") : Json(Good)));
        Assert.AreEqual("ipapi.co", (await GeoLocator.LocateAsync(http, CancellationToken.None))?.Source);

        using var broken = new HttpClient(new Routes(host => host == "ipwho.is" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Json(Good)));
        Assert.AreEqual("ipapi.co", (await GeoLocator.LocateAsync(broken, CancellationToken.None))?.Source);
    }

    [TestMethod]
    public async Task ReturnsNullWhenNothingWorks()
    {
        using var http = new HttpClient(new Routes(_ => Json("{")));
        Assert.IsNull(await GeoLocator.LocateAsync(http, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.IsNull(await GeoLocator.LocateAsync(http, cancelled.Token));
    }

    [TestMethod]
    public void ParsersRejectBadData()
    {
        static System.Text.Json.JsonElement P(string json) => System.Text.Json.JsonElement.Parse(json);
        Assert.IsNull(GeoLocator.ParseIpApiCo(P("""{"error":true}""")));
        Assert.IsNull(GeoLocator.ParseIpWhoIs(P("{}")));
        Assert.IsNull(GeoLocator.ParseIpWhoIs(P("""{"latitude":"x","longitude":1}""")));
        Assert.IsNull(GeoLocator.ParseIpWhoIs(P("""{"latitude":95,"longitude":1}""")));
        Assert.IsNull(GeoLocator.ParseIpWhoIs(P("""{"latitude":1,"longitude":181}""")));
        Assert.IsNotNull(GeoLocator.ParseIpWhoIs(P("""{"latitude":1,"longitude":1}""")));
    }
}

[TestClass]
public class PtyExtraTests
{
    public TestContext TestContext { get; set; }

    private sealed class Pipe : Stream
    {
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _chunks = [];
        public bool FailReads { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public List<byte> Written { get; } = [];
        public bool FailWrites { get; set; }
        public bool DisposedFailure { get; set; }

        public void Push(string text) => _chunks.Add(Encoding.UTF8.GetBytes(text));
        public void Complete() => _chunks.CompleteAdding();
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (FailReads)
            {
                throw new IOException("eio");
            }

            ObjectDisposedException.ThrowIf(DisposedFailure, this);
            if (!_chunks.TryTake(out var chunk, Timeout.Infinite))
            {
                return 0;
            }

            chunk.CopyTo(buffer, offset);
            return chunk.Length;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailWrites)
            {
                throw new IOException("closed");
            }

            Written.AddRange(buffer.AsSpan(offset, count).ToArray());
        }
    }

    private sealed class FakePty(int? exitCode = null) : IPtyConnection
    {
        public Pipe Out { get; } = new();
        public Pipe In { get; } = new();
        public int ResizedTo { get; private set; }
        public bool Disposed { get; private set; }
        public int ProcessId => 42;
        public Stream Output => Out;
        public Stream Input => In;
        public int? ExitCode { get; } = exitCode;
        public event EventHandler<int>? Exited;
        public void Resize(int columns, int rows) => ResizedTo = columns * 1000 + rows;
        public void Dispose() => Disposed = true;
        public void RaiseExit(int code) => Exited?.Invoke(this, code);
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.IsTrue(condition());
    }

    [TestMethod]
    public void SessionPumpsOutputAndInput()
    {
        var pty = new FakePty();
        var updates = 0;
        using var session = TerminalSession.Attach(pty, "sh", new TerminalBuffer(20, 4));
        session.Updated += (_, _) => Interlocked.Increment(ref updates);
        Assert.AreEqual(42, session.ProcessId);
        Assert.AreEqual("sh", session.Shell);

        pty.Out.Push("hello");
        WaitFor(() => session.Buffer.Snapshot().LineText(0) == "hello");
        Assert.IsGreaterThan(0, updates);

        session.Send("");
        session.Send("ls\r");
        Assert.AreEqual("ls\r", Encoding.UTF8.GetString(pty.In.Written.ToArray()));
        session.Paste("a\r\nb\nc");
        Assert.IsTrue(Encoding.UTF8.GetString(pty.In.Written.ToArray()).EndsWith("a\rb\rc", StringComparison.Ordinal));
        session.Buffer.Feed("\x1b[?2004h");
        session.Paste("x");
        Assert.IsTrue(Encoding.UTF8.GetString(pty.In.Written.ToArray()).EndsWith("\x1b[200~x\x1b[201~", StringComparison.Ordinal));

        session.Resize(20, 4);
        Assert.AreEqual(0, pty.ResizedTo);
        session.Resize(30, 5);
        Assert.AreEqual(30005, pty.ResizedTo);
        Assert.AreEqual(30, session.Buffer.Columns);

        pty.In.FailWrites = true;
        session.Send("ignored");
        pty.In.FailWrites = false;

        int? exitCode = null;
        session.Exited += (_, code) => exitCode = code;
        pty.RaiseExit(3);
        pty.RaiseExit(4);
        Assert.AreEqual(3, exitCode);
        Assert.AreEqual(3, session.ExitCode);
        Assert.IsTrue(session.HasExited);
        var before = pty.In.Written.Count;
        session.Send("after exit");
        Assert.HasCount(before, pty.In.Written);

        pty.Out.Complete();
        session.Dispose();
        session.Dispose();
        Assert.IsTrue(pty.Disposed);
        session.Send("after dispose");
    }

    [TestMethod]
    public async Task SessionSurvivesBrokenReadsAndEarlyExit()
    {
        var failing = new FakePty(exitCode: 9);
        failing.Out.FailReads = true;
        using (var session = TerminalSession.Attach(failing, "sh", new TerminalBuffer(10, 2)))
        {
            Assert.AreEqual(9, session.ExitCode);
            await Task.Delay(50, TestContext.CancellationToken);
        }

        var disposed = new FakePty();
        disposed.Out.DisposedFailure = true;
        using var second = TerminalSession.Attach(disposed, "sh", new TerminalBuffer(10, 2));
        await Task.Delay(50, TestContext.CancellationToken);
        Assert.IsFalse(second.HasExited);
    }

    [TestMethod]
    public void EnvironmentIncludesTerminalVariables()
    {
        var env = PtyFactory.BuildEnvironment(new Dictionary<string, string> { ["EDEX_EXTRA"] = "1" });
        Assert.AreEqual("xterm-256color", env["TERM"]);
        Assert.AreEqual("1", env["EDEX_EXTRA"]);
        Assert.IsFalse(PtyFactory.BuildEnvironment(null).ContainsKey("EDEX_EXTRA"));
        Assert.ThrowsExactly<ArgumentException>(() => PtyFactory.Spawn(new PtyOptions { Shell = " " }));
    }

    [TestMethod]
    public void InteractiveShellRoundTrip()
    {
        var options = OperatingSystem.IsWindows()
            ? new PtyOptions { Shell = "cmd.exe", Columns = 100, Rows = 20, WorkingDirectory = Path.GetTempPath(), Environment = new Dictionary<string, string> { ["EDEX_T"] = "yes" } }
            : new PtyOptions { Shell = "sh", Columns = 100, Rows = 20, WorkingDirectory = Path.GetTempPath(), Environment = new Dictionary<string, string> { ["EDEX_T"] = "yes" } };
        using var session = TerminalSession.Start(options);
        Assert.IsGreaterThan(0, session.ProcessId);
        session.Send(OperatingSystem.IsWindows() ? "echo %EDEX_T%-ok\r" : "echo $EDEX_T-ok\r");
        WaitFor(() => session.Buffer.ScrollbackText().Contains("yes-ok", StringComparison.Ordinal));
        session.Resize(90, 15);
        session.Send("exit\r");
        WaitFor(() => session.HasExited);
    }

    [TestMethod]
    public void MissingProgramFails()
    {
        var name = "edex-no-such-program-" + Guid.NewGuid().ToString("N");
        if (OperatingSystem.IsWindows())
        {
            Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => PtyFactory.Spawn(new PtyOptions { Shell = name }));
        }
        else
        {
            using var session = TerminalSession.Start(new PtyOptions { Shell = name, WorkingDirectory = "/does/not/exist" });
            WaitFor(() => session.HasExited);
            Assert.AreEqual(127, session.ExitCode);
        }
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public void ResolvesExecutablesOnPath()
    {
        Assert.AreEqual("/bin/sh", UnixPty.ResolveExecutable("/bin/sh"));
        Assert.EndsWith("/sh", UnixPty.ResolveExecutable("sh"));
        Assert.AreEqual("edex-missing-binary", UnixPty.ResolveExecutable("edex-missing-binary"));
    }
}

[TestClass]
public class MacSysctlTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public void ReadsKeysOnMacOnly()
    {
        Assert.IsGreaterThan(0UL, MacSysctl.ReadUInt64("hw.memsize"));
        Assert.AreNotEqual("", MacSysctl.ReadString("machdep.cpu.brand_string"));
        Assert.AreEqual(0UL, MacSysctl.ReadUInt64("edex.no.such.key"));
        Assert.AreEqual("", MacSysctl.ReadString("edex.no.such.key"));
    }
}
