// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;

namespace Edex.UI;

/// <summary>Entry point shared by the platform executables.</summary>
public static class EdexHost
{
    public static PlatformProfile Profile { get; private set; } = new() { Name = "generic" };
    public static string? SmokeTestDirectory { get; private set; }

    public static int Run(string[] args, PlatformProfile profile)
    {
        Profile = profile;
        int smoke = Array.IndexOf(args, "--smoke-test");
        if (smoke >= 0)
            SmokeTestDirectory = Path.GetFullPath(smoke + 1 < args.Length ? args[smoke + 1] : "smoke-test");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
