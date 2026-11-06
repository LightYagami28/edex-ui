// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using Edex.Core.Boot;
using Edex.Core.Files;
using Edex.Core.Monitoring;
using Edex.Core.Settings;
using Edex.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Edex.Core.Tests;

[TestClass]
public class ShortcutsTests
{
    [TestMethod]
    public void NormalizeOrdersModifiers()
    {
        Assert.AreEqual("CTRL+ALT+SHIFT+C", Shortcuts.Normalize("shift + Alt+control+c"));
        Assert.AreEqual("CTRL+TAB", Shortcuts.Normalize("Ctrl+Tab"));
        Assert.AreEqual("", Shortcuts.Normalize(""));
    }

    [TestMethod]
    public void FindMatchesEnabledOnly()
    {
        var list = new[] { new Shortcut("Ctrl+K", "off", null, false), new Shortcut("ctrl+shift+c", "copy") };
        Assert.IsNull(Shortcuts.Find(list, "Ctrl+K"));
        Assert.AreEqual("copy", Shortcuts.Find(list, "Shift+Ctrl+C")?.Action);
        Assert.AreEqual("paste", Shortcuts.Find(Shortcuts.Defaults, "ctrl+shift+v")?.Action);
    }

    [TestMethod]
    public void ParseHandlesBadInput()
    {
        Assert.AreSame(Shortcuts.Defaults, Shortcuts.Parse(null));
        Assert.AreSame(Shortcuts.Defaults, Shortcuts.Parse("  "));
        Assert.AreSame(Shortcuts.Defaults, Shortcuts.Parse("{nope"));
        Assert.AreSame(Shortcuts.Defaults, Shortcuts.Parse("null"));
        var parsed = Shortcuts.Parse("""[{"keys":"Ctrl+B","action":"x","command":"ls\n"},{"keys":"","action":"y"},{"keys":"Ctrl+N","action":""}]""");
        Assert.HasCount(1, parsed);
        Assert.AreEqual("ls\n", parsed[0].Command);
        Assert.AreEqual(parsed[0], Shortcuts.Parse(Shortcuts.Serialize(parsed))[0]);
    }
}

[TestClass]
public class BootSequenceTests
{
    [TestMethod]
    public void ParseAssignsDeterministicDelays()
    {
        var text = string.Join('\n', Enumerable.Range(0, 20).Select(i => $" line {i} ")) + "\n\n";
        var lines = BootSequence.Parse(text);
        Assert.HasCount(20, lines);
        Assert.AreEqual("line 0", lines[0].Text);
        Assert.AreEqual(400, lines[0].DelayMs);
        Assert.AreEqual(180, lines[17].DelayMs);
        Assert.AreEqual(8 + 5 * 7 % 40, lines[5].DelayMs);
        Assert.AreEqual(lines.Sum(l => l.DelayMs), BootSequence.TotalMs(lines));
        Assert.IsEmpty(BootSequence.Parse(""));
    }
}

[TestClass]
public class UpdateCheckerTests
{
    private sealed class Stub(HttpStatusCode status, string body, bool fail = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail) throw new HttpRequestException("down");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [TestMethod]
    public void IsNewerComparesVersions()
    {
        Assert.IsTrue(UpdateChecker.IsNewer("3.0.0", "v3.1.0"));
        Assert.IsTrue(UpdateChecker.IsNewer("3.0", "3.0.1-beta+7"));
        Assert.IsFalse(UpdateChecker.IsNewer("3.1.0", "3.1.0"));
        Assert.IsFalse(UpdateChecker.IsNewer("3.1.0", "3.0.9"));
        Assert.IsFalse(UpdateChecker.IsNewer("x", "3.0.0"));
        Assert.IsFalse(UpdateChecker.IsNewer("3.0.0", "latest"));
    }

    [TestMethod]
    public async Task CheckReadsLatestRelease()
    {
        const string json = """{"tag_name":"v4.0.0","html_url":"https://example.test/r"}""";
        using var client = new HttpClient(new Stub(HttpStatusCode.OK, json));
        var info = await UpdateChecker.CheckAsync(client, "o/r", "3.0.0");
        Assert.AreEqual(new UpdateInfo("4.0.0", "https://example.test/r"), info);
        Assert.IsNull(await UpdateChecker.CheckAsync(client, "o/r", "4.0.0"));
    }

    [TestMethod]
    public async Task CheckReturnsNullOnErrors()
    {
        using var notFound = new HttpClient(new Stub(HttpStatusCode.NotFound, ""));
        Assert.IsNull(await UpdateChecker.CheckAsync(notFound, "o/r", "3.0.0"));
        using var broken = new HttpClient(new Stub(HttpStatusCode.OK, "{"));
        Assert.IsNull(await UpdateChecker.CheckAsync(broken, "o/r", "3.0.0"));
        using var missing = new HttpClient(new Stub(HttpStatusCode.OK, "{}"));
        Assert.IsNull(await UpdateChecker.CheckAsync(missing, "o/r", "3.0.0"));
        using var nulls = new HttpClient(new Stub(HttpStatusCode.OK, """{"tag_name":null,"html_url":null}"""));
        Assert.IsNull(await UpdateChecker.CheckAsync(nulls, "o/r", "3.0.0"));
        using var down = new HttpClient(new Stub(HttpStatusCode.OK, "", fail: true));
        Assert.IsNull(await UpdateChecker.CheckAsync(down, "o/r", "3.0.0"));
    }
}

[TestClass]
public class FuzzyFinderTests
{
    [TestMethod]
    public void ScoreRequiresOrderedLetters()
    {
        Assert.AreEqual(-1, FuzzyFinder.Score("readme.md", "mr"));
        Assert.IsGreaterThan(0, FuzzyFinder.Score("readme.md", "rd"));
        Assert.IsGreaterThan(FuzzyFinder.Score("mxaxpxp.cs", "app"), FuzzyFinder.Score("my-app.cs", "app"));
        Assert.AreEqual(0, FuzzyFinder.Score("a", ""));
    }

    [TestMethod]
    public void SearchRanksAndFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), "edex-fuzzy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "main.cs"), "");
        File.WriteAllText(Path.Combine(root, "sub", "main_test.cs"), "");
        File.WriteAllText(Path.Combine(root, ".git", "main.hidden"), "");
        File.WriteAllText(Path.Combine(root, "other.txt"), "");
        try
        {
            var found = FuzzyFinder.Search(root, "main");
            Assert.AreSequenceEqual(new[] { "main.cs", Path.Combine("sub", "main_test.cs") }, found.Select(f => f.RelativePath));
            Assert.HasCount(3, FuzzyFinder.Search(root, "main", showHidden: true));
            Assert.HasCount(1, FuzzyFinder.Search(root, "main", maxResults: 1));
            Assert.IsEmpty(FuzzyFinder.Search(Path.Combine(root, "missing"), "x"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

[TestClass]
public class HardwareInfoTests
{
    [TestMethod]
    public void ReadNeverThrows()
    {
        var sample = HardwareInfo.Read();
        Assert.IsNotNull(sample.Manufacturer);
        if (OperatingSystem.IsMacOS()) Assert.AreEqual("Apple", sample.Manufacturer);
    }

    [TestMethod]
    public void ChassisNames()
    {
        Assert.AreEqual("Desktop", HardwareInfo.ChassisName("3"));
        Assert.AreEqual("Laptop", HardwareInfo.ChassisName("10"));
        Assert.AreEqual("Tablet", HardwareInfo.ChassisName("30"));
        Assert.AreEqual("Server", HardwareInfo.ChassisName("23"));
        Assert.AreEqual("All in One", HardwareInfo.ChassisName("13"));
        Assert.AreEqual("", HardwareInfo.ChassisName(""));
        Assert.AreEqual("Other", HardwareInfo.ChassisName("99"));
        Assert.AreEqual("", HardwareInfo.Clean(null));
        Assert.AreEqual("x", HardwareInfo.Clean(" x\n"));
    }
}
