// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Terminal;

[Flags]
public enum CellAttributes : ushort
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Hidden = 32,
    Strikethrough = 64,
}

/// <summary>
/// One character cell. Colors are encoded as: 0 = default,
/// 0x01000000 | index = palette entry (0-255), 0x02000000 | RRGGBB = true color.
/// </summary>
public readonly record struct TerminalCell(char Char, uint Foreground, uint Background, CellAttributes Attributes)
{
    public static readonly TerminalCell Blank = new(' ', 0, 0, CellAttributes.None);

    public const uint PaletteFlag = 0x01000000;
    public const uint RgbFlag = 0x02000000;

    public static uint Palette(int index) => PaletteFlag | (uint)(index & 0xFF);
    public static uint Rgb(int r, int g, int b) => RgbFlag | (uint)((r & 0xFF) << 16 | (g & 0xFF) << 8 | (b & 0xFF));
}

/// <summary>An immutable copy of the visible screen used by renderers.</summary>
public sealed class TerminalSnapshot
{
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required TerminalCell[][] Lines { get; init; }
    public required int CursorX { get; init; }
    public required int CursorY { get; init; }
    public required bool CursorVisible { get; init; }
    public required int ScrollOffset { get; init; }
    public required int ScrollbackLength { get; init; }

    public string LineText(int row) => new string(Lines[row].Select(c => c.Char == '\0' ? ' ' : c.Char).ToArray()).TrimEnd();

    public string Text => string.Join('\n', Enumerable.Range(0, Rows).Select(LineText)).TrimEnd('\n');
}
