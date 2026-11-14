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
    public const int HistoryCapacity = 60;
    public const int MaxFileRows = 64;
    public const int MaxProcessRows = 12;
    public const double TitleScale = 0.85;
    public const double BodyScale = 1.0;
    public const double SmallScale = 0.8;
    public const double RowScale = 1.45;
    public const double HalfFactor = 2;
    public const double GlobeLandStep = 3;
    public const double GlobeSpinDegreesPerSecond = 2;
    public const double FullTurnDegrees = 360;
    public const double DiscRadiusSquared = 1;
    public const double PercentScale = 100;
    public const double BytesPerKibibyte = 1024;
    public const double Padding = UiLayout.Gap;
    public const double TitleOffset = UiLayout.Gap * HalfFactor;

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

        double cellWidth = bounds.Width / snapshot.Columns;
        double cellHeight = bounds.Height / snapshot.Rows;
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return -1;
        }

        int column = (int)((x - bounds.X) / cellWidth);
        int row = (int)((y - bounds.Y) / cellHeight);
        if (column < 0 || column >= snapshot.Columns || row < 0 || row >= snapshot.Rows)
        {
            return -1;
        }

        return row * snapshot.Columns + column;
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
        string host = System?.HostName ?? Environment.MachineName;
        string description = System?.OsDescription ?? "";
        string identity = description.Length == 0 ? host : $"{host} — {description}";
        double captionHeight = Settings.FontSize * SmallScale;
        _scene.Text(new UiRect(inner.X, inner.Y, inner.Width, inner.Height - captionHeight), identity, UiBrush.Foreground, Settings.FontSize * BodyScale, UiTextAlign.Left, UiPanel.TopBar);
        _scene.Text(new UiRect(inner.X, inner.Y, inner.Width, inner.Height - captionHeight), FormatClock(now), UiBrush.Accent, Settings.FontSize * BodyScale, UiTextAlign.Right, UiPanel.TopBar);
        _scene.Text(new UiRect(inner.X, inner.Bottom - captionHeight, inner.Width, captionHeight), FormatUptime(System?.Uptime ?? TimeSpan.Zero), UiBrush.Muted, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.TopBar);
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
        int capacity = Math.Min(MaxFileRows, (int)(inner.Height / rowHeight));
        for (int i = 0; i < entries.Count && i < capacity; i++)
        {
            var entry = entries[i];
            bool folder = entry.Kind == FileEntryKind.Directory;
            _scene.Add(new UiElement
            {
                Kind = UiElementKind.Text,
                Bounds = new UiRect(inner.X, inner.Y + rowHeight * i, inner.Width, rowHeight),
                Brush = folder ? UiBrush.Accent : UiBrush.Foreground,
                Size = Settings.FontSize * SmallScale,
                Text = folder ? entry.Name + "/" : $"{entry.Name}  {FormatBytes(entry.Size)}",
                Payload = entry,
                Panel = UiPanel.Files,
            });
        }
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
        double barHeight = Math.Max(1, Settings.FontSize * TitleScale / HalfFactor);
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
        string status = network is { } current
            ? (current.PingMilliseconds is { } ping ? $"{ping:F0} ms · {current.ActiveTcpConnections} connessioni" : $"{current.InterfaceName} · senza risposta")
            : "nessuna interfaccia";
        _scene.Text(new UiRect(inner.X, inner.Y + rowHeight, inner.Width, Math.Max(0, inner.Height - rowHeight)), status, network?.Online == true ? UiBrush.Muted : UiBrush.Danger, Settings.FontSize * SmallScale, UiTextAlign.Left, UiPanel.Network);
        _scene.Polyline(inner, DownloadHistory, UiBrush.Accent, UiPanel.Network);
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
        double centerLatitude = Location?.Latitude ?? 0;
        double centerLongitude = (Location?.Longitude ?? 0) + _spin;
        var projection = new Orthographic(centerLatitude, centerLongitude);
        _projectedLand.Clear();
        foreach (var (latitude, longitude) in _land)
        {
            var (x, y, visible) = projection.Project(latitude, longitude);
            var point = new UiPoint(x, y);
            if (visible && IsOnDisc(point))
            {
                _projectedLand.Add(point);
            }
        }

        UiPoint? marker = null;
        if (Location is { } location)
        {
            var (x, y, visible) = projection.Project(location.Latitude, location.Longitude);
            var point = new UiPoint(x, y);
            if (visible && IsOnDisc(point))
            {
                marker = point;
            }
        }

        string caption = Location?.City ?? Location?.Country ?? Location?.Ip ?? "posizione non disponibile";
        double captionHeight = Settings.FontSize * SmallScale;
        var inner = Content(bounds, TitleOffset + captionHeight);
        _scene.Globe(inner, new UiGlobeFrame(_projectedLand.ToArray(), marker));
        _scene.Text(new UiRect(bounds.X, bounds.Bottom - captionHeight, bounds.Width, captionHeight), caption, UiBrush.Muted, Settings.FontSize * SmallScale, UiTextAlign.Center, UiPanel.Globe);
    }

    private static bool IsOnDisc(UiPoint point) => point.X * point.X + point.Y * point.Y <= DiscRadiusSquared;
}
