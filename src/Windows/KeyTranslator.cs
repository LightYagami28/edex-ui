// SPDX-License-Identifier: GPL-3.0-only
using System.Windows.Input;
using Edex.Core.Input;

namespace Edex.Windows;

internal static class KeyTranslator
{
    public const char AltPrefix = '\x1b';

    public static string? Translate(Key key, ModifierKeys modifiers, bool applicationCursorKeys)
    {
        bool control = modifiers.HasFlag(ModifierKeys.Control);
        bool alt = modifiers.HasFlag(ModifierKeys.Alt);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);

        string? sequence = Cursor(key, applicationCursorKeys) ?? Function(key);
        if (sequence is not null)
        {
            return alt ? AltPrefix + sequence : sequence;
        }

        switch (key)
        {
            case Key.Return:
                return TerminalKeys.Return(alt);
            case Key.Back:
                return TerminalKeys.Backspace(alt);
            case Key.Tab:
                return TerminalKeys.Tab(shift);
            case Key.Escape:
                return TerminalKeys.Escape.ToString();
            case Key.Space:
                return control ? TerminalKeys.Control(' ') : " ";
        }

        char? character = Character(key, shift);
        if (character is null)
        {
            return null;
        }

        if (control)
        {
            var controlChar = TerminalKeys.Control(character.Value);
            if (controlChar is not null)
            {
                return controlChar;
            }
        }

        string typed = character.Value.ToString();
        return alt ? AltPrefix + typed : typed;
    }

    private static string? Cursor(Key key, bool applicationCursorKeys) => key switch
    {
        Key.Up => TerminalKeys.Cursor(TerminalCursorKey.Up, applicationCursorKeys),
        Key.Down => TerminalKeys.Cursor(TerminalCursorKey.Down, applicationCursorKeys),
        Key.Left => TerminalKeys.Cursor(TerminalCursorKey.Left, applicationCursorKeys),
        Key.Right => TerminalKeys.Cursor(TerminalCursorKey.Right, applicationCursorKeys),
        Key.Home => TerminalKeys.Cursor(TerminalCursorKey.Home, applicationCursorKeys),
        Key.End => TerminalKeys.Cursor(TerminalCursorKey.End, applicationCursorKeys),
        Key.PageUp => TerminalKeys.Cursor(TerminalCursorKey.PageUp, applicationCursorKeys),
        Key.PageDown => TerminalKeys.Cursor(TerminalCursorKey.PageDown, applicationCursorKeys),
        Key.Delete => TerminalKeys.Cursor(TerminalCursorKey.Delete, applicationCursorKeys),
        Key.Insert => TerminalKeys.Cursor(TerminalCursorKey.Insert, applicationCursorKeys),
        _ => null,
    };

    private static string? Function(Key key)
    {
        int number = key switch
        {
            Key.F1 => 1, Key.F2 => 2, Key.F3 => 3, Key.F4 => 4,
            Key.F5 => 5, Key.F6 => 6, Key.F7 => 7, Key.F8 => 8,
            Key.F9 => 9, Key.F10 => 10, Key.F11 => 11, Key.F12 => 12,
            _ => 0,
        };

        return number == 0 ? null : TerminalKeys.Function(number);
    }

    private static char? Character(Key key, bool shift)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            char letter = (char)('A' + (key - Key.A));
            return shift ? letter : char.ToLowerInvariant(letter);
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            char digit = (char)('0' + (key - Key.D0));
            if (!shift)
            {
                return digit;
            }

            return key switch
            {
                Key.D1 => '!', Key.D2 => '@', Key.D3 => '#', Key.D4 => '$', Key.D5 => '%',
                Key.D6 => '^', Key.D7 => '&', Key.D8 => '*', Key.D9 => '(', Key.D0 => ')',
                _ => digit,
            };
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (char)('0' + (key - Key.NumPad0));
        }

        return key switch
        {
            Key.OemComma => ',',
            Key.OemPeriod => '.',
            Key.OemQuestion => '/',
            Key.OemSemicolon => ';',
            Key.OemQuotes => '\'',
            Key.OemOpenBrackets => '[',
            Key.OemCloseBrackets => ']',
            Key.OemPipe => '\\',
            Key.OemMinus => '-',
            Key.OemPlus => '=',
            Key.OemTilde => '`',
            _ => null,
        };
    }
}
