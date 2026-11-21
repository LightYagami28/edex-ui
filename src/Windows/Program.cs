// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Edex.Windows;

internal static class Program
{
    private const string SmokeMarker = "edex-smoke-ok";
    private const int SmokeTimeoutMilliseconds = 15000;
    private const int SmokePollMilliseconds = 200;
    private const int SmokeWidth = 1280;
    private const int SmokeHeight = 800;

    private static readonly (string Flag, Func<string[], int> Run)[] Commands =
    [
        ("--version", _ => PrintVersion()),
        ("--smoke-test", SmokeTest),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        AttachConsole(AttachParentProcess);
        if (args.Contains("--help"))
        {
            Console.WriteLine("Uso: edex-ui [--version | --help | --smoke-test [--screenshot <file>]]");
            return 0;
        }

        var command = Array.Find(Commands, candidate => args.Contains(candidate.Flag));
        if (command.Run is not null)
        {
            return command.Run(args);
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Run(new EdexWindow());
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"eDEX-UI {Version()}");
        return 0;
    }

    private static int Fail(string detail)
    {
        Console.Error.WriteLine(detail);
        return 1;
    }

    private static int SmokeTest(string[] args)
    {
        string screenshot = ScreenshotPath(args);
        using var host = new AppHost();
        host.Resize(SmokeWidth, SmokeHeight);
        host.Start();
        if (host.Terminal is null)
        {
            return Fail(host.TerminalError ?? "shell non avviata");
        }

        host.Send($"echo {SmokeMarker}\r");
        if (!WaitForMarker(host))
        {
            return Fail($"marcatore '{SmokeMarker}' non trovato nel terminale");
        }

        host.Refresh();
        SaveScreenshot(host, screenshot);
        Console.WriteLine($"eDEX-UI {Version()} smoke test ok: {screenshot}");
        return 0;
    }

    private static bool WaitForMarker(AppHost host)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SmokeTimeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = host.Terminal?.Buffer.Snapshot();
            if (snapshot is not null && snapshot.Text.Contains(SmokeMarker, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(SmokePollMilliseconds);
        }

        return false;
    }

    private static void SaveScreenshot(AppHost host, string path)
    {
        var surface = new Surface(host);
        surface.Measure(new Size(SmokeWidth, SmokeHeight));
        surface.Arrange(new Rect(0, 0, SmokeWidth, SmokeHeight));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(SmokeWidth, SmokeHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string ScreenshotPath(string[] args)
    {
        int index = Array.IndexOf(args, "--screenshot");
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        return Path.Combine(Path.GetTempPath(), "edex-ui-smoke.png");
    }

    private static string Version()
    {
        var informational = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? "3.0.0";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;
}
