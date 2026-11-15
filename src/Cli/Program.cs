// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;

[assembly: CLSCompliant(true)]

const int UsageError = 2;
var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

if (args.Contains("--version"))
{
    Console.WriteLine($"eDEX-UI {version}");
    return 0;
}

Console.WriteLine($"eDEX-UI {version}");
Console.WriteLine("Uso: edex-ui [--version | --help]");
return args.Length == 0 || args.Contains("--help") ? 0 : UsageError;
