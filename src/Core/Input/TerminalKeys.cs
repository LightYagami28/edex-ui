// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Input;

public enum TerminalCursorKey
{
    Up,
    Down,
    Right,
    Left,
    Home,
    End,
    PageUp,
    PageDown,
    Delete,
    Insert,
}

public static class TerminalKeys
{
    public const char Null = '\0';
    public const char BackspaceChar = '\b';
    public const char TabChar = '\t';
    public const char Escape = '\x1b';
    public const char ReturnChar = '\r';
    public const int FirstFunctionKey = 1;
    public const int LastFunctionKey = 12;
    public const int FunctionKeyOffset = 5;
    public const char ControlRangeStart = '@';
    public const char ControlRangeEnd = '_';
    public const char ControlQuestionMark = '?';
    public const char DeleteCharacter = '\x7f';

    private static readonly int[] FunctionSequences = [15, 17, 18, 19, 20, 21, 23, 24];

    public static string Cursor(TerminalCursorKey key, bool applicationCursorKeys) => key switch
    {
        TerminalCursorKey.Up => applicationCursorKeys ? $"{Escape}OA" : $"{Escape}[A",
        TerminalCursorKey.Down => applicationCursorKeys ? $"{Escape}OB" : $"{Escape}[B",
        TerminalCursorKey.Right => applicationCursorKeys ? $"{Escape}OC" : $"{Escape}[C",
        TerminalCursorKey.Left => applicationCursorKeys ? $"{Escape}OD" : $"{Escape}[D",
        TerminalCursorKey.Home => applicationCursorKeys ? $"{Escape}OH" : $"{Escape}[H",
        TerminalCursorKey.End => applicationCursorKeys ? $"{Escape}OF" : $"{Escape}[F",
        TerminalCursorKey.PageUp => $"{Escape}[5~",
        TerminalCursorKey.PageDown => $"{Escape}[6~",
        TerminalCursorKey.Delete => $"{Escape}[3~",
        TerminalCursorKey.Insert => $"{Escape}[2~",
        _ => string.Empty,
    };

    public static string Function(int number)
    {
        if (number < FirstFunctionKey || number > LastFunctionKey)
        {
            return string.Empty;
        }

        if (number == 1)
        {
            return $"{Escape}OP";
        }

        if (number == 2)
        {
            return $"{Escape}OQ";
        }

        if (number == 3)
        {
            return $"{Escape}OR";
        }

        if (number == 4)
        {
            return $"{Escape}OS";
        }

        return $"{Escape}[{FunctionSequences[number - FunctionKeyOffset]}~";
    }

    public static string? Control(char c)
    {
        char upper = char.ToUpperInvariant(c);
        if (upper is >= ControlRangeStart and <= ControlRangeEnd)
        {
            return ((char)(upper - ControlRangeStart)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (upper == ControlQuestionMark)
        {
            return DeleteCharacter.ToString();
        }

        return null;
    }

    public static string EscapeWith(string sequence) => $"{Escape}{sequence}";

    public static string? Backspace(bool alt) => alt ? $"{Escape}{BackspaceChar}" : BackspaceChar.ToString();

    public static string? Tab(bool shift) => shift ? $"{Escape}[Z" : TabChar.ToString();

    public static string? Return(bool alt) => alt ? $"{Escape}{ReturnChar}" : ReturnChar.ToString();
}
