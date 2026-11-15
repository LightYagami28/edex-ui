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

    private static readonly string[] Modifiers = ["CTRL", "ALT", "SHIFT"];

    public static string Normalize(string keys)
    {
        var parts = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToUpperInvariant())
            .Select(part => part == "CONTROL" ? "CTRL" : part)
            .ToList();
        var key = parts.LastOrDefault(part => !Modifiers.Contains(part)) ?? "";
        return string.Concat(Modifiers.Where(parts.Contains).Select(modifier => modifier + "+")) + key;
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
