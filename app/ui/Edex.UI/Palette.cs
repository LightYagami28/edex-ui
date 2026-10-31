// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Edex.Core.Terminal;
using Edex.Core.Themes;

namespace Edex.UI;

/// <summary>Theme colors converted to Avalonia brushes, plus terminal color resolution.</summary>
public sealed class Palette
{
    private readonly Color[] _ansi = new Color[256];
    private readonly Dictionary<uint, IBrush> _brushCache = [];

    public Theme Theme { get; }
    public Color Accent { get; }
    public Color Background { get; }
    public Color Surface { get; }
    public Color Foreground { get; }
    public Color Muted { get; }
    public Color Danger { get; }
    public Color TerminalForeground { get; }
    public Color TerminalBackground { get; }
    public Color Cursor { get; }

    public IBrush AccentBrush { get; }
    public IBrush BackgroundBrush { get; }
    public IBrush SurfaceBrush { get; }
    public IBrush ForegroundBrush { get; }
    public IBrush MutedBrush { get; }
    public IBrush BorderBrush { get; }
    public IBrush GlowBrush { get; }

    public Palette(Theme theme)
    {
        Theme = theme;
        Accent = ToColor(theme.Accent);
        Background = ToColor(theme.Background);
        Surface = ToColor(theme.Surface);
        Foreground = ToColor(theme.Foreground);
        Muted = ToColor(theme.Muted);
        Danger = ToColor(theme.Danger);
        TerminalForeground = ToColor(theme.EffectiveTerminalForeground);
        TerminalBackground = ToColor(theme.EffectiveTerminalBackground);
        Cursor = ToColor(theme.EffectiveCursor);

        for (int i = 0; i < 16; i++) _ansi[i] = ToColor(theme.Ansi[i]);
        for (int i = 16; i < 256; i++)
        {
            var (r, g, b) = ColorUtil.Xterm256(i);
            _ansi[i] = Color.FromRgb(r, g, b);
        }

        AccentBrush = new ImmutableSolidColorBrush(Accent);
        BackgroundBrush = new ImmutableSolidColorBrush(Background);
        SurfaceBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xD9, Surface.R, Surface.G, Surface.B));
        ForegroundBrush = new ImmutableSolidColorBrush(Foreground);
        MutedBrush = new ImmutableSolidColorBrush(Muted);
        BorderBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x66, Accent.R, Accent.G, Accent.B));
        GlowBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x22, Accent.R, Accent.G, Accent.B));
    }

    public static Color ToColor(string hex)
    {
        var (r, g, b) = ColorUtil.Parse(hex);
        return Color.FromRgb(r, g, b);
    }

    public Color Resolve(uint encoded, bool foreground, bool bold)
    {
        if (encoded == 0) return foreground ? TerminalForeground : TerminalBackground;
        if ((encoded & TerminalCell.RgbFlag) != 0)
            return Color.FromRgb((byte)(encoded >> 16), (byte)(encoded >> 8), (byte)encoded);
        int index = (int)(encoded & 0xFF);
        if (bold && foreground && index < 8) index += 8;
        return _ansi[index];
    }

    public IBrush Brush(Color color)
    {
        uint key = color.ToUInt32();
        if (!_brushCache.TryGetValue(key, out var brush))
        {
            brush = new ImmutableSolidColorBrush(color);
            _brushCache[key] = brush;
        }
        return brush;
    }
}
