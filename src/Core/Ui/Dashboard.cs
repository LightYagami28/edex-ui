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

    public void PushSample()
    {
        AdvanceRing();
        _cpuHistory[_historyStart] = ClampFraction((System?.Cpu.TotalPercent ?? 0) / PercentScale);
        _downloadHistory[_historyStart] = Math.Max(0, Network?.DownloadBytesPerSecond ?? 0);
        _uploadHistory[_historyStart] = Math.Max(0, Network?.UploadBytesPerSecond ?? 0);
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

    public bool HitTest(double x, double y, out UiHit hit)
    {
        hit = new UiHit(UiPanel.None, default, -1, null);
        if (_scene.HitTest(x, y) is not { } element)
        {
            return false;
        }

        switch (element.Kind)
        {
            case UiElementKind.Keys:
                int key = UiKeyboard.HitTest(Keys, x, y);
                if (key < 0)
                {
                    return false;
                }

                hit = new UiHit(UiPanel.Keyboard, element, key, Keys[key].Key);
                return true;
            case UiElementKind.Cells:
                int cell = TerminalCellIndex(x, y);
                if (cell < 0)
                {
                    return false;
                }

                hit = new UiHit(UiPanel.Terminal, element, cell, null);
                return true;
            case UiElementKind.Text when element.Payload is FileEntry entry:
                hit = new UiHit(UiPanel.Files, element, 0, entry);
                return true;
            default:
                hit = new UiHit(element.Panel, element, -1, element.Payload);
                return true;
        }
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

    private void DrawTopBar(DateTimeOffset now)
    {
        var bounds = Layout.Rect(UiPanel.TopBar);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.TopBar, "");
        var inner = bounds.Inset(Padding);
        double captionHeight = Settings.FontSize * SmallScale;
        double bodySize = Settings.FontSize * BodyScale;
        var top = new UiRect(inner.X, inner.Y, inner.Width, inner.Height - captionHeight);
        var caption = new UiRect(inner.X, inner.Bottom - captionHeight, inner.Width, captionHeight);
        _scene.Text(top, Identity(System), UiBrush.Foreground, bodySize, UiTextAlign.Left, UiPanel.TopBar);
        _scene.Text(top, FormatClock(now), UiBrush.Accent, bodySize, UiTextAlign.Right, UiPanel.TopBar);
        var uptime = FormatUptime(System?.Uptime ?? TimeSpan.Zero);
        _scene.Text(caption, uptime, UiBrush.Muted, captionHeight, UiTextAlign.Left, UiPanel.TopBar);
    }

    private static string Identity(SystemSnapshot? system)
    {
        string host = system?.HostName ?? Environment.MachineName;
        string description = system?.OsDescription ?? "";
        return description.Length == 0 ? host : $"{host} — {description}";
    }

    private void DrawTerminal()
    {
        var bounds = Layout.Rect(UiPanel.Terminal);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Terminal, "");
        if (Terminal is { } snapshot)
        {
            _scene.Cells(bounds.Inset(Padding), snapshot);
        }
    }

    private void DrawFiles()
    {
        var bounds = Layout.Rect(UiPanel.Files);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Files, Listing?.Path ?? "");
        var inner = Content(bounds, TitleOffset);
        if (Listing?.Error is { Length: > 0 } error)
        {
            _scene.Text(inner, error, UiBrush.Danger, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Files);
            return;
        }

        double rowHeight = Settings.FontSize * RowScale;
        if (rowHeight <= 0 || inner.Height < rowHeight)
        {
            return;
        }

        var entries = Listing?.Entries ?? [];
        int count = Math.Min(entries.Count, Math.Min(MaxFileRows, (int)(inner.Height / rowHeight)));
        for (int i = 0; i < count; i++)
        {
            _scene.Add(FileRow(entries[i], new UiRect(inner.X, inner.Y + rowHeight * i, inner.Width, rowHeight)));
        }
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
        var bounds = Layout.Rect(UiPanel.Keyboard);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Keyboard, KeyboardLayout.Name);
        _scene.Keys(Content(bounds, TitleOffset), Keys);
    }

    private void DrawCpu()
    {
        var bounds = Layout.Rect(UiPanel.Cpu);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Cpu, "cpu");
        var inner = Content(bounds, TitleOffset);
        double percent = System?.Cpu.TotalPercent ?? 0;
        double barHeight = Math.Max(1, Settings.FontSize * TitleScaleValue / HalfFactor);
        string label = string.Create(CultureInfo.InvariantCulture, $"{percent:F0}%  ·  {System?.Cpu.LogicalCores ?? 0} core");
        _scene.Text(inner.SliceTop(barHeight * HalfFactor), label, UiBrush.Foreground, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Cpu);
        var bar = new UiRect(inner.X, inner.Y + barHeight * HalfFactor, inner.Width, barHeight);
        _scene.Bar(bar, percent / PercentScale, UiBrush.Accent, UiPanel.Cpu);
        double graphTop = bar.Bottom + UiLayout.Gap;
        _scene.Polyline(new UiRect(inner.X, graphTop, inner.Width, Math.Max(0, inner.Bottom - graphTop)), CpuHistory, UiBrush.Accent, UiPanel.Cpu);
    }

    private void DrawMemory()
    {
        var bounds = Layout.Rect(UiPanel.Memory);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Memory, "memoria");
        var inner = Content(bounds, TitleOffset);
        var memory = System?.Memory;
        string label = memory is { } sample ? $"{FormatBytes((long)sample.UsedBytes)} / {FormatBytes((long)sample.TotalBytes)}" : "—";
        double labelHeight = inner.Height / HalfFactor;
        _scene.Text(inner.SliceTop(labelHeight), label, UiBrush.Foreground, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Memory);
        double barHeight = Math.Max(0, inner.Height - labelHeight);
        _scene.Bar(new UiRect(inner.X, inner.Y + labelHeight, inner.Width, barHeight), ClampFraction((memory?.UsedPercent ?? 0) / PercentScale), UiBrush.Accent, UiPanel.Memory);
    }

    private void DrawNetwork()
    {
        var bounds = Layout.Rect(UiPanel.Network);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Network, "rete");
        var inner = Content(bounds, TitleOffset);
        var network = Network;
        double rowHeight = inner.Height / HalfFactor;
        string throughput = network is { } snapshot
            ? $"↓ {FormatBytes((long)snapshot.DownloadBytesPerSecond)}/s  ↑ {FormatBytes((long)snapshot.UploadBytesPerSecond)}/s"
            : "—";
        _scene.Text(inner.SliceTop(rowHeight), throughput, UiBrush.Foreground, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Network);
        _scene.Text(new UiRect(inner.X, inner.Y + rowHeight, inner.Width, Math.Max(0, inner.Height - rowHeight)), NetworkStatus(network), network?.Online == true ? UiBrush.Muted : UiBrush.Danger, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Network);
        _scene.Polyline(inner, DownloadHistory, UiBrush.Accent, UiPanel.Network);
    }

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
        var bounds = Layout.Rect(UiPanel.Processes);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Processes, "processi");
        var inner = Content(bounds, TitleOffset);
        double rowHeight = Settings.FontSize * RowScale;
        if (rowHeight <= 0 || inner.Height < rowHeight)
        {
            return;
        }

        var processes = System?.TopProcesses ?? [];
        int capacity = Math.Min(MaxProcessRows, (int)(inner.Height / rowHeight));
        for (int i = 0; i < processes.Count && i < capacity; i++)
        {
            var process = processes[i];
            string label = string.Create(CultureInfo.InvariantCulture, $"{process.Name}  {process.CpuPercent:F0}%  {FormatBytes(process.WorkingSetBytes)}");
            _scene.Text(new UiRect(inner.X, inner.Y + rowHeight * i, inner.Width, rowHeight), label, UiBrush.Muted, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Processes);
        }
    }

    private void DrawGlobe()
    {
        var bounds = Layout.Rect(UiPanel.Globe);
        if (bounds.IsEmpty)
        {
            return;
        }

        _scene.Frame(bounds, UiPanel.Globe, "posizione");
        var location = Location;
        var projection = new Orthographic(location?.Latitude ?? 0, (location?.Longitude ?? 0) + _spin);
        ProjectLand(projection);
        UiPoint? marker = location is null ? null : ProjectOnDisc(projection, location.Latitude, location.Longitude);
        double captionHeight = Settings.FontSize * SmallScale;
        var inner = Content(bounds, TitleOffset + captionHeight);
        _scene.Globe(inner, new UiGlobeFrame(_projectedLand.ToArray(), marker));
        var captionBounds = new UiRect(bounds.X, bounds.Bottom - captionHeight, bounds.Width, captionHeight);
        _scene.Text(captionBounds, GlobeCaption(location), UiBrush.Muted, Settings.FontSize * SmallScale, UiTextAlign.Center, UiPanel.Globe);
    }

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
        location?.City ?? location?.Country ?? location?.Ip ?? "posizione non disponibile";

    private static bool IsOnDisc(UiPoint point) => point.X * point.X + point.Y * point.Y <= DiscRadiusSquared;
}
