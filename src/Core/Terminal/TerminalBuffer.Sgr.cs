// SPDX-License-Identifier: GPL-3.0-only

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
{
    private const int SgrExtendedForeground = 38;
    private const int SgrExtendedBackground = 48;
    private const int SgrForegroundFirst = 30;
    private const int SgrForegroundLast = 37;
    private const int SgrBackgroundFirst = 40;
    private const int SgrBackgroundLast = 47;
    private const int SgrBrightForegroundFirst = 90;
    private const int SgrBrightForegroundLast = 97;
    private const int SgrBrightBackgroundFirst = 100;
    private const int SgrBrightBackgroundLast = 107;
    private const int BrightPaletteOffset = 8;
    private const int SgrColorIndexed = 5;
    private const int SgrColorRgb = 2;
    private const int IndexedColorArgs = 1;
    private const int RgbColorArgs = 3;
    private const int RedIndex = 0;
    private const int GreenIndex = 1;
    private const int BlueIndex = 2;

    private static readonly Dictionary<int, Action<TerminalBuffer>> SgrSimple = new()
    {
        [0] = b => b.ResetAttributes(),
        [1] = b => b._attrs |= CellAttributes.Bold,
        [2] = b => b._attrs |= CellAttributes.Dim,
        [3] = b => b._attrs |= CellAttributes.Italic,
        [4] = b => b._attrs |= CellAttributes.Underline,
        [7] = b => b._attrs |= CellAttributes.Inverse,
        [8] = b => b._attrs |= CellAttributes.Hidden,
        [9] = b => b._attrs |= CellAttributes.Strikethrough,
        [21] = b => b._attrs &= ~(CellAttributes.Bold | CellAttributes.Dim),
        [22] = b => b._attrs &= ~(CellAttributes.Bold | CellAttributes.Dim),
        [23] = b => b._attrs &= ~CellAttributes.Italic,
        [24] = b => b._attrs &= ~CellAttributes.Underline,
        [27] = b => b._attrs &= ~CellAttributes.Inverse,
        [28] = b => b._attrs &= ~CellAttributes.Hidden,
        [29] = b => b._attrs &= ~CellAttributes.Strikethrough,
        [39] = b => b._fg = 0,
        [49] = b => b._bg = 0,
    };

    private static readonly (int First, int Last, int Offset, bool Foreground)[] SgrPaletteRanges =
    [
        (SgrForegroundFirst, SgrForegroundLast, 0, true),
        (SgrBackgroundFirst, SgrBackgroundLast, 0, false),
        (SgrBrightForegroundFirst, SgrBrightForegroundLast, BrightPaletteOffset, true),
        (SgrBrightBackgroundFirst, SgrBrightBackgroundLast, BrightPaletteOffset, false),
    ];

    private void ResetAttributes()
    {
        _fg = 0;
        _bg = 0;
        _attrs = CellAttributes.None;
    }

    private void Sgr(int[] p)
    {
        int[] codes = p.Length == 0 ? [0] : p;
        int i = 0;
        while (i < codes.Length)
        {
            int code = codes[i++];
            if (code is SgrExtendedForeground or SgrExtendedBackground)
            {
                i = ExtendedColor(codes, i, code == SgrExtendedForeground);
            }
            else
            {
                ApplySimpleOrPaletteSgr(code);
            }
        }
    }

    private void ApplySimpleOrPaletteSgr(int code)
    {
        foreach (var range in SgrPaletteRanges)
        {
            if (code >= range.First && code <= range.Last)
            {
                SetColor(TerminalCell.Palette(code - range.First + range.Offset), range.Foreground);
                return;
            }
        }

        if (SgrSimple.TryGetValue(code, out var action))
        {
            action(this);
        }
    }

    private void SetColor(uint color, bool foreground)
    {
        if (foreground)
        {
            _fg = color;
        }
        else
        {
            _bg = color;
        }
    }

    private int ExtendedColor(int[] p, int next, bool foreground)
    {
        if (!TryReadExtendedColor(p, next, out uint color, out int end))
        {
            return p.Length;
        }

        SetColor(color, foreground);
        return end;
    }

    private static bool TryReadExtendedColor(int[] p, int next, out uint color, out int end)
    {
        if (next + IndexedColorArgs < p.Length && p[next] == SgrColorIndexed)
        {
            color = TerminalCell.Palette(p[next + 1]);
            end = next + IndexedColorArgs + 1;
            return true;
        }

        if (next + RgbColorArgs < p.Length && p[next] == SgrColorRgb)
        {
            var rgb = p.AsSpan(next + 1, RgbColorArgs);
            color = TerminalCell.Rgb(rgb[RedIndex], rgb[GreenIndex], rgb[BlueIndex]);
            end = next + RgbColorArgs + 1;
            return true;
        }

        color = 0;
        end = p.Length;
        return false;
    }
}
