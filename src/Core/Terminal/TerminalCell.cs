// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Terminal;

[Flags]
public enum CellAttributes
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

public readonly record struct TerminalCell(char Glyph, uint Foreground, uint Background, CellAttributes Attributes)
{
    private const uint PaletteBit = 0x01000000;
    private const uint RgbBit = 0x02000000;
    private const int ChannelMask = 0xFF;
    private const int RedShift = 16;
    private const int GreenShift = 8;

    public static readonly TerminalCell Blank = new(' ', 0, 0, CellAttributes.None);

    public static uint PaletteFlag => PaletteBit;
    public static uint RgbFlag => RgbBit;

    public static uint Palette(int index) => PaletteBit | (uint)(index & ChannelMask);
    public static uint Rgb(int r, int g, int b) =>
        RgbBit | (uint)((r & ChannelMask) << RedShift | (g & ChannelMask) << GreenShift | (b & ChannelMask));
}

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

    public string LineText(int row) => new string(Lines[row].Select(c => c.Glyph == '\0' ? ' ' : c.Glyph).ToArray()).TrimEnd();

    public string Text => string.Join('\n', Enumerable.Range(0, Rows).Select(LineText)).TrimEnd('\n');
}
