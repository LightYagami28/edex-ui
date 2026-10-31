// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Edex.Core.Files;
using Edex.Core.Monitoring;
using Edex.Core.Networking;
using Edex.Core.Pty;
using Edex.Core.Settings;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Edex.UI.Controls;

namespace Edex.UI.Views;

public sealed class MainWindow : Window
{
    private readonly PlatformProfile _profile;
    private readonly string? _smokeTestDirectory;
    private readonly SettingsStore _store = new();
    private readonly SystemMonitor _systemMonitor = new();
    private readonly NetworkMonitor _networkMonitor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<TerminalSession> _sessions = [];
    private readonly IReadOnlyList<Theme> _themes;
    private readonly Sparkline _cpuHistory = new();
    private readonly Sparkline _netHistory = new();

    private AppSettings _settings;
    private Palette _palette;
    private TerminalSession? _active;
    private string _browserPath;
    private TerminalView _terminal = null!;

    // Live widgets (recreated whenever the layout is rebuilt for a theme change).
    private TextBlock _clock = null!, _date = null!, _uptime = null!;
    private TextBlock _host = null!, _os = null!, _cpuModel = null!, _cpuValue = null!, _memValue = null!;
    private ProgressBar _cpuBar = null!, _memBar = null!;
    private StackPanel _processes = null!, _disks = null!, _tabs = null!;
    private TextBlock _netState = null!, _netIface = null!, _netIp = null!, _netGateway = null!, _netPing = null!, _netRates = null!, _netConns = null!;
    private TextBlock _browserTitle = null!;
    private ListBox _fileList = null!;

    public MainWindow(PlatformProfile profile, string? smokeTestDirectory)
    {
        _profile = profile;
        _smokeTestDirectory = smokeTestDirectory;
        _settings = _store.Load();
        _themes = ThemeLibrary.Load(_store.ThemesDirectory);
        _palette = new Palette(ThemeLibrary.Find(_themes, _settings.Theme));
        _networkMonitor = new NetworkMonitor(_settings.PingHost);
        _browserPath = FileBrowser.ExpandHome(_settings.StartDirectory ?? "~");
        if (!Directory.Exists(_browserPath)) _browserPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Title = "eDEX-UI";
        Width = 1440;
        Height = 900;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _cpuHistory.FixedMaximum = 100;
        BuildLayout();
        RefreshFiles();
        Opened += OnOpened;
        Closing += (_, _) => Shutdown();
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var p = _palette;
        Background = p.BackgroundBrush;
        _cpuHistory.Stroke = p.AccentBrush;
        _netHistory.Stroke = p.AccentBrush;
        _netHistory.SecondaryStroke = new ImmutableSolidColorBrush(p.Danger);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(14) };
        root.Children.Add(BuildTopBar());

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("290,*,300"), Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(main, 1);
        root.Children.Add(main);

        var left = new ScrollViewer { Content = BuildLeftColumn(), Margin = new Thickness(0, 0, 12, 0) };
        main.Children.Add(left);

        var center = BuildCenter();
        Grid.SetColumn(center, 1);
        main.Children.Add(center);

        var right = new ScrollViewer { Content = BuildRightColumn(), Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(right, 2);
        main.Children.Add(right);

        // Subtle radial glow behind everything for depth.
        var backdrop = new Panel();
        backdrop.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x26, p.Accent.R, p.Accent.G, p.Accent.B), 0),
                    new GradientStop(Colors.Transparent, 1),
                },
            },
        });
        backdrop.Children.Add(root);
        Content = backdrop;
    }

    private Control BuildTopBar()
    {
        var p = _palette;
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            BorderBrush = p.AccentBrush,
            BorderThickness = new Thickness(1.5),
            Child = Ui.Text("ε", p.AccentBrush, 18, FontWeight.Bold).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
        });
        var brandText = new StackPanel();
        brandText.Children.Add(Ui.Text("eDEX-UI", p.ForegroundBrush, 16, FontWeight.SemiBold));
        brandText.Children.Add(Ui.Text($"Next · .NET {Environment.Version.ToString(2)} · {_profile.Name}", p.MutedBrush, 11));
        brand.Children.Add(brandText);
        bar.Children.Add(brand);

        var clockStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _clock = Ui.Text("--:--:--", p.ForegroundBrush, 30, FontWeight.Light, _palette.Theme.FontFamily);
        _clock.HorizontalAlignment = HorizontalAlignment.Center;
        _date = Ui.Text("", p.MutedBrush, 12);
        _date.HorizontalAlignment = HorizontalAlignment.Center;
        clockStack.Children.Add(_clock);
        clockStack.Children.Add(_date);
        Grid.SetColumn(clockStack, 1);
        bar.Children.Add(clockStack);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        _uptime = Ui.Text("uptime --", p.MutedBrush, 12);
        right.Children.Add(_uptime);
        var themePicker = new ComboBox
        {
            ItemsSource = _themes.Select(t => t.Name).ToList(),
            SelectedItem = _palette.Theme.Name,
            MinWidth = 120,
            BorderBrush = p.BorderBrush,
        };
        themePicker.SelectionChanged += (_, _) =>
        {
            if (themePicker.SelectedItem is string name && name != _palette.Theme.Name) ApplyTheme(name);
        };
        right.Children.Add(themePicker);
        Grid.SetColumn(right, 2);
        bar.Children.Add(right);
        return bar;
    }

    private Control BuildLeftColumn()
    {
        var p = _palette;
        var column = new StackPanel();

        var system = new StackPanel { Spacing = 2 };
        _host = Ui.Text(Environment.MachineName, p.ForegroundBrush, 13);
        _os = Ui.Text(System.Runtime.InteropServices.RuntimeInformation.OSDescription, p.ForegroundBrush, 12);
        system.Children.Add(Ui.KeyValue(p, "host", _host));
        system.Children.Add(Ui.KeyValue(p, "os", _os));
        system.Children.Add(Ui.KeyValue(p, "user", Ui.Text(Environment.UserName, p.ForegroundBrush, 12)));
        system.Children.Add(Ui.KeyValue(p, "arch", Ui.Text(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), p.ForegroundBrush, 12)));
        column.Children.Add(Ui.Card(p, "System", system));

        var cpu = new StackPanel();
        _cpuModel = Ui.Text("", p.MutedBrush, 11);
        _cpuValue = Ui.Text("0%", p.ForegroundBrush, 22, FontWeight.Light);
        _cpuBar = Ui.Bar(p);
        cpu.Children.Add(_cpuModel);
        cpu.Children.Add(_cpuValue);
        cpu.Children.Add(_cpuBar);
        cpu.Children.Add(_cpuHistory.Detach());
        column.Children.Add(Ui.Card(p, "CPU", cpu, $"{Environment.ProcessorCount} threads"));

        var mem = new StackPanel();
        _memValue = Ui.Text("", p.ForegroundBrush, 13);
        _memBar = Ui.Bar(p);
        mem.Children.Add(_memValue);
        mem.Children.Add(_memBar);
        column.Children.Add(Ui.Card(p, "Memory", mem));

        _processes = new StackPanel { Spacing = 3 };
        column.Children.Add(Ui.Card(p, "Top processes", _processes));
        return column;
    }

    private Control BuildRightColumn()
    {
        var p = _palette;
        var column = new StackPanel();

        var net = new StackPanel { Spacing = 2 };
        _netState = Ui.Text("…", p.AccentBrush, 13, FontWeight.SemiBold);
        _netIface = Ui.Text("", p.ForegroundBrush, 12);
        _netIp = Ui.Text("", p.ForegroundBrush, 12);
        _netGateway = Ui.Text("", p.ForegroundBrush, 12);
        _netPing = Ui.Text("", p.ForegroundBrush, 12);
        _netConns = Ui.Text("", p.ForegroundBrush, 12);
        net.Children.Add(Ui.KeyValue(p, "state", _netState));
        net.Children.Add(Ui.KeyValue(p, "interface", _netIface));
        net.Children.Add(Ui.KeyValue(p, "ipv4", _netIp));
        net.Children.Add(Ui.KeyValue(p, "gateway", _netGateway));
        net.Children.Add(Ui.KeyValue(p, $"ping {_settings.PingHost}", _netPing));
        net.Children.Add(Ui.KeyValue(p, "tcp established", _netConns));
        column.Children.Add(Ui.Card(p, "Network", net));

        var traffic = new StackPanel();
        _netRates = Ui.Text("", p.ForegroundBrush, 12);
        traffic.Children.Add(_netRates);
        traffic.Children.Add(_netHistory.Detach());
        column.Children.Add(Ui.Card(p, "Traffic", traffic, "▼ in  ▲ out"));

        _disks = new StackPanel { Spacing = 6 };
        column.Children.Add(Ui.Card(p, "Storage", _disks));
        return column;
    }

    private Control BuildCenter()
    {
        var p = _palette;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,3*,Auto,2*") };

        var tabBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var add = new Button
        {
            Content = "+",
            Padding = new Thickness(10, 2),
            Foreground = p.AccentBrush,
            Background = p.GlowBrush,
            BorderBrush = p.BorderBrush,
            BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(add, "New terminal");
        add.Click += (_, _) => OpenTerminal();
        DockPanel.SetDock(add, Dock.Right);
        tabBar.Children.Add(add);
        _tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tabBar.Children.Add(_tabs);
        grid.Children.Add(tabBar);

        var previous = _terminal;
        _terminal = new TerminalView(p);
        _terminal.SetFont(p.Theme.FontFamily, _settings.FontSize);
        if (previous is not null) previous.Session = null;
        var terminalFrame = new Border
        {
            BorderBrush = p.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, Color = Color.FromArgb(0x40, p.Accent.R, p.Accent.G, p.Accent.B) }),
            Child = _terminal,
        };
        Grid.SetRow(terminalFrame, 1);
        grid.Children.Add(terminalFrame);

        var browserHeader = new DockPanel { Margin = new Thickness(2, 10, 2, 6) };
        var up = new Button { Content = "↑", Padding = new Thickness(8, 0), Foreground = p.AccentBrush, Background = Brushes.Transparent };
        up.Click += (_, _) => NavigateBrowser(Directory.GetParent(_browserPath)?.FullName ?? _browserPath, syncShell: false);
        DockPanel.SetDock(up, Dock.Right);
        browserHeader.Children.Add(up);
        _browserTitle = Ui.Text("", p.AccentBrush, 12, FontWeight.SemiBold);
        browserHeader.Children.Add(_browserTitle);
        Grid.SetRow(browserHeader, 2);
        grid.Children.Add(browserHeader);

        _fileList = new ListBox
        {
            Background = p.SurfaceBrush,
            BorderBrush = p.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
        };
        _fileList.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        Grid.SetRow(_fileList, 3);
        grid.Children.Add(_fileList);

        if (_active is not null) ShowSession(_active);
        RebuildTabs();
        return grid;
    }

    // ------------------------------------------------------------------ terminals

    private void OpenTerminal()
    {
        try
        {
            var session = TerminalSession.Start(new PtyOptions
            {
                Shell = _settings.Shell,
                Args = _settings.ShellArgs,
                WorkingDirectory = _browserPath,
                Columns = 100,
                Rows = 30,
            }, _settings.Scrollback);
            session.Exited += (_, _) => Dispatcher.UIThread.Post(() => OnSessionExited(session));
            _sessions.Add(session);
            ShowSession(session);
            RebuildTabs();
        }
        catch (Exception e)
        {
            _browserTitle.Text = $"Cannot start shell '{_settings.Shell}': {e.Message}";
        }
    }

    private void ShowSession(TerminalSession session)
    {
        _active = session;
        _terminal.Session = session;
        _terminal.Focus();
        RebuildTabs();
    }

    private void OnSessionExited(TerminalSession session)
    {
        _sessions.Remove(session);
        session.Dispose();
        if (ReferenceEquals(_active, session))
        {
            _active = null;
            if (_sessions.Count > 0) ShowSession(_sessions[^1]);
            else if (_smokeTestDirectory is null) OpenTerminal();
        }
        RebuildTabs();
    }

    private void RebuildTabs()
    {
        if (_tabs is null) return;
        _tabs.Children.Clear();
        for (int i = 0; i < _sessions.Count; i++)
        {
            var session = _sessions[i];
            bool active = ReferenceEquals(session, _active);
            string title = string.IsNullOrWhiteSpace(session.Buffer.Title) ? Path.GetFileNameWithoutExtension(session.Shell) : session.Buffer.Title;
            if (title.Length > 28) title = title[..27] + "…";
            var tab = new Button
            {
                Content = $"{i + 1} · {title}",
                Padding = new Thickness(12, 4),
                FontSize = 12,
                Foreground = active ? _palette.BackgroundBrush : _palette.ForegroundBrush,
                Background = active ? _palette.AccentBrush : _palette.GlowBrush,
                BorderBrush = _palette.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            };
            tab.Click += (_, _) => ShowSession(session);
            _tabs.Children.Add(tab);
        }
    }

    // ------------------------------------------------------------------ file browser

    private void NavigateBrowser(string path, bool syncShell)
    {
        _browserPath = path;
        RefreshFiles();
        if (syncShell && _active is not null && !_active.HasExited)
        {
            _active.Send(OperatingSystem.IsWindows() ? $"cd '{path.Replace("'", "''")}'\r" : $"cd '{path.Replace("'", "'\\''")}'\r");
            _terminal.Focus();
        }
    }

    private void RefreshFiles()
    {
        var listing = FileBrowser.List(_browserPath, _settings.ShowHiddenFiles);
        _browserPath = listing.Path;
        _browserTitle.Text = listing.Error is null ? $"FILES  {listing.Path}" : $"FILES  {listing.Path}  ({listing.Error})";
        var items = new List<Control>();
        foreach (var entry in listing.Entries.Take(400))
        {
            bool dir = entry.Kind == FileEntryKind.Directory;
            var tile = new StackPanel { Width = 92, Spacing = 2, Margin = new Thickness(2) };
            tile.Children.Add(Ui.Text(dir ? "▣" : entry.Kind == FileEntryKind.Link ? "⇢" : "▤", dir ? _palette.AccentBrush : _palette.MutedBrush, 22)
                .Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            tile.Children.Add(Ui.Text(entry.Name, _palette.ForegroundBrush, 11).Also(t =>
            {
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.TextAlignment = TextAlignment.Center;
            }));
            tile.Children.Add(Ui.Text(dir ? "" : FileBrowser.FormatSize(entry.Size), _palette.MutedBrush, 10)
                .Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            ToolTip.SetTip(tile, entry.FullPath);
            tile.Background = Brushes.Transparent;
            tile.Tapped += (_, _) =>
            {
                if (dir) NavigateBrowser(entry.FullPath, syncShell: true);
                else if (_active is not null)
                {
                    _active.Send(entry.FullPath.Contains(' ') ? $"\"{entry.FullPath}\" " : entry.FullPath + " ");
                    _terminal.Focus();
                }
            };
            items.Add(tile);
        }
        _fileList.ItemsSource = items;
    }

    // ------------------------------------------------------------------ themes

    private void ApplyTheme(string name)
    {
        _palette = new Palette(ThemeLibrary.Find(_themes, name));
        _settings = _settings with { Theme = _palette.Theme.Name };
        try { _store.Save(_settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        BuildLayout();
        RefreshFiles();
    }

    // ------------------------------------------------------------------ monitors

    private void OnOpened(object? sender, EventArgs e)
    {
        OpenTerminal();
        _ = Task.Run(() => MonitorLoopAsync(_shutdown.Token));
        var clockTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateClock());
        clockTimer.Start();
        UpdateClock();
        if (_smokeTestDirectory is not null) _ = RunSmokeTestAsync(_smokeTestDirectory);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _date.Text = now.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, _settings.MonitorIntervalMs)));
        do
        {
            try
            {
                var system = _systemMonitor.Sample();
                var network = await _networkMonitor.SampleAsync(token);
                await Dispatcher.UIThread.InvokeAsync(() => UpdateMonitors(system, network));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"monitor error: {ex}");
            }
        }
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
    }

    private void UpdateMonitors(SystemSnapshot system, NetworkSnapshot network)
    {
        var p = _palette;
        _uptime.Text = $"uptime {(int)system.Uptime.TotalDays}d {system.Uptime.Hours:00}:{system.Uptime.Minutes:00}";
        _cpuModel.Text = system.Cpu.Model;
        _cpuValue.Text = $"{system.Cpu.TotalPercent:0}%";
        _cpuBar.Value = system.Cpu.TotalPercent;
        _cpuHistory.Push(system.Cpu.TotalPercent);

        _memValue.Text = $"{FileBrowser.FormatSize((long)system.Memory.UsedBytes)} / {FileBrowser.FormatSize((long)system.Memory.TotalBytes)}  ({system.Memory.UsedPercent:0}%)";
        _memBar.Value = system.Memory.UsedPercent;

        _processes.Children.Clear();
        foreach (var proc in system.TopProcesses)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,52,64") };
            row.Children.Add(Ui.Text(proc.Name, p.ForegroundBrush, 12));
            var cpu = Ui.Text($"{proc.CpuPercent:0.0}%", p.AccentBrush, 12).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);
            Grid.SetColumn(cpu, 1);
            row.Children.Add(cpu);
            var mem = Ui.Text(FileBrowser.FormatSize(proc.WorkingSetBytes), p.MutedBrush, 12).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);
            Grid.SetColumn(mem, 2);
            row.Children.Add(mem);
            _processes.Children.Add(row);
        }

        _disks.Children.Clear();
        foreach (var disk in system.Disks.Take(8))
        {
            var block = new StackPanel();
            block.Children.Add(Ui.KeyValue(p, disk.Name, Ui.Text($"{FileBrowser.FormatSize(disk.FreeBytes)} free / {FileBrowser.FormatSize(disk.TotalBytes)}", p.ForegroundBrush, 11)));
            var bar = Ui.Bar(p);
            bar.Value = disk.UsedPercent;
            block.Children.Add(bar);
            _disks.Children.Add(block);
        }

        _netState.Text = network.Online ? "ONLINE" : "OFFLINE";
        _netState.Foreground = network.Online ? p.AccentBrush : new ImmutableSolidColorBrush(p.Danger);
        _netIface.Text = network.InterfaceName;
        _netIp.Text = network.IPv4 ?? "—";
        _netGateway.Text = network.Gateway ?? "—";
        _netPing.Text = network.PingMilliseconds is double ping ? $"{ping:0} ms" : "—";
        _netConns.Text = network.ActiveTcpConnections.ToString(CultureInfo.InvariantCulture);
        _netRates.Text = $"▼ {FileBrowser.FormatSize((long)network.DownloadBytesPerSecond)}/s   ▲ {FileBrowser.FormatSize((long)network.UploadBytesPerSecond)}/s";
        _netHistory.Push(network.DownloadBytesPerSecond, network.UploadBytesPerSecond);

        RebuildTabsIfTitleChanged();
    }

    private string _lastTitles = "";

    private void RebuildTabsIfTitleChanged()
    {
        var titles = string.Join('|', _sessions.Select(s => s.Buffer.Title));
        if (titles == _lastTitles) return;
        _lastTitles = titles;
        RebuildTabs();
    }

    // ------------------------------------------------------------------ smoke test

    private async Task RunSmokeTestAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        const string marker = "EDEX_SMOKE_OK";
        var started = DateTime.UtcNow;
        bool ok = false;
        string? error = null;
        try
        {
            // Wait for the shell prompt, then type a command and wait for its output.
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(30) && (_active is null || _active.Buffer.ScrollbackText().Trim().Length == 0))
                await Task.Delay(200);
            if (_active is null) throw new InvalidOperationException("No terminal session started");
            await Task.Delay(1500);
            _active.Send($"echo {marker}\r");
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(60))
            {
                var text = _active.Buffer.ScrollbackText();
                if (CountOccurrences(text, marker) >= 2) { ok = true; break; }
                await Task.Delay(200);
            }
            await Task.Delay(2500); // let the monitors fill their charts
        }
        catch (Exception e)
        {
            error = e.ToString();
        }

        var screenshot = Path.Combine(directory, "screenshot.png");
        try
        {
            var scaling = RenderScaling;
            var size = new PixelSize((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(this);
            bitmap.Save(screenshot, PngBitmapEncoderOptions.Default);
        }
        catch (Exception e)
        {
            error = (error is null ? "" : error + "\n") + "screenshot: " + e.Message;
        }

        var terminalText = _active?.Buffer.ScrollbackText() ?? "";
        await File.WriteAllTextAsync(Path.Combine(directory, "terminal.txt"), terminalText);
        var report = new
        {
            ok,
            marker,
            shell = _settings.Shell,
            platform = _profile.Name,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            windowSize = $"{Bounds.Width}x{Bounds.Height}",
            renderScaling = RenderScaling,
            terminalSize = _active is null ? "" : $"{_active.Buffer.Columns}x{_active.Buffer.Rows}",
            cpu = _cpuValue.Text,
            memory = _memValue.Text,
            network = $"{_netState.Text} {_netIface.Text} {_netIp.Text}",
            processes = _processes.Children.Count,
            disks = _disks.Children.Count,
            elapsedSeconds = (DateTime.UtcNow - started).TotalSeconds,
            error,
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(ok ? 0 : 1);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }

    private void Shutdown()
    {
        _shutdown.Cancel();
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    /// <summary>Removes a reusable control from its previous parent so it can be placed in a rebuilt layout.</summary>
    public static T Detach<T>(this T control) where T : Control
    {
        switch (control.Parent)
        {
            case Panel panel: panel.Children.Remove(control); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
        return control;
    }
}
