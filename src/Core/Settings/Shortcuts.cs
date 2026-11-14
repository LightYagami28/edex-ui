// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;

namespace Edex.Core.Settings;

public sealed record Shortcut(string Keys, string Action, string? Command = null, bool Enabled = true);

public static class Shortcuts
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static IReadOnlyList<Shortcut> Defaults { get; } =
    [
        new("Ctrl+Shift+C", "copy"),
        new("Ctrl+Shift+V", "paste"),
        new("Ctrl+Tab", "nextTab"),
        new("Ctrl+Shift+Tab", "previousTab"),
        new("Ctrl+1", "tab1"),
        new("Ctrl+2", "tab2"),
        new("Ctrl+3", "tab3"),
        new("Ctrl+4", "tab4"),
        new("Ctrl+5", "tab5"),
        new("Ctrl+Shift+S", "settings"),
        new("Ctrl+Shift+K", "shortcuts"),
        new("Ctrl+Shift+F", "fuzzyFinder"),
        new("Ctrl+Shift+L", "listView"),
        new("Ctrl+Shift+H", "dotfiles"),
        new("Ctrl+Shift+P", "password"),
    ];

    public static string Normalize(string keys)
    {
        bool ctrl = false, alt = false, shift = false;
        string key = "";
        foreach (var part in keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": ctrl = true; break;
                case "ALT": alt = true; break;
                case "SHIFT": shift = true; break;
                default: key = part.ToUpperInvariant(); break;
            }
        }
        return (ctrl ? "CTRL+" : "") + (alt ? "ALT+" : "") + (shift ? "SHIFT+" : "") + key;
    }

    public static IReadOnlyList<Shortcut> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Defaults;
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<Shortcut>>(json, JsonOptions);
            if (list is null)
            {
                return Defaults;
            }

            return list.Where(s => !string.IsNullOrWhiteSpace(s.Keys) && !string.IsNullOrWhiteSpace(s.Action)).ToList();
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    public static string Serialize(IEnumerable<Shortcut> shortcuts) => JsonSerializer.Serialize(shortcuts, JsonOptions);

    public static Shortcut? Find(IEnumerable<Shortcut> shortcuts, string keys)
    {
        var wanted = Normalize(keys);
        return shortcuts.FirstOrDefault(s => s.Enabled && Normalize(s.Keys) == wanted);
    }
}
