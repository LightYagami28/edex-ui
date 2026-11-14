// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Edex.Core.Ui;

namespace Edex.Windows;

internal sealed class ThemePalette
{
    private const uint PaletteIndexMask = 0xFF;
    private const uint ChannelMask = 0xFF;

    private readonly Dictionary<UiBrush, SolidColorBrush> _brushes = [];

    public ThemePalette(Theme theme) => Theme = theme;

    public Theme Theme { get; }

    public SolidColorBrush Brush(UiBrush brush)
    {
        if (_brushes.TryGetValue(brush, out var cached))
        {
            return cached;
        }

        var created = new SolidColorBrush(Resolve(brush));
        created.Freeze();
        _brushes[brush] = created;
        return created;
    }

    public Color Resolve(UiBrush brush) => brush switch
    {
        UiBrush.Background => Parse(Theme.Background),
        UiBrush.Surface => Parse(Theme.Surface),
        UiBrush.Foreground => Parse(Theme.Foreground),
        UiBrush.Muted => Parse(Theme.Muted),
        UiBrush.Accent => Parse(Theme.Accent),
        UiBrush.Danger => Parse(Theme.Danger),
        UiBrush.TerminalForeground => Parse(Theme.EffectiveTerminalForeground),
        UiBrush.TerminalBackground => Parse(Theme.EffectiveTerminalBackground),
        UiBrush.Cursor => Parse(Theme.EffectiveCursor),
        _ => Colors.Transparent,
    };

    public Color TerminalForeground(uint value)
    {
        if (value == 0)
        {
            return Parse(Theme.EffectiveTerminalForeground);
        }

        if ((value & TerminalCell.PaletteFlag) != 0)
        {
            return Parse(Ansi(value & PaletteIndexMask));
        }

        if ((value & TerminalCell.RgbFlag) != 0)
        {
            return Color.FromRgb(Red(value), Green(value), Blue(value));
        }

        return Parse(Theme.EffectiveTerminalForeground);
    }

    public Color TerminalBackground(uint value)
    {
        if (value == 0)
        {
            return Colors.Transparent;
        }

        if ((value & TerminalCell.PaletteFlag) != 0)
        {
            return Parse(Ansi(value & PaletteIndexMask));
        }

        if ((value & TerminalCell.RgbFlag) != 0)
        {
            return Color.FromRgb(Red(value), Green(value), Blue(value));
        }

        return Colors.Transparent;
    }

    private string Ansi(uint index)
    {
        var palette = Theme.Ansi;
        return index < palette.Count ? palette[(int)index] : Theme.Foreground;
    }

    private static byte Red(uint value) => (byte)(value >> 16 & ChannelMask);

    private static byte Green(uint value) => (byte)(value >> 8 & ChannelMask);

    private static byte Blue(uint value) => (byte)(value & ChannelMask);

    private static Color Parse(string hex)
    {
        if (!ColorUtil.IsValidHex(hex))
        {
            return Colors.White;
        }

        var (r, g, b) = ColorUtil.Parse(hex);
        return Color.FromRgb(r, g, b);
    }

    public static Typeface Typeface(string family) => new(
        new FontFamily(family),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    public static CultureInfo Culture => CultureInfo.InvariantCulture;
}
