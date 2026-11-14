// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Edex.Windows;

internal static class Program
{
    public const string SmokeMarker = "edex-smoke-ok";
    public const int SmokeTimeoutMilliseconds = 15000;
    public const int SmokePollMilliseconds = 200;
    public const int SmokeWidth = 1280;
    public const int SmokeHeight = 800;

    [STAThread]
    private static int Main(string[] args)
    {
        AttachConsole(AttachParentProcess);

        if (args.Contains("--version"))
        {
            Console.WriteLine($"eDEX-UI {Version()}");
            return 0;
        }

        if (args.Contains("--help"))
        {
            Console.WriteLine("Uso: edex-ui [--version | --help | --smoke-test [--screenshot <file>]]");
            return 0;
        }

        if (args.Contains("--smoke-test"))
        {
            return SmokeTest(args);
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Run(new EdexWindow());
        return 0;
    }

    private static int SmokeTest(string[] args)
    {
        string screenshot = ScreenshotPath(args);
        using var host = new AppHost();
        host.Resize(SmokeWidth, SmokeHeight);
        host.Start();

        if (host.Terminal is null)
        {
            Console.Error.WriteLine(host.TerminalError ?? "shell non avviata");
            return 1;
        }

        host.Send($"echo {SmokeMarker}\r");
        if (!WaitForMarker(host))
        {
            Console.Error.WriteLine($"marcatore '{SmokeMarker}' non trovato nel terminale");
            return 1;
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
        var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? "3.0.0";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;
}
