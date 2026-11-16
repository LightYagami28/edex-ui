// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Windows.Input;
using Edex.Core.Input;

namespace Edex.Windows;

internal static class KeyTranslator
{
    private const string ShiftedDigits = ")!@#$%^&*(";

    private static readonly Dictionary<Key, TerminalCursorKey> CursorKeys = new()
    {
        [Key.Up] = TerminalCursorKey.Up,
        [Key.Down] = TerminalCursorKey.Down,
        [Key.Left] = TerminalCursorKey.Left,
        [Key.Right] = TerminalCursorKey.Right,
        [Key.Home] = TerminalCursorKey.Home,
        [Key.End] = TerminalCursorKey.End,
        [Key.PageUp] = TerminalCursorKey.PageUp,
        [Key.PageDown] = TerminalCursorKey.PageDown,
        [Key.Delete] = TerminalCursorKey.Delete,
        [Key.Insert] = TerminalCursorKey.Insert,
    };

    private static readonly Dictionary<Key, char> Punctuation = new()
    {
        [Key.OemComma] = ',',
        [Key.OemPeriod] = '.',
        [Key.OemQuestion] = '/',
        [Key.OemSemicolon] = ';',
        [Key.OemQuotes] = '\'',
        [Key.OemOpenBrackets] = '[',
        [Key.OemCloseBrackets] = ']',
        [Key.OemPipe] = '\\',
        [Key.OemMinus] = '-',
        [Key.OemPlus] = '=',
        [Key.OemTilde] = '`',
    };

    internal static string? Translate(Key key, ModifierKeys modifiers, bool applicationCursorKeys)
    {
        string? sequence = Cursor(key, applicationCursorKeys) ?? Function(key);
        if (sequence is null)
        {
            return Special(key, modifiers) ?? Typed(key, modifiers);
        }

        return WithAlt(sequence, modifiers);
    }

    private static string WithAlt(string text, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Alt) ? TerminalKeys.Escape + text : text;

    private static string? Special(Key key, ModifierKeys modifiers) => key switch
    {
        Key.Return => TerminalKeys.Return(modifiers.HasFlag(ModifierKeys.Alt)),
        Key.Back => TerminalKeys.Backspace(modifiers.HasFlag(ModifierKeys.Alt)),
        Key.Tab => TerminalKeys.Tab(modifiers.HasFlag(ModifierKeys.Shift)),
        Key.Escape => TerminalKeys.Escape,
        Key.Space => modifiers.HasFlag(ModifierKeys.Control) ? TerminalKeys.Control(' ') : " ",
        _ => null,
    };

    private static string? Typed(Key key, ModifierKeys modifiers)
    {
        char? character = Character(key, modifiers.HasFlag(ModifierKeys.Shift));
        return character is null ? null : TypedCharacter(character.Value, modifiers);
    }

    private static string TypedCharacter(char character, ModifierKeys modifiers)
    {
        string? control = modifiers.HasFlag(ModifierKeys.Control) ? TerminalKeys.Control(character) : null;
        return control ?? WithAlt(character.ToString(CultureInfo.InvariantCulture), modifiers);
    }

    private static string? Cursor(Key key, bool applicationCursorKeys) =>
        CursorKeys.TryGetValue(key, out var cursor) ? TerminalKeys.Cursor(cursor, applicationCursorKeys) : null;

    private static string? Function(Key key) =>
        key is >= Key.F1 and <= Key.F12 ? TerminalKeys.Function(key - Key.F1 + 1) : null;

    private static char? Character(Key key, bool shift) =>
        Letter(key, shift) ?? Digit(key, shift) ?? NumPadDigit(key) ?? PunctuationMark(key);

    private static char? Letter(Key key, bool shift)
    {
        if (key is < Key.A or > Key.Z)
        {
            return null;
        }

        return (char)((shift ? 'A' : 'a') + (key - Key.A));
    }

    private static char? Digit(Key key, bool shift)
    {
        if (key is < Key.D0 or > Key.D9)
        {
            return null;
        }

        int digit = key - Key.D0;
        return shift ? ShiftedDigits[digit] : (char)('0' + digit);
    }

    private static char? NumPadDigit(Key key)
    {
        if (key is < Key.NumPad0 or > Key.NumPad9)
        {
            return null;
        }

        return (char)('0' + (key - Key.NumPad0));
    }

    private static char? PunctuationMark(Key key) => Punctuation.TryGetValue(key, out char punctuation) ? punctuation : null;
}
