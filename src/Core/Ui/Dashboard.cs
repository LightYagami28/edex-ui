// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using Edex.Core.Files;
using Edex.Core.Geo;
using Edex.Core.Input;
using Edex.Core.Monitoring;
using Edex.Core.Networking;
using Edex.Core.Settings;
using Edex.Core.Terminal;
using Edex.Core.Themes;

namespace Edex.Core.Ui;

public sealed class Dashboard
{
    internal const int HistoryCapacity = 60;
    internal const int MaxFileRows = 64;
    internal const int MaxProcessRows = 12;
    private const double TitleScaleValue = 0.85;

    public static double TitleScale => TitleScaleValue;
    internal const double BodyScale = 1.0;
    internal const double SmallScale = 0.8;
    internal const double RowScale = 1.45;
    internal const double HalfFactor = 2;
    internal const double GlobeLandStep = 3;
    internal const double GlobeSpinDegreesPerSecond = 2;
    internal const double FullTurnDegrees = 360;
    internal const double DiscRadiusSquared = 1;
    internal const double PercentScale = 100;
    internal const double BytesPerKibibyte = 1024;
    internal const double Padding = UiLayout.Gap;
    internal const double TitleOffset = UiLayout.Gap * HalfFactor;

    private const string NoLocation = "posizione non disponibile";

    private static readonly DirectoryListing EmptyListing = new("", [], null);

    private static readonly string[] ByteUnits = ["B", "K", "M", "G", "T"];

    private readonly double[] _cpuHistory = new double[HistoryCapacity];
    private readonly double[] _downloadHistory = new double[HistoryCapacity];
    private readonly double[] _uploadHistory = new double[HistoryCapacity];
    private readonly List<(double Latitude, double Longitude)> _land = [];
    private readonly List<UiPoint> _projectedLand = [];
    private readonly UiScene _scene = new();

    private int _historyCount;
    private int _historyStart;
    private double _spin;
    private DateTimeOffset _previousFrame;

    public Dashboard(AppSettings settings, Theme theme, KeyboardLayout layout, KeyboardState keyboard)
    {
        Settings = settings;
        Theme = theme;
        KeyboardLayout = layout;
        Keyboard = keyboard;
        Layout = UiLayout.Compute(0, 0, settings.ShowKeyboard);
        _land.AddRange(LandMask.Default.LandPoints(GlobeLandStep));
    }

    public AppSettings Settings { get; }

    public Theme Theme { get; }

    public KeyboardLayout KeyboardLayout { get; }

    public KeyboardState Keyboard { get; }

    public UiLayout Layout { get; private set; }

    public SystemSnapshot? System { get; private set; }

    public NetworkSnapshot? Network { get; private set; }

    public DirectoryListing? Listing { get; private set; }

    public GeoLocation? Location { get; private set; }

    public TerminalSnapshot? Terminal { get; private set; }

    public IReadOnlyList<UiKey> Keys { get; private set; } = [];

    public int HistoryCount => Math.Min(_historyCount, HistoryCapacity);

    public IReadOnlyList<double> CpuHistory => Slice(_cpuHistory);

    public IReadOnlyList<double> DownloadHistory => Slice(_downloadHistory);

    public IReadOnlyList<double> UploadHistory => Slice(_uploadHistory);

    public void Resize(double width, double height) => Layout = UiLayout.Compute(width, height, Settings.ShowKeyboard);

    public void AttachSystem(SystemSnapshot snapshot) => System = snapshot;

    public void AttachNetwork(NetworkSnapshot snapshot) => Network = snapshot;

    public void AttachListing(DirectoryListing listing) => Listing = listing;

    public void AttachLocation(GeoLocation? location) => Location = location;

    public void AttachTerminal(TerminalSnapshot snapshot) => Terminal = snapshot;

    private double CpuPercent() => System?.Cpu.TotalPercent ?? 0;

    private double DownloadRate() => Math.Max(0, Network?.DownloadBytesPerSecond ?? 0);

    private double UploadRate() => Math.Max(0, Network?.UploadBytesPerSecond ?? 0);

    public void PushSample()
    {
        AdvanceRing();
        _cpuHistory[_historyStart] = ClampFraction(CpuPercent() / PercentScale);
        _downloadHistory[_historyStart] = DownloadRate();
        _uploadHistory[_historyStart] = UploadRate();
    }

    public UiScene BuildScene(DateTimeOffset now)
    {
        AdvanceSpin(now);
        Keys = UiKeyboard.Arrange(KeyboardLayout, Content(Layout.Rect(UiPanel.Keyboard), TitleOffset), Keyboard);
        _scene.Clear();
        DrawTopBar(now);
        DrawTerminal();
        DrawFiles();
        DrawKeyboard();
        DrawCpu();
        DrawMemory();
        DrawNetwork();
        DrawProcesses();
        DrawGlobe();
        return _scene;
    }

    public UiHit? HitTest(double x, double y)
    {
        var element = _scene.HitTest(x, y);
        return element is null ? null : Resolve(element.Value, x, y);
    }

    private UiHit? Resolve(UiElement element, double x, double y)
    {
        if (element.Kind == UiElementKind.Keys)
        {
            return KeyHit(element, x, y);
        }

        if (element.Kind == UiElementKind.Cells)
        {
            return CellHit(element, x, y);
        }

        return element.Kind == UiElementKind.Text && element.Payload is FileEntry entry
            ? new UiHit(UiPanel.Files, element, 0, entry)
            : new UiHit(element.Panel, element, -1, element.Payload);
    }

    private UiHit? KeyHit(UiElement element, double x, double y)
    {
        int key = UiKeyboard.HitTest(Keys, x, y);
        return key < 0 ? null : new UiHit(UiPanel.Keyboard, element, key, Keys[key].Key);
    }

    private UiHit? CellHit(UiElement element, double x, double y)
    {
        int cell = TerminalCellIndex(x, y);
        return cell < 0 ? null : new UiHit(UiPanel.Terminal, element, cell, null);
    }

    public int TerminalCellIndex(double x, double y)
    {
        if (Terminal is not { } snapshot)
        {
            return -1;
        }

        var bounds = Layout.Rect(UiPanel.Terminal).Inset(Padding);
        if (!bounds.Contains(x, y))
        {
            return -1;
        }

        int column = CellIndex(x - bounds.X, bounds.Width, snapshot.Columns);
        int row = CellIndex(y - bounds.Y, bounds.Height, snapshot.Rows);
        return column < 0 || row < 0 ? -1 : row * snapshot.Columns + column;
    }

    private static int CellIndex(double offset, double size, int count)
    {
        double cell = size / count;
        if (cell <= 0)
        {
            return -1;
        }

        int index = (int)(offset / cell);
        return index >= 0 && index < count ? index : -1;
    }

    internal static string FormatClock(DateTimeOffset now) => now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    internal static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalDays}g {uptime.Hours:D2}:{uptime.Minutes:D2} attivi")
        : string.Create(CultureInfo.InvariantCulture, $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2} attivi");

    internal static string FormatBytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= BytesPerKibibyte && unit < ByteUnits.Length - 1)
        {
            value /= BytesPerKibibyte;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:F0} {ByteUnits[unit]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:F1} {ByteUnits[unit]}");
    }

    private double[] Slice(double[] ring)
    {
        int count = HistoryCount;
        int oldest = ((_historyStart - count + 1) % HistoryCapacity + HistoryCapacity) % HistoryCapacity;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ring[(oldest + i) % HistoryCapacity];
        }

        return values;
    }

    private void AdvanceRing()
    {
        if (_historyCount < HistoryCapacity)
        {
            _historyStart = _historyCount;
            _historyCount++;
            return;
        }

        _historyStart = (_historyStart + 1) % HistoryCapacity;
    }

    private void AdvanceSpin(DateTimeOffset now)
    {
        if (_previousFrame != default)
        {
            _spin = (_spin + Math.Max(0, (now - _previousFrame).TotalSeconds) * GlobeSpinDegreesPerSecond) % FullTurnDegrees;
        }

        _previousFrame = now;
    }

    private static double ClampFraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    internal static UiRect Content(UiRect bounds, double reserved) => new(
        bounds.X + Padding,
        bounds.Y + Padding + reserved,
        Math.Max(0, bounds.Width - Padding * HalfFactor),
        Math.Max(0, bounds.Height - Padding * HalfFactor - reserved));

    private UiRect? OpenPanel(UiPanel panel, string title)
    {
        var bounds = Layout.Rect(panel);
        if (bounds.IsEmpty)
        {
            return null;
        }

        _scene.Frame(bounds, panel, title);
        return bounds;
    }

    private UiTextStyle Small(UiPanel panel, UiBrush brush) => new(brush, Settings.FontSize * SmallScale, UiTextAlign.Left, panel);

    private void DrawTopBar(DateTimeOffset now)
    {
        if (OpenPanel(UiPanel.TopBar, "") is not { } bounds)
        {
            return;
        }

        var inner = bounds.Inset(Padding);
        double captionHeight = Settings.FontSize * SmallScale;
        double bodySize = Settings.FontSize * BodyScale;
        var top = new UiRect(inner.X, inner.Y, inner.Width, inner.Height - captionHeight);
        var caption = new UiRect(inner.X, inner.Bottom - captionHeight, inner.Width, captionHeight);
        _scene.Text(top, Identity(System), new UiTextStyle(UiBrush.Foreground, bodySize, UiTextAlign.Left, UiPanel.TopBar));
        _scene.Text(top, FormatClock(now), new UiTextStyle(UiBrush.Accent, bodySize, UiTextAlign.Right, UiPanel.TopBar));
        _scene.Text(caption, FormatUptime(System?.Uptime ?? TimeSpan.Zero), Small(UiPanel.TopBar, UiBrush.Muted));
    }

    private static string Identity(SystemSnapshot? system) =>
        system is null ? Environment.MachineName : Identity(system.HostName, system.OsDescription);

    private static string Identity(string host, string description) => description.Length == 0 ? host : $"{host} — {description}";

    private void DrawTerminal()
    {
        if (OpenPanel(UiPanel.Terminal, "") is { } bounds && Terminal is { } snapshot)
        {
            _scene.Cells(bounds.Inset(Padding), snapshot, UiPanel.Terminal);
        }
    }

    private void DrawFiles()
    {
        var listing = Listing ?? EmptyListing;
        if (OpenPanel(UiPanel.Files, listing.Path) is not { } bounds)
        {
            return;
        }

        var inner = Content(bounds, TitleOffset);
        if (string.IsNullOrEmpty(listing.Error))
        {
            DrawFileRows(inner, listing.Entries);
        }
        else
        {
            _scene.Text(inner, listing.Error, Small(UiPanel.Files, UiBrush.Danger));
        }
    }

    private void DrawFileRows(UiRect inner, IReadOnlyList<FileEntry> entries)
    {
        int count = Math.Min(entries.Count, VisibleRows(inner, MaxFileRows));
        double rowHeight = Settings.FontSize * RowScale;
        for (int i = 0; i < count; i++)
        {
            _scene.Add(FileRow(entries[i], new UiRect(inner.X, inner.Y + rowHeight * i, inner.Width, rowHeight)));
        }
    }

    private int VisibleRows(UiRect inner, int maximum)
    {
        double rowHeight = Settings.FontSize * RowScale;
        return rowHeight <= 0 || inner.Height < rowHeight ? 0 : Math.Min(maximum, (int)(inner.Height / rowHeight));
    }

    private UiElement FileRow(FileEntry entry, UiRect bounds)
    {
        bool folder = entry.Kind == FileEntryKind.Directory;
        return new UiElement
        {
            Kind = UiElementKind.Text,
            Bounds = bounds,
            Brush = folder ? UiBrush.Accent : UiBrush.Foreground,
            Size = Settings.FontSize * SmallScale,
            Text = folder ? entry.Name + "/" : $"{entry.Name}  {FormatBytes(entry.Size)}",
            Payload = entry,
            Panel = UiPanel.Files,
        };
    }

    private void DrawKeyboard()
    {
        if (OpenPanel(UiPanel.Keyboard, KeyboardLayout.Name) is { } bounds)
        {
            _scene.Keys(Content(bounds, TitleOffset), Keys, UiPanel.Keyboard);
        }
    }

    private void DrawCpu()
    {
        if (OpenPanel(UiPanel.Cpu, "cpu") is not { } bounds)
        {
            return;
        }

        var inner = Content(bounds, TitleOffset);
        double barHeight = Math.Max(1, Settings.FontSize * TitleScaleValue / HalfFactor);
        string label = string.Create(CultureInfo.InvariantCulture, $"{CpuPercent():F0}%  ·  {System?.Cpu.LogicalCores ?? 0} core");
        _scene.Text(inner.SliceTop(barHeight * HalfFactor), label, Small(UiPanel.Cpu, UiBrush.Foreground));
        var bar = new UiRect(inner.X, inner.Y + barHeight * HalfFactor, inner.Width, barHeight);
        _scene.Bar(bar, CpuPercent() / PercentScale, UiBrush.Accent, UiPanel.Cpu);
        double graphTop = bar.Bottom + UiLayout.Gap;
        _scene.Polyline(new UiRect(inner.X, graphTop, inner.Width, Math.Max(0, inner.Bottom - graphTop)), CpuHistory, UiBrush.Accent, UiPanel.Cpu);
    }

    private void DrawMemory()
    {
        if (OpenPanel(UiPanel.Memory, "memoria") is not { } bounds)
        {
            return;
        }

        var inner = Content(bounds, TitleOffset);
        var memory = System?.Memory;
        double labelHeight = inner.Height / HalfFactor;
        _scene.Text(inner.SliceTop(labelHeight), MemoryLabel(memory), Small(UiPanel.Memory, UiBrush.Foreground));
        var bar = new UiRect(inner.X, inner.Y + labelHeight, inner.Width, Math.Max(0, inner.Height - labelHeight));
        _scene.Bar(bar, MemoryFraction(memory), UiBrush.Accent, UiPanel.Memory);
    }

    private static string MemoryLabel(MemorySample? memory) =>
        memory is null ? "—" : $"{FormatBytes((long)memory.UsedBytes)} / {FormatBytes((long)memory.TotalBytes)}";

    private static double MemoryFraction(MemorySample? memory) => ClampFraction((memory?.UsedPercent ?? 0) / PercentScale);

    private void DrawNetwork()
    {
        if (OpenPanel(UiPanel.Network, "rete") is not { } bounds)
        {
            return;
        }

        var inner = Content(bounds, TitleOffset);
        var network = Network;
        double rowHeight = inner.Height / HalfFactor;
        _scene.Text(inner.SliceTop(rowHeight), Throughput(network), Small(UiPanel.Network, UiBrush.Foreground));
        var status = new UiRect(inner.X, inner.Y + rowHeight, inner.Width, Math.Max(0, inner.Height - rowHeight));
        _scene.Text(status, NetworkStatus(network), Small(UiPanel.Network, network?.Online == true ? UiBrush.Muted : UiBrush.Danger));
        _scene.Polyline(inner, DownloadHistory, UiBrush.Accent, UiPanel.Network);
    }

    private static string Throughput(NetworkSnapshot? network) => network is null
        ? "—"
        : $"↓ {FormatBytes((long)network.DownloadBytesPerSecond)}/s  ↑ {FormatBytes((long)network.UploadBytesPerSecond)}/s";

    internal static string NetworkStatus(NetworkSnapshot? network)
    {
        if (network is null)
        {
            return "nessuna interfaccia";
        }

        if (network.PingMilliseconds is { } ping)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{ping:F0} ms · {network.ActiveTcpConnections} connessioni");
        }

        return $"{network.InterfaceName} · senza risposta";
    }

    private void DrawProcesses()
    {
        if (OpenPanel(UiPanel.Processes, "processi") is not { } bounds)
        {
            return;
        }

        var inner = Content(bounds, TitleOffset);
        var processes = System?.TopProcesses ?? [];
        int count = Math.Min(processes.Count, VisibleRows(inner, MaxProcessRows));
        double rowHeight = Settings.FontSize * RowScale;
        for (int i = 0; i < count; i++)
        {
            var row = new UiRect(inner.X, inner.Y + rowHeight * i, inner.Width, rowHeight);
            _scene.Text(row, ProcessLabel(processes[i]), Small(UiPanel.Processes, UiBrush.Muted));
        }
    }

    private static string ProcessLabel(ProcessSample process) =>
        string.Create(CultureInfo.InvariantCulture, $"{process.Name}  {process.CpuPercent:F0}%  {FormatBytes(process.WorkingSetBytes)}");

    private void DrawGlobe()
    {
        if (OpenPanel(UiPanel.Globe, "posizione") is not { } bounds)
        {
            return;
        }

        var location = Location;
        var projection = Projection(location);
        ProjectLand(projection);
        double captionHeight = Settings.FontSize * SmallScale;
        _scene.Globe(Content(bounds, TitleOffset + captionHeight), new UiGlobeFrame(_projectedLand.ToArray(), Marker(projection, location)), UiPanel.Globe);
        var captionBounds = new UiRect(bounds.X, bounds.Bottom - captionHeight, bounds.Width, captionHeight);
        _scene.Text(captionBounds, GlobeCaption(location), new UiTextStyle(UiBrush.Muted, captionHeight, UiTextAlign.Center, UiPanel.Globe));
    }

    private Orthographic Projection(GeoLocation? location) =>
        location is null ? new Orthographic(0, _spin) : new Orthographic(location.Latitude, location.Longitude + _spin);

    private static UiPoint? Marker(Orthographic projection, GeoLocation? location) =>
        location is null ? null : ProjectOnDisc(projection, location.Latitude, location.Longitude);

    private void ProjectLand(Orthographic projection)
    {
        _projectedLand.Clear();
        foreach (var (latitude, longitude) in _land)
        {
            if (ProjectOnDisc(projection, latitude, longitude) is { } point)
            {
                _projectedLand.Add(point);
            }
        }
    }

    private static UiPoint? ProjectOnDisc(Orthographic projection, double latitude, double longitude)
    {
        var (x, y, visible) = projection.Project(latitude, longitude);
        var point = new UiPoint(x, y);
        return visible && IsOnDisc(point) ? point : null;
    }

    private static string GlobeCaption(GeoLocation? location) =>
        location is null ? NoLocation : location.City ?? location.Country ?? location.Ip ?? NoLocation;

    private static bool IsOnDisc(UiPoint point) => point.X * point.X + point.Y * point.Y <= DiscRadiusSquared;
}
