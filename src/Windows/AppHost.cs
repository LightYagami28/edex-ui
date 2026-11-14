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
    public const int TerminalColumns = 120;
    public const int TerminalRows = 32;
    public const int RefreshMilliseconds = 1000;
    public const int NetworkRefreshMilliseconds = 3000;
    public const int FileRefreshMilliseconds = 4000;

    private readonly DispatcherTimer _timer;
    private readonly SystemMonitor _monitor = new();
    private readonly NetworkMonitor _network;
    private readonly SettingsStore _store;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly KeyboardState _keyboard = new();
    private int _fileCounter;

    public AppHost()
    {
        _store = new SettingsStore();
        Settings = _store.Load();
        Theme = ThemeLibrary.Find(ThemeLibrary.Load(_store.ThemesDirectory), Settings.Theme);
        KeyboardLayout = KeyboardLayout.Find(Settings.KeyboardLayout);
        Dashboard = new Dashboard(Settings, Theme, KeyboardLayout, _keyboard);
        _network = new NetworkMonitor(Settings.PingHost);
        Renderer = new SceneRenderer(new ThemePalette(Theme));
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(RefreshMilliseconds) };
        _timer.Tick += (_, _) => Refresh();
    }

    public AppSettings Settings { get; }

    public Theme Theme { get; }

    public KeyboardLayout KeyboardLayout { get; }

    public Dashboard Dashboard { get; }

    public SceneRenderer Renderer { get; }

    public TerminalSession? Terminal { get; private set; }

    public string? TerminalError { get; private set; }

    public void Start()
    {
        StartTerminal();
        Refresh();
        _timer.Start();
    }

    public void Resize(double width, double height)
    {
        Dashboard.Resize(width, height);
        Terminal?.Resize(TerminalColumns, TerminalRows);
    }

    public void Send(string text) => Terminal?.Send(text);

    public void SendKey(KeyDefinition key)
    {
        var text = _keyboard.Press(key);
        if (text is { Length: > 0 })
        {
            Send(text);
        }
    }

    public void Refresh()
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
        RefreshNetwork();
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

    private async void RefreshNetwork()
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
