// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Edex.Core.Input;

public enum KeyAction
{
    Char, Space, Enter, Backspace, Tab, Escape, Delete, Shift, Control, Alt, CapsLock, Up, Down, Left, Right, Home, End, PageUp, PageDown,
}

/// <summary>One key: label, label with Shift, behaviour and relative width (1 = standard key).</summary>
public sealed record KeyDefinition(string Label, string? ShiftLabel = null, KeyAction Action = KeyAction.Char, double Width = 1)
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

    private static IReadOnlyList<IReadOnlyList<KeyDefinition>> Build(string r1, string r1s, string r2, string r2s, string r3, string r3s, string r4, string r4s)
    {
        var row1 = new List<KeyDefinition> { new("Esc", null, KeyAction.Escape) };
        row1.AddRange(Chars(r1, r1s));
        row1.Add(new KeyDefinition("⌫", null, KeyAction.Backspace, 1.8));

        var row2 = new List<KeyDefinition> { new("Tab", null, KeyAction.Tab, 1.5) };
        row2.AddRange(Chars(r2, r2s));
        row2.Add(new KeyDefinition("Del", null, KeyAction.Delete, 1.3));

        var row3 = new List<KeyDefinition> { new("Caps", null, KeyAction.CapsLock, 1.8) };
        row3.AddRange(Chars(r3, r3s));
        row3.Add(new KeyDefinition("Enter", null, KeyAction.Enter, 2.0));

        var row4 = new List<KeyDefinition> { new("Shift", null, KeyAction.Shift, 2.3) };
        row4.AddRange(Chars(r4, r4s));
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

    public static readonly KeyboardLayout EnUs = new("en-US", Build(
        "`1234567890-=", "~!@#$%^&*()_+",
        "qwertyuiop[]\\", "QWERTYUIOP{}|",
        "asdfghjkl;'", "ASDFGHJKL:\"",
        "zxcvbnm,./", "ZXCVBNM<>?"));

    public static readonly KeyboardLayout ItIt = new("it-IT", Build(
        "\\1234567890'ì", "|!\"£$%&/()=?^",
        "qwertyuiopè+ù", "QWERTYUIOPé*§",
        "asdfghjklòà", "ASDFGHJKLç°",
        "<zxcvbnm,.-", ">ZXCVBNM;:_"));

    public static IReadOnlyList<KeyboardLayout> All { get; } = [EnUs, ItIt];

    /// <summary>Layout by name; "auto" (or unknown) picks one from the UI culture.</summary>
    public static KeyboardLayout Find(string? name)
    {
        var match = All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Returns the bytes to send, or null when the key only changed a modifier.</summary>
    public string? Press(KeyDefinition key, bool applicationCursorKeys = false)
    {
        switch (key.Action)
        {
            case KeyAction.Shift: Shift = !Shift; return null;
            case KeyAction.Control: Control = !Control; return null;
            case KeyAction.Alt: Alt = !Alt; return null;
            case KeyAction.CapsLock: CapsLock = !CapsLock; return null;
        }

        int modifier = 1 + (Shift ? 1 : 0) + (Alt ? 2 : 0) + (Control ? 4 : 0);
        string Cursor(char final) => modifier > 1 ? $"\x1b[1;{modifier}{final}" : applicationCursorKeys ? $"\x1bO{final}" : $"\x1b[{final}";
        string Tilde(int code) => modifier > 1 ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";

        string output = key.Action switch
        {
            KeyAction.Char => CharOutput(key),
            KeyAction.Space => Control ? "\0" : Alt ? "\x1b " : " ",
            KeyAction.Enter => Alt ? "\x1b\r" : "\r",
            KeyAction.Backspace => Control ? "\x08" : Alt ? "\x1b\x7f" : "\x7f",
            KeyAction.Tab => Shift ? "\x1b[Z" : "\t",
            KeyAction.Escape => "\x1b",
            KeyAction.Delete => Tilde(3),
            KeyAction.Up => Cursor('A'),
            KeyAction.Down => Cursor('B'),
            KeyAction.Right => Cursor('C'),
            KeyAction.Left => Cursor('D'),
            KeyAction.Home => Cursor('H'),
            KeyAction.End => Cursor('F'),
            KeyAction.PageUp => Tilde(5),
            KeyAction.PageDown => Tilde(6),
            _ => "",
        };
        Shift = Control = Alt = false;
        return output;
    }

    /// <summary>The label a character key shows with the current modifiers.</summary>
    public string Display(KeyDefinition key)
    {
        if (key.Action != KeyAction.Char) return key.Label;
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
