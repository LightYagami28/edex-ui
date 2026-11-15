// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Edex.Core.Input;

public enum KeyAction
{
    Character, Space, Enter, Backspace, Tab, Escape, Delete, Shift, Control, Alt, CapsLock, Up, Down, Left, Right, Home, End, PageUp, PageDown,
}

public sealed record KeyDefinition(string Label, string? ShiftLabel = null, KeyAction Action = KeyAction.Character, double Width = 1)
{
    public bool IsModifier => Action is KeyAction.Shift or KeyAction.Control or KeyAction.Alt or KeyAction.CapsLock;
}

public sealed record KeyboardLayout(string Name, IReadOnlyList<IReadOnlyList<KeyDefinition>> Rows)
{
    private static KeyDefinition C(string label, string shift) => new(label, shift);

    private static List<KeyDefinition> Chars(string normal, string shifted)
    {
        var list = new List<KeyDefinition>();
        var n = StringInfo.GetTextElementEnumerator(normal);
        var s = StringInfo.GetTextElementEnumerator(shifted);
        while (n.MoveNext() && s.MoveNext())
        {
            list.Add(C(n.GetTextElement(), s.GetTextElement()));
        }

        return list;
    }

    private const int HomeRow = 2;
    private const int BottomRow = 3;

    private static KeyDefinition Special(string label, KeyAction action) => new(label, Action: action);

    private static KeyDefinition Special(string label, KeyAction action, double width) => new(label, Action: action, Width: width);

    private static IReadOnlyList<IReadOnlyList<KeyDefinition>> Build(params (string normal, string shifted)[] rows) =>
    [
        Row(Special("Esc", KeyAction.Escape), rows[0], Special("⌫", KeyAction.Backspace, 1.8)),
        Row(Special("Tab", KeyAction.Tab, 1.5), rows[1], Special("Del", KeyAction.Delete, 1.3)),
        Row(Special("Caps", KeyAction.CapsLock, 1.8), rows[HomeRow], Special("Enter", KeyAction.Enter, 2.0)),
        Row(Special("Shift", KeyAction.Shift, 2.3), rows[BottomRow], Special("↑", KeyAction.Up), Special("Shift", KeyAction.Shift, 1.6)),
        [
            Special("Ctrl", KeyAction.Control, 1.6),
            Special("Alt", KeyAction.Alt, 1.4),
            Special("", KeyAction.Space, 7.4),
            Special("Home", KeyAction.Home, 1.2),
            Special("End", KeyAction.End, 1.2),
            Special("←", KeyAction.Left),
            Special("↓", KeyAction.Down),
            Special("→", KeyAction.Right),
        ],
    ];

    private static List<KeyDefinition> Row(KeyDefinition first, (string normal, string shifted) characters, params KeyDefinition[] last)
    {
        var row = new List<KeyDefinition> { first };
        row.AddRange(Chars(characters.normal, characters.shifted));
        row.AddRange(last);
        return row;
    }

    private const string ResourcePrefix = "Edex.Core.Input.Layouts.";
    private static readonly string[] JsonRows = ["row_numbers", "row_1", "row_2", "row_3"];

    public static IReadOnlyList<KeyboardLayout> All { get; } = LoadEmbedded();

    public static KeyboardLayout EnUs { get; } = All.First(l => l.Name == "en-US");

    public static KeyboardLayout ItIt { get; } = All.First(l => l.Name == "it-IT");

    private static KeyboardLayout[] LoadEmbedded()
    {
        var assembly = typeof(KeyboardLayout).Assembly;
        return [.. assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return FromJson(n[ResourcePrefix.Length..^".json".Length], reader.ReadToEnd());
            })];
    }

    public static KeyboardLayout FromJson(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = JsonRows
            .Select(row => CharacterRow(document.RootElement.GetProperty(row)))
            .ToArray();
        return new KeyboardLayout(name, Build(rows));
    }

    private static (string normal, string shifted) CharacterRow(JsonElement keys)
    {
        var normal = new StringBuilder();
        var shifted = new StringBuilder();
        foreach (var key in keys.EnumerateArray())
        {
            var cmd = key.GetProperty("cmd").ToString();
            if (cmd.Length != 1 || char.IsControl(cmd[0]))
            {
                continue;
            }
            var shift = key.TryGetProperty("shift_cmd", out var value) ? value.GetString() : null;
            normal.Append(cmd);
            shifted.Append(shift is { Length: 1 } ? shift : cmd.ToUpperInvariant());
        }
        return (normal.ToString(), shifted.ToString());
    }

    public static KeyboardLayout Find(string? name)
    {
        var match = All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        match ??= All.FirstOrDefault(l => l.Name == CultureInfo.CurrentUICulture.Name);
        if (match is not null)
        {
            return match;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? ItIt : EnUs;
    }
}

public sealed class KeyboardState
{
    private const int ShiftModifier = 1;
    private const int AltModifier = 2;
    private const int ControlModifier = 4;

    private static readonly Dictionary<KeyAction, int> TildeCodes = new()
    {
        [KeyAction.Delete] = 3,
        [KeyAction.PageUp] = 5,
        [KeyAction.PageDown] = 6,
    };

    private static readonly Dictionary<KeyAction, char> CursorFinals = new()
    {
        [KeyAction.Up] = 'A',
        [KeyAction.Down] = 'B',
        [KeyAction.Right] = 'C',
        [KeyAction.Left] = 'D',
        [KeyAction.Home] = 'H',
        [KeyAction.End] = 'F',
    };

    private static readonly Dictionary<KeyAction, Func<KeyboardState, KeyDefinition, string>> TypingOutputs = new()
    {
        [KeyAction.Character] = (state, key) => state.CharOutput(key),
        [KeyAction.Space] = (state, _) => state.SpaceOutput(),
        [KeyAction.Enter] = (state, _) => state.Alt ? "\x1b\r" : "\r",
        [KeyAction.Backspace] = (state, _) => state.BackspaceOutput(),
        [KeyAction.Tab] = (state, _) => state.Shift ? "\x1b[Z" : "\t",
        [KeyAction.Escape] = (_, _) => "\x1b",
    };

    public bool Shift { get; private set; }
    public bool Control { get; private set; }
    public bool Alt { get; private set; }
    public bool CapsLock { get; private set; }

    public string? Press(KeyDefinition key) => Press(key, false);

    public string? Press(KeyDefinition key, bool applicationCursorKeys)
    {
        if (key is null)
        {
            return null;
        }

        if (key.IsModifier)
        {
            Toggle(key.Action);
            return null;
        }

        string output = Output(key, applicationCursorKeys);
        Shift = Control = Alt = false;
        return output;
    }

    private void Toggle(KeyAction action)
    {
        Shift ^= action == KeyAction.Shift;
        Control ^= action == KeyAction.Control;
        Alt ^= action == KeyAction.Alt;
        CapsLock ^= action == KeyAction.CapsLock;
    }

    private string Output(KeyDefinition key, bool applicationCursorKeys)
    {
        int modifier = Modifier();
        if (TildeCodes.TryGetValue(key.Action, out int code))
        {
            return modifier > 1 ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";
        }

        if (CursorFinals.TryGetValue(key.Action, out char final))
        {
            return CursorSequence(final, modifier, applicationCursorKeys);
        }

        return TypingOutputs.TryGetValue(key.Action, out var typing) ? typing(this, key) : "";
    }

    private int Modifier() => 1 + (Shift ? ShiftModifier : 0) + (Alt ? AltModifier : 0) + (Control ? ControlModifier : 0);

    private static string CursorSequence(char final, int modifier, bool applicationCursorKeys)
    {
        if (modifier > 1)
        {
            return $"\x1b[1;{modifier}{final}";
        }

        return applicationCursorKeys ? $"\x1bO{final}" : $"\x1b[{final}";
    }

    private string SpaceOutput()
    {
        if (Control)
        {
            return "\0";
        }

        return Alt ? "\x1b " : " ";
    }

    private string BackspaceOutput()
    {
        if (Control)
        {
            return "\x08";
        }

        return Alt ? "\x1b\x7f" : "\x7f";
    }

    public string Display(KeyDefinition key)
    {
        if (key.Action != KeyAction.Character)
        {
            return key.Label;
        }

        return IsUpper(key) ? key.ShiftLabel ?? key.Label.ToUpperInvariant() : key.Label;
    }

    private bool IsUpper(KeyDefinition key) => IsLetter(key.Label) ? Shift ^ CapsLock : Shift;

    private static bool IsLetter(string label) => label.Length == 1 && char.IsLetter(label[0]);

    private string CharOutput(KeyDefinition key)
    {
        string text = Display(key);
        if (Control && text.Length == 1)
        {
            text = ControlChar(char.ToUpperInvariant(text[0]), text);
        }
        return Alt ? "\x1b" + text : text;
    }

    private static string ControlChar(char c, string fallback)
    {
        if (c is >= '@' and <= '_')
        {
            return ((char)(c - '@')).ToString(CultureInfo.InvariantCulture);
        }

        return c == '?' ? "\x7f" : fallback;
    }
}
