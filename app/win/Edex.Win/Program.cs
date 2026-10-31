// SPDX-License-Identifier: GPL-3.0-only
using Edex.UI;

namespace Edex.Win;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => EdexHost.Run(args, new PlatformProfile
    {
        Name = "Windows",
        UseCommandKey = false,
        PreferredMonospaceFonts = "Cascadia Mono, Cascadia Code, Consolas, Lucida Console, monospace",
        FileIcons = ShellIcons.Get,
    });
}
