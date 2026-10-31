// SPDX-License-Identifier: GPL-3.0-only
using Edex.UI;

namespace Edex.MacOS;

internal static class Program
{
    public static int Main(string[] args) => EdexHost.Run(args, new PlatformProfile
    {
        Name = "macOS",
        UseCommandKey = true,
        PreferredMonospaceFonts = "SF Mono, Menlo, Monaco, JetBrains Mono, monospace",
    });
}
