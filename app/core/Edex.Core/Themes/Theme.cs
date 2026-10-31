// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edex.Core.Themes;

/// <summary>A color scheme for the whole UI. Colors are "#RRGGBB" strings.</summary>
public sealed record Theme
{
    public string Name { get; init; } = "";
    public required string Accent { get; init; }
    public required string Background { get; init; }
    public required string Surface { get; init; }
    public required string Foreground { get; init; }
    public required string Muted { get; init; }
    public string Danger { get; init; } = "#FF5370";
    public string TerminalForeground { get; init; } = "";
    public string TerminalBackground { get; init; } = "";
    public string Cursor { get; init; } = "";
    public string FontFamily { get; init; } = "Cascadia Mono, Consolas, JetBrains Mono, DejaVu Sans Mono, Menlo, monospace";

    /// <summary>16 ANSI colors (normal 0-7, bright 8-15).</summary>
    public IReadOnlyList<string> Ansi { get; init; } = ThemeLibrary.DefaultAnsi;

    [JsonIgnore] public string EffectiveTerminalForeground => string.IsNullOrEmpty(TerminalForeground) ? Foreground : TerminalForeground;
    [JsonIgnore] public string EffectiveTerminalBackground => string.IsNullOrEmpty(TerminalBackground) ? Background : TerminalBackground;
    [JsonIgnore] public string EffectiveCursor => string.IsNullOrEmpty(Cursor) ? Accent : Cursor;
}

public static class ThemeLibrary
{
    public static readonly IReadOnlyList<string> DefaultAnsi =
    [
        "#1B1E25", "#FF5370", "#C3E88D", "#FFCB6B", "#82AAFF", "#C792EA", "#89DDFF", "#D0D7DE",
        "#545C6E", "#FF757F", "#DDFFA7", "#FFE585", "#9CC4FF", "#E1ACFF", "#A3F7FF", "#FFFFFF",
    ];

    public static IReadOnlyList<Theme> BuiltIn { get; } =
    [
        new Theme { Name = "tron", Accent = "#AACFD1", Background = "#05080D", Surface = "#0B141A", Foreground = "#D6F1F2", Muted = "#5D7C80" },
        new Theme { Name = "amber", Accent = "#FFB000", Background = "#0D0900", Surface = "#1A1200", Foreground = "#FFD580", Muted = "#8A6A2A" },
        new Theme { Name = "matrix", Accent = "#39FF6A", Background = "#020A04", Surface = "#06160A", Foreground = "#B6FFC8", Muted = "#3D7A4C" },
        new Theme { Name = "crimson", Accent = "#FF3B5C", Background = "#0C0306", Surface = "#1A070C", Foreground = "#FFD3DA", Muted = "#7F3A47" },
        new Theme { Name = "glacier", Accent = "#7AA2F7", Background = "#0B0E17", Surface = "#121726", Foreground = "#C0CAF5", Muted = "#565F89" },
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Built-in themes followed by valid *.json themes from <paramref name="directory"/> (same name overrides).</summary>
    public static IReadOnlyList<Theme> Load(string? directory)
    {
        var themes = BuiltIn.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var theme = TryParse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
                if (theme is not null) themes[theme.Name] = theme;
            }
        }
        return themes.Values.ToList();
    }

    public static Theme? TryParse(string json, string fallbackName)
    {
        try
        {
            var theme = JsonSerializer.Deserialize<Theme>(json, JsonOptions);
            if (theme is null) return null;
            if (string.IsNullOrWhiteSpace(theme.Name)) theme = theme with { Name = fallbackName };
            string[] colors = [theme.Accent, theme.Background, theme.Surface, theme.Foreground, theme.Muted];
            if (!colors.All(ColorUtil.IsValidHex) || theme.Ansi.Count != 16 || !theme.Ansi.All(ColorUtil.IsValidHex)) return null;
            return theme;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(Theme theme) => JsonSerializer.Serialize(theme, JsonOptions);

    public static Theme Find(IEnumerable<Theme> themes, string? name) =>
        themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? BuiltIn[0];
}

public static class ColorUtil
{
    public static bool IsValidHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        if (!IsValidHex(hex)) return (255, 255, 255);
        uint value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>RGB of an xterm 256 color palette entry (16-255); 0-15 come from the theme.</summary>
    public static (byte R, byte G, byte B) Xterm256(int index)
    {
        if (index < 16) return (0, 0, 0);
        if (index < 232)
        {
            int i = index - 16;
            static byte Level(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
            return (Level(i / 36), Level(i / 6 % 6), Level(i % 6));
        }
        byte gray = (byte)(8 + (index - 232) * 10);
        return (gray, gray, gray);
    }
}
