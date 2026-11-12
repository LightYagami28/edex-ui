// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Monitoring;

public sealed record HardwareSample(string Manufacturer, string Model, string Chassis);

public static class HardwareInfo
{
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

    /// <summary>SMBIOS chassis type number to a name.</summary>
    internal static string ChassisName(string code) => code switch
    {
        "3" or "4" or "5" or "6" or "7" or "15" or "16" => "Desktop",
        "8" or "9" or "10" or "14" => "Laptop",
        "11" or "30" or "31" or "32" => "Tablet",
        "17" or "23" or "28" => "Server",
        "13" => "All in One",
        "" => "",
        _ => "Other",
    };
}
