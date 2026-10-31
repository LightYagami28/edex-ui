// SPDX-License-Identifier: GPL-3.0-only
using Edex.UI;

namespace Edex.Linux;

internal static class Program
{
    public static int Main(string[] args) => EdexHost.Run(args, new PlatformProfile
    {
        Name = "Linux",
        UseCommandKey = false,
        PreferredMonospaceFonts = "JetBrains Mono, Fira Code, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace",
    });
}
