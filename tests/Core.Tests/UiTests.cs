// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Files;
using Edex.Core.Geo;
using Edex.Core.Input;
using Edex.Core.Monitoring;
using Edex.Core.Networking;
using Edex.Core.Settings;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Edex.Core.Ui;

namespace Edex.Core.Tests;

[TestClass]
public sealed class UiRectTests
{
    [TestMethod]
    public void ContainsIsExclusiveOnFarEdges()
    {
        var rect = new UiRect(10, 20, 30, 40);
        Assert.IsTrue(rect.Contains(10, 20));
        Assert.IsTrue(rect.Contains(39.5, 59.5));
        Assert.IsFalse(rect.Contains(40, 60));
        Assert.IsFalse(rect.Contains(9.9, 20));
    }

    [TestMethod]
    public void EmptyRectContainsNothing()
    {
        Assert.IsTrue(UiRect.Empty.IsEmpty);
        Assert.IsFalse(UiRect.Empty.Contains(0, 0));
        Assert.IsFalse(new UiRect(0, 0, 0, 10).Contains(0, 0));
    }

    [TestMethod]
    public void InsetShrinksOnBothSidesAndClampsAtZero()
    {
        var rect = new UiRect(0, 0, 100, 50);
        Assert.AreEqual(new UiRect(5, 5, 90, 40), rect.Inset(5));
        Assert.AreEqual(new UiRect(2, 3, 96, 44), rect.Inset(2, 3));
        Assert.IsTrue(new UiRect(0, 0, 4, 4).Inset(10).IsEmpty);
    }

    [TestMethod]
    public void SliceTopAndBelowSplitVertically()
    {
        var rect = new UiRect(0, 0, 100, 200);
        var top = rect.SliceTop(60);
        Assert.AreEqual(60, top.Height);
        var rest = rect.Below(top, 10);
        Assert.AreEqual(70, rest.Y);
        Assert.AreEqual(130, rest.Height);
    }

    [TestMethod]
    public void SliceTopClampsToAvailableHeight()
    {
        var rect = new UiRect(0, 0, 10, 20);
        Assert.AreEqual(20, rect.SliceTop(500).Height);
        Assert.AreEqual(0, rect.SliceTop(-5).Height);
    }

    [TestMethod]
    public void BelowOfTallerRectIsEmpty()
    {
        var rect = new UiRect(0, 0, 10, 20);
        Assert.IsTrue(rect.Below(new UiRect(0, 30, 10, 5), 0).IsEmpty);
    }

    [TestMethod]
    public void ColumnAndRowDivideSpaceWithGaps()
    {
        var rect = new UiRect(0, 0, 100, 90);
        var first = rect.Column(0, 2, 10);
        var second = rect.Column(1, 2, 10);
        Assert.AreEqual(45, first.Width);
        Assert.AreEqual(55, second.X);
        Assert.AreEqual(45, second.Width);
        Assert.AreEqual(40, rect.Row(0, 2, 10).Height);
        Assert.AreEqual(50, rect.Row(1, 2, 10).Y);
    }

    [TestMethod]
    public void ColumnAndRowClampOutOfRangeAndRejectZeroCount()
    {
        var rect = new UiRect(0, 0, 100, 100);
        Assert.AreEqual(rect.Column(1, 2, 0), rect.Column(9, 2, 0));
        Assert.AreEqual(rect.Row(-3, 2, 0), rect.Row(0, 2, 0));
        Assert.IsTrue(rect.Column(0, 0, 0).IsEmpty);
        Assert.IsTrue(rect.Row(0, 0, 0).IsEmpty);
    }
}

[TestClass]
public sealed class UiLayoutTests
{
    [TestMethod]
    public void FullSizeWindowShowsEveryPanel()
    {
        var layout = UiLayout.Compute(1600, 900, showKeyboard: true);
        foreach (var panel in Enum.GetValues<UiPanel>())
        {
            if (panel != UiPanel.None)
            {
                Assert.IsTrue(layout.IsVisible(panel), $"pannello mancante: {panel}");
            }
        }

        Assert.AreEqual(9, layout.VisiblePanels.Count);
    }

    [TestMethod]
    public void PanelsStayInsideTheWindowAndDoNotOverlapTheTerminal()
    {
        var layout = UiLayout.Compute(1440, 900, showKeyboard: true);
        var terminal = layout.Rect(UiPanel.Terminal);
        foreach (var panel in layout.VisiblePanels)
        {
            var rect = layout.Rect(panel);
            Assert.IsTrue(rect.X >= 0 && rect.Y >= 0);
            Assert.IsTrue(rect.Right <= layout.Width, $"{panel} esce a destra");
            Assert.IsTrue(rect.Bottom <= layout.Height, $"{panel} esce in basso");
            if (panel == UiPanel.Terminal)
            {
                continue;
            }

            Assert.IsFalse(Overlaps(rect, terminal), $"{panel} si sovrappone al terminale");
        }
    }

    [TestMethod]
    public void RightColumnStacksFivePanelsTopToBottom()
    {
        var layout = UiLayout.Compute(1600, 900, showKeyboard: true);
        double previousBottom = double.MinValue;
        foreach (var panel in new[] { UiPanel.Cpu, UiPanel.Memory, UiPanel.Network, UiPanel.Processes, UiPanel.Globe })
        {
            var rect = layout.Rect(panel);
            Assert.IsTrue(rect.Y > previousBottom, $"{panel} non è sotto il precedente");
            previousBottom = rect.Bottom;
        }
    }

    [TestMethod]
    public void NarrowWindowDropsSideColumnsAndKeepsTerminal()
    {
        var layout = UiLayout.Compute(420, 700, showKeyboard: true);
        Assert.IsFalse(layout.IsVisible(UiPanel.Files));
        Assert.IsFalse(layout.IsVisible(UiPanel.Keyboard));
        Assert.IsFalse(layout.IsVisible(UiPanel.Globe));
        Assert.IsTrue(layout.IsVisible(UiPanel.Terminal));
        Assert.IsTrue(layout.IsVisible(UiPanel.TopBar));
    }

    [TestMethod]
    public void ShortWindowKeepsOnlyTheTopBar()
    {
        var layout = UiLayout.Compute(1600, 90, showKeyboard: true);
        Assert.IsTrue(layout.IsVisible(UiPanel.TopBar));
        Assert.IsFalse(layout.IsVisible(UiPanel.Terminal));
    }

    [TestMethod]
    public void DegenerateSizesProduceNoPanels()
    {
        Assert.AreEqual(0, UiLayout.Compute(0, 0, true).VisiblePanels.Count);
        Assert.AreEqual(0, UiLayout.Compute(-100, -100, true).VisiblePanels.Count);
        Assert.AreEqual(0, UiLayout.Compute(10, 10, true).VisiblePanels.Count);
        Assert.AreEqual(0, UiLayout.Compute(double.NaN, 900, true).VisiblePanels.Count);
        Assert.AreEqual(0, UiLayout.Compute(1600, double.PositiveInfinity, true).VisiblePanels.Count);
    }

    [TestMethod]
    public void HiddenKeyboardGivesItsSpaceToFiles()
    {
        var withKeyboard = UiLayout.Compute(1600, 900, showKeyboard: true);
        var without = UiLayout.Compute(1600, 900, showKeyboard: false);
        Assert.IsTrue(withKeyboard.IsVisible(UiPanel.Keyboard));
        Assert.IsFalse(without.IsVisible(UiPanel.Keyboard));
        Assert.IsTrue(without.Rect(UiPanel.Files).Height > withKeyboard.Rect(UiPanel.Files).Height);
        Assert.AreEqual(withKeyboard.Rect(UiPanel.Files).X, without.Rect(UiPanel.Files).X);
    }

    [TestMethod]
    public void UnknownPanelIsEmpty()
    {
        Assert.IsTrue(UiLayout.Compute(1600, 900, true).Rect(UiPanel.None).IsEmpty);
    }

    private static bool Overlaps(UiRect a, UiRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}

[TestClass]
public sealed class UiSceneTests
{
    [TestMethod]
    public void ClearEmptiesTheSceneAndAddAppends()
    {
        var scene = new UiScene();
        Assert.AreEqual(0, scene.Count);
        scene.Text(new UiRect(0, 0, 10, 10), "a", UiBrush.Foreground, 12);
        scene.Rule(new UiRect(0, 0, 10, 1), UiBrush.Muted);
        Assert.AreEqual(2, scene.Count);
        scene.Clear();
        Assert.AreEqual(0, scene.Count);
    }

    [TestMethod]
    public void BarClampsOutOfRangeAndNonFiniteFractions()
    {
        var scene = new UiScene();
        scene.Bar(new UiRect(0, 0, 10, 10), 1.8, UiBrush.Accent);
        scene.Bar(new UiRect(0, 0, 10, 10), -0.4, UiBrush.Accent);
        scene.Bar(new UiRect(0, 0, 10, 10), double.NaN, UiBrush.Accent);
        Assert.AreEqual(1, scene.Elements[0].Value);
        Assert.AreEqual(0, scene.Elements[1].Value);
        Assert.AreEqual(0, scene.Elements[2].Value);
    }

    [TestMethod]
    public void HitTestReturnsTheTopmostElement()
    {
        var scene = new UiScene();
        var bounds = new UiRect(0, 0, 100, 100);
        scene.Add(new UiElement { Kind = UiElementKind.Frame, Bounds = bounds, Text = "sotto", Panel = UiPanel.Cpu });
        scene.Add(new UiElement { Kind = UiElementKind.Text, Bounds = bounds, Text = "sopra", Panel = UiPanel.Memory });
        var hit = scene.HitTest(50, 50);
        Assert.IsNotNull(hit);
        Assert.AreEqual("sopra", hit.Value.Text);
        Assert.AreEqual(UiPanel.Memory, hit.Value.Panel);
    }

    [TestMethod]
    public void HitTestSkipsRulesAndMissesOutside()
    {
        var scene = new UiScene();
        scene.Rule(new UiRect(0, 0, 100, 100), UiBrush.Muted);
        Assert.IsNull(scene.HitTest(10, 10));
        scene.Frame(new UiRect(0, 0, 100, 100), UiPanel.Globe, "g");
        Assert.IsNull(scene.HitTest(150, 150));
        Assert.IsNotNull(scene.HitTest(10, 10));
    }

    [TestMethod]
    public void BuildersCarryTheirPayloadAndPanel()
    {
        var scene = new UiScene();
        var bounds = new UiRect(0, 0, 10, 10);
        double[] samples = [0.1, 0.9];
        scene.Polyline(bounds, samples, UiBrush.Accent, UiPanel.Network);
        scene.Cells(bounds, "snapshot");
        scene.Keys(bounds, "keys");
        scene.Globe(bounds, "globe");
        Assert.AreSame(samples, scene.Elements[0].Payload);
        Assert.AreEqual(UiPanel.Network, scene.Elements[0].Panel);
        Assert.AreEqual(UiElementKind.Cells, scene.Elements[1].Kind);
        Assert.AreEqual(UiPanel.Terminal, scene.Elements[1].Panel);
        Assert.AreEqual(UiPanel.Keyboard, scene.Elements[2].Panel);
        Assert.AreEqual(UiPanel.Globe, scene.Elements[3].Panel);
    }
}

[TestClass]
public sealed class UiKeyboardTests
{
    private static KeyboardLayout TwoRows() => new(
        "test",
        [
            [new KeyDefinition("q"), new KeyDefinition("w")],
            [new KeyDefinition("shift", null, KeyAction.Shift, 2), new KeyDefinition("a")],
        ]);

    [TestMethod]
    public void ArrangeProducesOneEntryPerKeyWithProportionalWidths()
    {
        var keys = UiKeyboard.Arrange(TwoRows(), new UiRect(0, 0, 300, 100), new KeyboardState());
        Assert.AreEqual(4, keys.Count);
        Assert.AreEqual(keys[0].Bounds.Width, keys[1].Bounds.Width);
        Assert.IsTrue(keys[2].Bounds.Width > keys[3].Bounds.Width);
        Assert.IsTrue(keys[2].Bounds.Y > keys[0].Bounds.Y);
    }

    [TestMethod]
    public void ArrangeOnEmptyBoundsOrWithoutRowsGivesNothing()
    {
        Assert.AreEqual(0, UiKeyboard.Arrange(TwoRows(), UiRect.Empty, new KeyboardState()).Count);
        Assert.AreEqual(0, UiKeyboard.Arrange(new KeyboardLayout("vuota", []), new UiRect(0, 0, 100, 100), new KeyboardState()).Count);
        var withEmptyRow = new KeyboardLayout("mista", [[], [new KeyDefinition("x")]]);
        Assert.AreEqual(1, UiKeyboard.Arrange(withEmptyRow, new UiRect(0, 0, 100, 100), new KeyboardState()).Count);
    }

    [TestMethod]
    public void ModifierKeysAreReportedActive()
    {
        var state = new KeyboardState();
        var layout = TwoRows();
        Assert.IsFalse(UiKeyboard.Arrange(layout, new UiRect(0, 0, 300, 100), state)[2].Active);
        state.Press(layout.Rows[1][0]);
        Assert.IsTrue(UiKeyboard.Arrange(layout, new UiRect(0, 0, 300, 100), state)[2].Active);
    }

    [TestMethod]
    public void HitTestFindsTheKeyUnderThePointer()
    {
        var keys = UiKeyboard.Arrange(TwoRows(), new UiRect(0, 0, 300, 100), new KeyboardState());
        var target = keys[3].Bounds;
        Assert.AreEqual(3, UiKeyboard.HitTest(keys, target.X + 1, target.Y + 1));
        Assert.AreEqual(-1, UiKeyboard.HitTest(keys, 5000, 5000));
        Assert.AreEqual(-1, UiKeyboard.HitTest([], 5, 5));
    }
}

[TestClass]
public sealed class DashboardTests
{
    private const double Width = 1600;
    private const double Height = 900;

    private static readonly double[] ExpectedCpuRamp = [0.1, 0.2, 0.3];

    private static Dashboard NewDashboard(bool showKeyboard = true)
    {
        var settings = new AppSettings { ShowKeyboard = showKeyboard, FontSize = 14 };
        var dashboard = new Dashboard(settings, ThemeLibrary.BuiltIn[0], KeyboardLayout.EnUs, new KeyboardState());
        dashboard.Resize(Width, Height);
        return dashboard;
    }

    private static SystemSnapshot Snapshot(double cpuPercent, ulong usedBytes, ulong totalBytes) => new(
        DateTimeOffset.UnixEpoch,
        "host",
        "OS descrizione",
        "X64",
        new TimeSpan(1, 2, 3, 4),
        new CpuSample(cpuPercent, 8, "cpu modello"),
        new MemorySample(totalBytes, totalBytes - usedBytes),
        [new ProcessSample(1234, "processo", 12.5, 2048)],
        [new DiskSample("sda", "/", "ext4", 1000, 400)]);

    private static NetworkSnapshot Network(bool online, double? ping) => new(
        online,
        "eth0",
        "192.0.2.10",
        "192.0.2.1",
        "aa:bb:cc:dd:ee:ff",
        2048,
        512,
        10_000,
        4_000,
        ping,
        7);

    private static TerminalSnapshot Terminal()
    {
        var buffer = new TerminalBuffer(20, 5);
        buffer.Feed("riga uno\r\nriga due");
        return buffer.Snapshot();
    }

    [TestMethod]
    public void ResizeRecomputesTheLayout()
    {
        var dashboard = NewDashboard();
        Assert.AreEqual(Width, dashboard.Layout.Width);
        dashboard.Resize(400, 700);
        Assert.AreEqual(400, dashboard.Layout.Width);
        Assert.IsFalse(dashboard.Layout.IsVisible(UiPanel.Globe));
    }

    [TestMethod]
    public void BuildSceneFramesEveryVisiblePanel()
    {
        var dashboard = NewDashboard();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var framed = scene.Elements.Where(e => e.Kind == UiElementKind.Frame).Select(e => e.Panel).ToHashSet();
        foreach (var panel in dashboard.Layout.VisiblePanels)
        {
            Assert.IsTrue(framed.Contains(panel), $"nessuna cornice per {panel}");
        }

        Assert.IsTrue(scene.Count > framed.Count);
    }

    [TestMethod]
    public void BuildSceneIsReusableAndDoesNotAccumulate()
    {
        var dashboard = NewDashboard();
        var first = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        int count = first.Count;
        var second = dashboard.BuildScene(DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.AreSame(first, second);
        Assert.AreEqual(count, second.Count);
    }

    [TestMethod]
    public void HiddenPanelsProduceNothing()
    {
        var dashboard = NewDashboard(showKeyboard: false);
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsFalse(scene.Elements.Any(e => e.Panel == UiPanel.Keyboard));
        Assert.AreEqual(0, dashboard.Keys.Count);
    }

    [TestMethod]
    public void TopBarShowsHostClockAndUptime()
    {
        var dashboard = NewDashboard();
        dashboard.AttachSystem(Snapshot(25, 1024, 4096));
        var scene = dashboard.BuildScene(new DateTimeOffset(2026, 11, 14, 9, 8, 7, TimeSpan.Zero));
        var texts = scene.Elements.Where(e => e.Panel == UiPanel.TopBar && e.Kind == UiElementKind.Text).Select(e => e.Text).ToList();
        Assert.IsTrue(texts.Any(t => t.Contains("host", StringComparison.Ordinal)));
        Assert.IsTrue(texts.Any(t => t.Contains("09:08:07", StringComparison.Ordinal)));
        Assert.IsTrue(texts.Any(t => t.Contains("1g", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TopBarFallsBackToMachineNameWithoutSnapshot()
    {
        var dashboard = NewDashboard();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var texts = scene.Elements.Where(e => e.Panel == UiPanel.TopBar && e.Kind == UiElementKind.Text).Select(e => e.Text).ToList();
        Assert.IsTrue(texts.Any(t => t.Contains(Environment.MachineName, StringComparison.Ordinal)));
        Assert.IsTrue(texts.Any(t => t.Contains("00:00:00", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TerminalSnapshotBecomesACellsElement()
    {
        var dashboard = NewDashboard();
        dashboard.AttachTerminal(Terminal());
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var cells = scene.Elements.Single(e => e.Kind == UiElementKind.Cells);
        Assert.AreEqual(UiPanel.Terminal, cells.Panel);
        Assert.IsInstanceOfType<TerminalSnapshot>(cells.Payload);
    }

    [TestMethod]
    public void FileRowsCarryTheirEntryAndFoldersAreAccented()
    {
        var dashboard = NewDashboard();
        var entries = new List<FileEntry>
        {
            new("cartella", "/tmp/cartella", FileEntryKind.Directory, 0, DateTime.UnixEpoch, false),
            new("file.txt", "/tmp/file.txt", FileEntryKind.File, 2048, DateTime.UnixEpoch, false),
        };
        dashboard.AttachListing(new DirectoryListing("/tmp", entries, null));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var rows = scene.Elements.Where(e => e.Panel == UiPanel.Files && e.Payload is FileEntry).ToList();
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(UiBrush.Accent, rows[0].Brush);
        Assert.AreEqual(UiBrush.Foreground, rows[1].Brush);
        Assert.AreEqual("cartella/", rows[0].Text);
        Assert.IsTrue(rows[1].Text.Contains("2.0 K", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DirectoryErrorIsShownAsDanger()
    {
        var dashboard = NewDashboard();
        dashboard.AttachListing(new DirectoryListing("/negato", [], "permesso negato"));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var error = scene.Elements.Single(e => e.Panel == UiPanel.Files && e.Kind == UiElementKind.Text);
        Assert.AreEqual("permesso negato", error.Text);
        Assert.AreEqual(UiBrush.Danger, error.Brush);
    }

    [TestMethod]
    public void FilesAndProcessesRenderNothingWhenRowsDoNotFit()
    {
        var settings = new AppSettings { ShowKeyboard = true, FontSize = 400 };
        var dashboard = new Dashboard(settings, ThemeLibrary.BuiltIn[0], KeyboardLayout.EnUs, new KeyboardState());
        dashboard.Resize(Width, Height);
        dashboard.AttachListing(new DirectoryListing("/tmp", [new FileEntry("a", "/tmp/a", FileEntryKind.File, 1, DateTime.UnixEpoch, false)], null));
        dashboard.AttachSystem(Snapshot(10, 1, 2));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsFalse(scene.Elements.Any(e => e.Panel == UiPanel.Files && e.Payload is FileEntry));
        Assert.IsFalse(scene.Elements.Any(e => e.Panel == UiPanel.Processes && e.Kind == UiElementKind.Text));
    }

    [TestMethod]
    public void ZeroFontSizeProducesNoRows()
    {
        var settings = new AppSettings { FontSize = 0 };
        var dashboard = new Dashboard(settings, ThemeLibrary.BuiltIn[0], KeyboardLayout.EnUs, new KeyboardState());
        dashboard.Resize(Width, Height);
        dashboard.AttachListing(new DirectoryListing("/tmp", [new FileEntry("a", "/tmp/a", FileEntryKind.File, 1, DateTime.UnixEpoch, false)], null));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsFalse(scene.Elements.Any(e => e.Payload is FileEntry));
    }

    [TestMethod]
    public void CpuPanelShowsPercentBarAndHistory()
    {
        var dashboard = NewDashboard();
        dashboard.AttachSystem(Snapshot(42, 0, 0));
        dashboard.PushSample();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var label = scene.Elements.First(e => e.Panel == UiPanel.Cpu && e.Kind == UiElementKind.Text);
        Assert.IsTrue(label.Text.StartsWith("42%", StringComparison.Ordinal));
        var bar = scene.Elements.Single(e => e.Panel == UiPanel.Cpu && e.Kind == UiElementKind.Bar);
        Assert.AreEqual(0.42, bar.Value, 0.001);
        Assert.AreEqual(1, scene.Elements.Count(e => e.Panel == UiPanel.Cpu && e.Kind == UiElementKind.Polyline));
    }

    [TestMethod]
    public void MemoryPanelRendersBarEvenWithoutSnapshot()
    {
        var dashboard = NewDashboard();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var bar = scene.Elements.Single(e => e.Panel == UiPanel.Memory && e.Kind == UiElementKind.Bar);
        Assert.AreEqual(0, bar.Value);
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Memory && e.Text == "—"));
    }

    [TestMethod]
    public void MemoryPanelFormatsUsedOverTotal()
    {
        var dashboard = NewDashboard();
        dashboard.AttachSystem(Snapshot(0, 1024, 4096));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Memory && e.Text.Contains("1.0 K / 4.0 K", StringComparison.Ordinal)));
        Assert.AreEqual(0.25, scene.Elements.Single(e => e.Panel == UiPanel.Memory && e.Kind == UiElementKind.Bar).Value, 0.001);
    }

    [TestMethod]
    public void NetworkPanelShowsThroughputLatencyAndOfflineState()
    {
        var dashboard = NewDashboard();
        dashboard.AttachNetwork(Network(online: true, ping: 12.4));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Network && e.Text.Contains("2.0 K/s", StringComparison.Ordinal)));
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Network && e.Text.Contains("12 ms", StringComparison.Ordinal)));
        Assert.AreEqual(UiBrush.Muted, scene.Elements.Single(e => e.Panel == UiPanel.Network && e.Text.Contains("connessioni", StringComparison.Ordinal)).Brush);

        dashboard.AttachNetwork(Network(online: false, ping: null));
        var offline = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var status = offline.Elements.Single(e => e.Panel == UiPanel.Network && e.Text.Contains("senza risposta", StringComparison.Ordinal));
        Assert.AreEqual(UiBrush.Danger, status.Brush);
    }

    [TestMethod]
    public void NetworkPanelWithoutSnapshotIsNeutral()
    {
        var dashboard = NewDashboard();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Network && e.Text == "—"));
        Assert.IsTrue(scene.Elements.Any(e => e.Panel == UiPanel.Network && e.Text == "nessuna interfaccia"));
    }

    [TestMethod]
    public void ProcessesAreListedWithPidFreeLabels()
    {
        var dashboard = NewDashboard();
        dashboard.AttachSystem(Snapshot(0, 0, 0));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var row = scene.Elements.Single(e => e.Panel == UiPanel.Processes && e.Kind == UiElementKind.Text);
        Assert.IsTrue(row.Text.Contains("processo", StringComparison.Ordinal));
        Assert.IsTrue(row.Text.Contains("13%", StringComparison.Ordinal) || row.Text.Contains("12%", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GlobeCarriesLandPointsAndTheMarkerWhenVisible()
    {
        var dashboard = NewDashboard();
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var globe = scene.Elements.Single(e => e.Kind == UiElementKind.Globe);
        var frame = Assert.IsInstanceOfType<UiGlobeFrame>(globe.Payload);
        Assert.IsTrue(frame.Land.Count > 0);
        Assert.IsNull(frame.Marker);
        Assert.IsTrue(scene.Elements.Any(e => e.Text == "posizione non disponibile"));

        dashboard.AttachLocation(new GeoLocation(45.07, 7.69, "Torino", "Italia", "203.0.113.9", "ipwho.is"));
        var located = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var locatedFrame = Assert.IsInstanceOfType<UiGlobeFrame>(located.Elements.Single(e => e.Kind == UiElementKind.Globe).Payload);
        Assert.IsNotNull(locatedFrame.Marker);
        Assert.IsTrue(located.Elements.Any(e => e.Text == "Torino"));
    }

    [TestMethod]
    public void GlobeFallsBackToCountryThenIp()
    {
        var dashboard = NewDashboard();
        dashboard.AttachLocation(new GeoLocation(0, 0, null, "Italia", null, "ipapi.co"));
        Assert.IsTrue(dashboard.BuildScene(DateTimeOffset.UnixEpoch).Elements.Any(e => e.Text == "Italia"));
        dashboard.AttachLocation(new GeoLocation(0, 0, null, null, "203.0.113.9", "ipapi.co"));
        Assert.IsTrue(dashboard.BuildScene(DateTimeOffset.UnixEpoch).Elements.Any(e => e.Text == "203.0.113.9"));
    }

    [TestMethod]
    public void SpinAdvancesBetweenFramesAndStaysInRange()
    {
        var dashboard = NewDashboard();
        dashboard.AttachLocation(new GeoLocation(45.07, 7.69, "Torino", "Italia", null, "ipwho.is"));
        dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        for (int i = 1; i <= 400; i++)
        {
            var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch.AddSeconds(i));
            Assert.IsNotNull(((UiGlobeFrame)scene.Elements.Single(e => e.Kind == UiElementKind.Globe).Payload!).Marker);
        }
    }

    [TestMethod]
    public void PushSampleFillsHistoryUpToCapacity()
    {
        var dashboard = NewDashboard();
        Assert.AreEqual(0, dashboard.HistoryCount);
        dashboard.AttachSystem(Snapshot(50, 0, 0));
        dashboard.AttachNetwork(Network(true, 5));
        for (int i = 0; i < Dashboard.HistoryCapacity + 10; i++)
        {
            dashboard.PushSample();
        }

        Assert.AreEqual(Dashboard.HistoryCapacity, dashboard.HistoryCount);
        Assert.AreEqual(Dashboard.HistoryCapacity, dashboard.CpuHistory.Count);
        Assert.IsTrue(dashboard.CpuHistory.All(v => Math.Abs(v - 0.5) < 0.001));
        Assert.IsTrue(dashboard.DownloadHistory.All(v => v == 2048));
        Assert.IsTrue(dashboard.UploadHistory.All(v => v == 512));
    }

    [TestMethod]
    public void PushSampleWithoutDataStoresZeros()
    {
        var dashboard = NewDashboard();
        dashboard.PushSample();
        Assert.AreEqual(1, dashboard.HistoryCount);
        Assert.AreEqual(0, dashboard.CpuHistory[0]);
    }

    [TestMethod]
    public void PushSampleClampsImpossibleCpuValues()
    {
        var dashboard = NewDashboard();
        dashboard.AttachSystem(Snapshot(double.NaN, 0, 0));
        dashboard.PushSample();
        Assert.AreEqual(0, dashboard.CpuHistory[0]);
    }

    [TestMethod]
    public void HistoryPreservesInsertionOrder()
    {
        var dashboard = NewDashboard();
        for (int i = 1; i <= 3; i++)
        {
            dashboard.AttachSystem(Snapshot(i * 10, 0, 0));
            dashboard.PushSample();
        }

        Assert.AreEqual(3, dashboard.HistoryCount);
        CollectionAssert.AreEqual(ExpectedCpuRamp, dashboard.CpuHistory.Select(v => Math.Round(v, 3)).ToArray());
    }

    [TestMethod]
    public void HitTestResolvesKeyboardKeys()
    {
        var dashboard = NewDashboard();
        dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsTrue(dashboard.Keys.Count > 0);
        var target = dashboard.Keys[5].Bounds;
        Assert.IsTrue(dashboard.HitTest(target.X + 1, target.Y + 1, out var hit));
        Assert.AreEqual(UiPanel.Keyboard, hit.Panel);
        Assert.AreEqual(5, hit.Index);
        Assert.AreSame(dashboard.Keys[5].Key, hit.Value);
    }

    [TestMethod]
    public void HitTestResolvesTerminalCells()
    {
        var dashboard = NewDashboard();
        var snapshot = Terminal();
        dashboard.AttachTerminal(snapshot);
        dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var bounds = dashboard.Layout.Rect(UiPanel.Terminal).Inset(Dashboard.Padding);
        double cellWidth = bounds.Width / snapshot.Columns;
        double cellHeight = bounds.Height / snapshot.Rows;
        Assert.IsTrue(dashboard.HitTest(bounds.X + cellWidth * 3.5, bounds.Y + cellHeight * 2.5, out var hit));
        Assert.AreEqual(UiPanel.Terminal, hit.Panel);
        Assert.AreEqual(2 * snapshot.Columns + 3, hit.Index);
    }

    [TestMethod]
    public void HitTestResolvesFileRows()
    {
        var dashboard = NewDashboard();
        dashboard.AttachListing(new DirectoryListing("/tmp", [new FileEntry("a.txt", "/tmp/a.txt", FileEntryKind.File, 1, DateTime.UnixEpoch, false)], null));
        var scene = dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        var row = scene.Elements.First(e => e.Payload is FileEntry);
        Assert.IsTrue(dashboard.HitTest(row.Bounds.X + 1, row.Bounds.Y + 1, out var hit));
        Assert.AreEqual(UiPanel.Files, hit.Panel);
        Assert.IsInstanceOfType<FileEntry>(hit.Value);
    }

    [TestMethod]
    public void HitTestOutsideAnyPanelFails()
    {
        var dashboard = NewDashboard();
        dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsFalse(dashboard.HitTest(-50, -50, out var hit));
        Assert.AreEqual(UiPanel.None, hit.Panel);
    }

    [TestMethod]
    public void HitTestOnKeyboardPanelButBetweenKeysFails()
    {
        var dashboard = NewDashboard(showKeyboard: false);
        dashboard.BuildScene(DateTimeOffset.UnixEpoch);
        Assert.IsFalse(dashboard.HitTest(dashboard.Layout.Rect(UiPanel.Keyboard).X + 1, 1, out _));
    }

    [TestMethod]
    public void TerminalCellIndexRejectsOutOfBoundsAndMissingSnapshot()
    {
        var dashboard = NewDashboard();
        Assert.AreEqual(-1, dashboard.TerminalCellIndex(10, 10));
        dashboard.AttachTerminal(Terminal());
        Assert.AreEqual(-1, dashboard.TerminalCellIndex(-100, -100));
        var bounds = dashboard.Layout.Rect(UiPanel.Terminal).Inset(Dashboard.Padding);
        Assert.AreEqual(-1, dashboard.TerminalCellIndex(bounds.Right + 500, bounds.Bottom + 500));
    }

    [TestMethod]
    public void TerminalCellIndexRejectsCollapsedCells()
    {
        var settings = new AppSettings { ShowKeyboard = true };
        var dashboard = new Dashboard(settings, ThemeLibrary.BuiltIn[0], KeyboardLayout.EnUs, new KeyboardState());
        dashboard.Resize(200, 130);
        dashboard.AttachTerminal(Terminal());
        var bounds = dashboard.Layout.Rect(UiPanel.Terminal).Inset(Dashboard.Padding);
        Assert.AreEqual(-1, dashboard.TerminalCellIndex(bounds.X + 1, bounds.Y + 1));
    }

    [TestMethod]
    public void FormattersAreStable()
    {
        Assert.AreEqual("09:08:07", Dashboard.FormatClock(new DateTimeOffset(2026, 11, 14, 9, 8, 7, TimeSpan.Zero)));
        Assert.AreEqual("02:03:04 attivi", Dashboard.FormatUptime(new TimeSpan(0, 2, 3, 4)));
        Assert.AreEqual("1g 02:03 attivi", Dashboard.FormatUptime(new TimeSpan(1, 2, 3, 4)));
        Assert.AreEqual("512 B", Dashboard.FormatBytes(512));
        Assert.AreEqual("1.5 K", Dashboard.FormatBytes(1536));
        Assert.AreEqual("2.0 M", Dashboard.FormatBytes(2 * 1024 * 1024));
        Assert.AreEqual("0 B", Dashboard.FormatBytes(-10));
        Assert.AreEqual("8388608.0 T", Dashboard.FormatBytes(long.MaxValue));
    }

    [TestMethod]
    public void AttachSettersExposeTheirValues()
    {
        var dashboard = NewDashboard();
        var system = Snapshot(1, 2, 3);
        var network = Network(true, 1);
        var listing = new DirectoryListing("/tmp", [], null);
        var location = new GeoLocation(1, 2, null, null, null, "test");
        var terminal = Terminal();
        dashboard.AttachSystem(system);
        dashboard.AttachNetwork(network);
        dashboard.AttachListing(listing);
        dashboard.AttachLocation(location);
        dashboard.AttachTerminal(terminal);
        Assert.AreSame(system, dashboard.System);
        Assert.AreSame(network, dashboard.Network);
        Assert.AreSame(listing, dashboard.Listing);
        Assert.AreSame(location, dashboard.Location);
        Assert.AreSame(terminal, dashboard.Terminal);
        Assert.AreSame(ThemeLibrary.BuiltIn[0], dashboard.Theme);
        Assert.AreEqual(KeyboardLayout.EnUs, dashboard.KeyboardLayout);
        Assert.IsNotNull(dashboard.Keyboard);
        Assert.IsNotNull(dashboard.Settings);
    }
}
