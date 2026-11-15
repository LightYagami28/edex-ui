// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Edex.Core.Files;
using Edex.Core.Input;
using Edex.Core.Monitoring;
using Edex.Core.Networking;
using Edex.Core.Pty;
using Edex.Core.Settings;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Edex.Core.Ui;

namespace Edex.Windows;

internal sealed class AppHost : IDisposable
{
    private const int TerminalColumns = 120;
    private const int TerminalRows = 32;
    private const int RefreshMilliseconds = 1000;
    private const int NetworkRefreshMilliseconds = 3000;
    private const int FileRefreshMilliseconds = 4000;

    private readonly DispatcherTimer _timer;
    private readonly SystemMonitor _monitor = new();
    private readonly NetworkMonitor _network;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly KeyboardState _keyboard = new();
    private int _fileCounter;

    internal AppHost()
    {
        var store = new SettingsStore();
        Settings = store.Load();
        Theme = ThemeLibrary.Find(ThemeLibrary.Load(store.ThemesDirectory), Settings.Theme);
        KeyboardLayout = KeyboardLayout.Find(Settings.KeyboardLayout);
        Dashboard = new Dashboard(Settings, Theme, KeyboardLayout, _keyboard);
        _network = new NetworkMonitor(Settings.PingHost);
        Renderer = new SceneRenderer(new ThemePalette(Theme));
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(RefreshMilliseconds) };
        _timer.Tick += (_, _) => Refresh();
    }

    internal AppSettings Settings { get; }

    internal Theme Theme { get; }

    internal KeyboardLayout KeyboardLayout { get; }

    internal Dashboard Dashboard { get; }

    internal SceneRenderer Renderer { get; }

    internal TerminalSession? Terminal { get; private set; }

    internal string? TerminalError { get; private set; }

    internal void Start()
    {
        StartTerminal();
        Refresh();
        _timer.Start();
    }

    internal void Resize(double width, double height)
    {
        Dashboard.Resize(width, height);
        Terminal?.Resize(TerminalColumns, TerminalRows);
    }

    internal void Send(string text) => Terminal?.Send(text);

    internal void SendKey(KeyDefinition key)
    {
        var text = _keyboard.Press(key);
        if (text is { Length: > 0 })
        {
            Send(text);
        }
    }

    internal void Refresh()
    {
        try
        {
            Dashboard.AttachSystem(_monitor.Sample());
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            // Il campionamento del sistema può non essere disponibile su ogni piattaforma.
        }

        Dashboard.PushSample();
        if (Terminal is { } terminal)
        {
            Dashboard.AttachTerminal(terminal.Buffer.Snapshot());
        }

        if (_fileCounter % FileRefreshMilliseconds == 0)
        {
            RefreshListing();
        }

        _fileCounter += RefreshMilliseconds;
        _ = RefreshNetworkAsync();
    }

    public void Dispose()
    {
        _timer.Stop();
        Terminal?.Dispose();
        _http.Dispose();
    }

    private void StartTerminal()
    {
        var options = new PtyOptions
        {
            Shell = Settings.Shell,
            Args = Settings.ShellArgs,
            WorkingDirectory = Settings.StartDirectory,
            Columns = TerminalColumns,
            Rows = TerminalRows,
        };

        try
        {
            Terminal = TerminalSession.Start(options, Settings.Scrollback);
            Terminal.Updated += (_, _) => Dashboard.AttachTerminal(Terminal!.Buffer.Snapshot());
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            TerminalError = $"shell non avviata: {e.Message}";
        }
    }

    private void RefreshListing()
    {
        string path = Settings.StartDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Dashboard.AttachListing(FileBrowser.List(path, Settings.ShowHiddenFiles));
    }

    private async Task RefreshNetworkAsync()
    {
        try
        {
            var snapshot = await _network.SampleAsync().ConfigureAwait(true);
            Dashboard.AttachNetwork(snapshot);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException)
        {
            // La rete può non rispondere: i pannelli restano con gli ultimi valori.
        }
    }
}
