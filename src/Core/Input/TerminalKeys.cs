// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

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
    private const char EscapeChar = '\x1b';
    private const char BackspaceChar = '\b';
    private const char TabChar = '\t';
    private const char ReturnChar = '\r';
    private const char ControlRangeStart = '@';
    private const char ControlRangeEnd = '_';
    private const char ControlQuestionMark = '?';
    private const char DeleteCharacter = '\x7f';

    private static readonly Dictionary<TerminalCursorKey, (string Normal, string Application)> CursorSequences = new()
    {
        [TerminalCursorKey.Up] = ("\x1b[A", "\x1bOA"),
        [TerminalCursorKey.Down] = ("\x1b[B", "\x1bOB"),
        [TerminalCursorKey.Right] = ("\x1b[C", "\x1bOC"),
        [TerminalCursorKey.Left] = ("\x1b[D", "\x1bOD"),
        [TerminalCursorKey.Home] = ("\x1b[H", "\x1bOH"),
        [TerminalCursorKey.End] = ("\x1b[F", "\x1bOF"),
        [TerminalCursorKey.PageUp] = ("\x1b[5~", "\x1b[5~"),
        [TerminalCursorKey.PageDown] = ("\x1b[6~", "\x1b[6~"),
        [TerminalCursorKey.Delete] = ("\x1b[3~", "\x1b[3~"),
        [TerminalCursorKey.Insert] = ("\x1b[2~", "\x1b[2~"),
    };

    private static readonly string[] FunctionSequences =
    [
        "\x1bOP", "\x1bOQ", "\x1bOR", "\x1bOS",
        "\x1b[15~", "\x1b[17~", "\x1b[18~", "\x1b[19~",
        "\x1b[20~", "\x1b[21~", "\x1b[23~", "\x1b[24~",
    ];

    public static string Escape { get; } = EscapeChar.ToString(CultureInfo.InvariantCulture);

    public static string Cursor(TerminalCursorKey key, bool applicationCursorKeys)
    {
        if (!CursorSequences.TryGetValue(key, out var sequences))
        {
            return string.Empty;
        }

        return applicationCursorKeys ? sequences.Application : sequences.Normal;
    }

    public static string Function(int number)
    {
        int index = number - 1;
        return index >= 0 && index < FunctionSequences.Length ? FunctionSequences[index] : string.Empty;
    }

    public static string? Control(char c)
    {
        char upper = char.ToUpperInvariant(c);
        if (upper is >= ControlRangeStart and <= ControlRangeEnd)
        {
            return ((char)(upper - ControlRangeStart)).ToString(CultureInfo.InvariantCulture);
        }

        return upper == ControlQuestionMark ? DeleteCharacter.ToString(CultureInfo.InvariantCulture) : null;
    }

    public static string EscapeWith(string sequence) => $"{EscapeChar}{sequence}";

    public static string? Backspace(bool alt) => alt ? $"{EscapeChar}{BackspaceChar}" : BackspaceChar.ToString(CultureInfo.InvariantCulture);

    public static string? Tab(bool shift) => shift ? $"{EscapeChar}[Z" : TabChar.ToString(CultureInfo.InvariantCulture);

    public static string? Return(bool alt) => alt ? $"{EscapeChar}{ReturnChar}" : ReturnChar.ToString(CultureInfo.InvariantCulture);
}
