// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Monitoring;

public sealed record HardwareSample(string Manufacturer, string Model, string Chassis);

public static class HardwareInfo
{
    private static readonly Dictionary<string, string> ChassisNames = new (string Name, string[] Codes)[]
    {
        ("Desktop", ["3", "4", "5", "6", "7", "15", "16"]),
        ("Laptop", ["8", "9", "10", "14"]),
        ("Tablet", ["11", "30", "31", "32"]),
        ("Server", ["17", "23", "28"]),
        ("All in One", ["13"]),
        ("", [""]),
    }.SelectMany(group => group.Codes, (group, code) => (Code: code, group.Name)).ToDictionary(pair => pair.Code, pair => pair.Name, StringComparer.Ordinal);

    public static HardwareSample Read() => Safe.Run(ReadSystem, new HardwareSample("", "", ""));

    private static HardwareSample ReadSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            const string key = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
            return new HardwareSample(Registry("SystemManufacturer", key), Registry("SystemProductName", key), "");
        }
        if (OperatingSystem.IsMacOS())
        {
            return new HardwareSample("Apple", MacSysctl.ReadString("hw.model"), "");
        }

        return new HardwareSample(Dmi("sys_vendor"), Dmi("product_name"), ChassisName(Dmi("chassis_type")));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string Registry(string name, string key) =>
        Clean(Convert.ToString(Microsoft.Win32.Registry.GetValue(key, name, ""), System.Globalization.CultureInfo.InvariantCulture));

    private static string Dmi(string file)
    {
        var path = "/sys/class/dmi/id/" + file;
        return File.Exists(path) ? Clean(File.ReadAllText(path)) : "";
    }

    internal static string Clean(string? value) => value?.Trim() ?? "";

    internal static string ChassisName(string code) => ChassisNames.GetValueOrDefault(code, "Other");
}
