// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Windows.Media;
using Edex.Core.Terminal;
using Edex.Core.Themes;
using Edex.Core.Ui;

namespace Edex.Windows;

internal sealed class ThemePalette
{
    private const int RedShift = 16;
    private const int GreenShift = 8;
    private const uint PaletteIndexMask = 0xFF;
    private const uint ChannelMask = 0xFF;

    private static readonly Dictionary<UiBrush, Func<Theme, string>> BrushColors = new()
    {
        [UiBrush.Background] = theme => theme.Background,
        [UiBrush.Surface] = theme => theme.Surface,
        [UiBrush.Foreground] = theme => theme.Foreground,
        [UiBrush.Muted] = theme => theme.Muted,
        [UiBrush.Accent] = theme => theme.Accent,
        [UiBrush.Danger] = theme => theme.Danger,
        [UiBrush.TerminalForeground] = theme => theme.EffectiveTerminalForeground,
        [UiBrush.TerminalBackground] = theme => theme.EffectiveTerminalBackground,
        [UiBrush.Cursor] = theme => theme.EffectiveCursor,
    };

    private readonly Dictionary<UiBrush, SolidColorBrush> _brushes = [];

    internal ThemePalette(Theme theme) => Theme = theme;

    internal Theme Theme { get; }

    internal SolidColorBrush Brush(UiBrush kind)
    {
        if (_brushes.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var created = new SolidColorBrush(Resolve(kind));
        created.Freeze();
        _brushes[kind] = created;
        return created;
    }

    internal Color Resolve(UiBrush kind) =>
        BrushColors.TryGetValue(kind, out var color) ? Parse(color(Theme)) : Colors.Transparent;

    internal Color TerminalForeground(uint value) => TerminalColor(value) ?? Parse(Theme.EffectiveTerminalForeground);

    internal Color TerminalBackground(uint value) => TerminalColor(value) ?? Colors.Transparent;

    private Color? TerminalColor(uint value)
    {
        if ((value & TerminalCell.PaletteFlag) != 0)
        {
            return Parse(Ansi(value & PaletteIndexMask));
        }

        return (value & TerminalCell.RgbFlag) != 0 ? Color.FromRgb(Red(value), Green(value), Blue(value)) : null;
    }

    private string Ansi(uint index)
    {
        var palette = Theme.Ansi;
        return index < palette.Count ? palette[(int)index] : Theme.Foreground;
    }

    private static byte Red(uint value) => (byte)(value >> RedShift & ChannelMask);

    private static byte Green(uint value) => (byte)(value >> GreenShift & ChannelMask);

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

    internal static Typeface Typeface(string family) => new(
        new FontFamily(family),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    internal static CultureInfo Culture => CultureInfo.InvariantCulture;
}
