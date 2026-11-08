// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Edex.Core.Input;

public enum KeyAction
{
    Character, Space, Enter, Backspace, Tab, Escape, Delete, Shift, Control, Alt, CapsLock, Up, Down, Left, Right, Home, End, PageUp, PageDown,
}

/// <summary>One key: label, label with Shift, behaviour and relative width (1 = standard key).</summary>
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
        while (n.MoveNext() && s.MoveNext()) list.Add(C(n.GetTextElement(), s.GetTextElement()));
        return list;
    }

    private static IReadOnlyList<IReadOnlyList<KeyDefinition>> Build(params (string normal, string shifted)[] rows)
    {
        const int homeRow = 2;
        const int bottomRow = 3;

        var row1 = new List<KeyDefinition> { new("Esc", null, KeyAction.Escape) };
        row1.AddRange(Chars(rows[0].normal, rows[0].shifted));
        row1.Add(new KeyDefinition("⌫", null, KeyAction.Backspace, 1.8));

        var row2 = new List<KeyDefinition> { new("Tab", null, KeyAction.Tab, 1.5) };
        row2.AddRange(Chars(rows[1].normal, rows[1].shifted));
        row2.Add(new KeyDefinition("Del", null, KeyAction.Delete, 1.3));

        var row3 = new List<KeyDefinition> { new("Caps", null, KeyAction.CapsLock, 1.8) };
        row3.AddRange(Chars(rows[homeRow].normal, rows[homeRow].shifted));
        row3.Add(new KeyDefinition("Enter", null, KeyAction.Enter, 2.0));

        var row4 = new List<KeyDefinition> { new("Shift", null, KeyAction.Shift, 2.3) };
        row4.AddRange(Chars(rows[bottomRow].normal, rows[bottomRow].shifted));
        row4.Add(new KeyDefinition("↑", null, KeyAction.Up));
        row4.Add(new KeyDefinition("Shift", null, KeyAction.Shift, 1.6));

        var row5 = new List<KeyDefinition>
        {
            new("Ctrl", null, KeyAction.Control, 1.6),
            new("Alt", null, KeyAction.Alt, 1.4),
            new("", null, KeyAction.Space, 7.4),
            new("Home", null, KeyAction.Home, 1.2),
            new("End", null, KeyAction.End, 1.2),
            new("←", null, KeyAction.Left),
            new("↓", null, KeyAction.Down),
            new("→", null, KeyAction.Right),
        };
        return [row1, row2, row3, row4, row5];
    }

    private const string ResourcePrefix = "Edex.Core.Input.Layouts.";
    private static readonly string[] JsonRows = ["row_numbers", "row_1", "row_2", "row_3"];

    /// <summary>The eDEX-UI layouts of <c>assets/kb_layouts</c>, embedded in the library.</summary>
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

    /// <summary>Reads an eDEX-UI layout file: the character keys of its four main rows; special keys come from <see cref="Build"/>.</summary>
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
            // Tab, Enter, Shift, arrows and the other special keys have control or multi-character commands.
            if (cmd.Length != 1 || char.IsControl(cmd[0])) continue;
            var shift = key.TryGetProperty("shift_cmd", out var value) ? value.GetString() : null;
            normal.Append(cmd);
            shifted.Append(shift is { Length: 1 } ? shift : cmd.ToUpperInvariant());
        }
        return (normal.ToString(), shifted.ToString());
    }

    /// <summary>Layout by name; "auto" (or unknown) picks one from the UI culture.</summary>
    public static KeyboardLayout Find(string? name)
    {
        var match = All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        match ??= All.FirstOrDefault(l => l.Name == CultureInfo.CurrentUICulture.Name);
        if (match is not null) return match;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? ItIt : EnUs;
    }
}

/// <summary>Turns on-screen key presses into terminal input. Shift, Ctrl and Alt are one-shot; Caps Lock toggles.</summary>
public sealed class KeyboardState
{
    public bool Shift { get; private set; }
    public bool Control { get; private set; }
    public bool Alt { get; private set; }
    public bool CapsLock { get; private set; }

    private const int deleteSequence = 3;
    private const int pageUpSequence = 5;
    private const int pageDownSequence = 6;

    /// <summary>Returns the bytes to send, or null when the key only changed a modifier.</summary>
    public string? Press(KeyDefinition key, bool applicationCursorKeys = false)
    {
        if (key == null)
        {
            return null;
        }

        switch (key.Action)
        {
            case KeyAction.Shift: Shift = !Shift; return null;
            case KeyAction.Control: Control = !Control; return null;
            case KeyAction.Alt: Alt = !Alt; return null;
            case KeyAction.CapsLock: CapsLock = !CapsLock; return null;
        }

        int modifier = 1 + (Shift ? 1 : 0) + (Alt ? 2 : 0) + (Control ? 4 : 0);
        string Cursor(char final)
        {
            if (modifier > 1)
            {
                return $"\x1b[1;{modifier}{final}";
            }
            return applicationCursorKeys ? $"\x1bO{final}" : $"\x1b[{final}";
        }
        string Tilde(int code) => modifier > 1 ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";

        string output = CreateOutput(key, Cursor, Tilde);
        Shift = Control = Alt = false;
        return output;
    }

    private String CreateOutput(KeyDefinition key, Func<char, string> cursor, Func<int, string> tilde)
    {
        if (key == null)
        {
            return "";
        }

        String? navigation = NavigationOutput(key.Action, cursor, tilde);
        if (navigation != null)
        {
            return navigation;
        }

        return TypingOutput(key);
    }

    private static String? NavigationOutput(KeyAction action, Func<char, string> cursor, Func<int, string> tilde)
    {
        KeyAction current = action;
        String? edited = EditedKeyOutput(current, tilde);
        if (edited != null)
        {
            return edited;
        }

        return ArrowOutput(current, cursor);
    }

    private static String? EditedKeyOutput(KeyAction action, Func<int, string> tilde)
    {
        switch (action)
        {
            case KeyAction.Delete:
                return tilde(deleteSequence);
            case KeyAction.PageUp:
                return tilde(pageUpSequence);
            case KeyAction.PageDown:
                return tilde(pageDownSequence);
            default:
                return null;
        }
    }

    private static String? ArrowOutput(KeyAction action, Func<char, string> cursor)
    {
        KeyAction current = action;
        switch (current)
        {
            case KeyAction.Up:
                return cursor('A');
            case KeyAction.Down:
                return cursor('B');
            case KeyAction.Right:
                return cursor('C');
            default:
                return OtherArrowOutput(current, cursor);
        }
    }

    private static String? OtherArrowOutput(KeyAction action, Func<char, string> cursor)
    {
        switch (action)
        {
            case KeyAction.Left:
                return cursor('D');
            case KeyAction.Home:
                return cursor('H');
            case KeyAction.End:
                return cursor('F');
            default:
                return null;
        }
    }

    private String TypingOutput(KeyDefinition key)
    {
        if (key == null)
        {
            return "";
        }

        switch (key.Action)
        {
            case KeyAction.Character:
                return CharOutput(key);
            case KeyAction.Space:
                return SpaceOutput();
            case KeyAction.Enter:
                return EnterOutput();
            default:
                return MoreTyping(key);
        }
    }

    private String MoreTyping(KeyDefinition key)
    {
        if (key == null)
        {
            return "";
        }

        switch (key.Action)
        {
            case KeyAction.Backspace:
                return BackspaceOutput();
            case KeyAction.Tab:
                return TabOutput();
            case KeyAction.Escape:
                return "\x1b";
            default:
                return "";
        }
    }

    private String SpaceOutput()
    {
        if (Control)
        {
            return "\0";
        }

        if (Alt)
        {
            return "\x1b ";
        }

        return " ";
    }

    private String EnterOutput()
    {
        if (Alt)
        {
            return "\x1b\r";
        }

        return "\r";
    }

    private String BackspaceOutput()
    {
        if (Control)
        {
            return "\x08";
        }

        if (Alt)
        {
            return "\x1b\x7f";
        }

        return "\x7f";
    }

    private String TabOutput()
    {
        if (Shift)
        {
            return "\x1b[Z";
        }

        return "\t";
    }

    /// <summary>The label a character key shows with the current modifiers.</summary>
    public string Display(KeyDefinition key)
    {
        if (key.Action != KeyAction.Character) return key.Label;
        bool letter = key.Label.Length == 1 && char.IsLetter(key.Label[0]);
        bool upper = letter ? Shift ^ CapsLock : Shift;
        return upper ? key.ShiftLabel ?? key.Label.ToUpperInvariant() : key.Label;
    }

    private string CharOutput(KeyDefinition key)
    {
        string text = Display(key);
        if (Control && text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if (c is >= '@' and <= '_') text = ((char)(c - '@')).ToString();
            else if (c == '?') text = "\x7f";
        }
        return Alt ? "\x1b" + text : text;
    }
}
