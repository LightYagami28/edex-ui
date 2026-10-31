// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;

namespace Edex.Core.Settings;

public sealed record AppSettings
{
    public string Shell { get; init; } = DefaultShell();
    public List<string> ShellArgs { get; init; } = [];
    public string? StartDirectory { get; init; }
    public string Theme { get; init; } = "tron";
    public double FontSize { get; init; } = 14;
    public int Scrollback { get; init; } = 2000;
    public bool ShowHiddenFiles { get; init; }
    public string PingHost { get; init; } = "1.1.1.1";
    public int MonitorIntervalMs { get; init; } = 1000;
    public bool ShowKeyboard { get; init; } = true;
    /// <summary>"auto", "en-US" or "it-IT".</summary>
    public string KeyboardLayout { get; init; } = "auto";
    public bool Sounds { get; init; } = true;
    public bool KeyboardSounds { get; init; } = true;
    public double SoundVolume { get; init; } = 0.4;
    /// <summary>Look up the public IP location (no API key) for the globe; false keeps everything offline.</summary>
    public bool OnlineGeolocation { get; init; } = true;

    public static string DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, "pwsh.exe"))) return "pwsh.exe";
                }
                catch (ArgumentException) { }
            }
            return "powershell.exe";
        }
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(shell)) return shell;
        return OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
    }
}

/// <summary>Loads and saves settings as JSON. Location: $EDEX_CONFIG_DIR or the per-user application data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, "settings.json");
    public string ThemesDirectory => Path.Combine(Directory, "themes");

    public SettingsStore(string? directory = null)
    {
        Directory = directory
            ?? Environment.GetEnvironmentVariable("EDEX_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "eDEX-UI-Next");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(ThemesDirectory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
