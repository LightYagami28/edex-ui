// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Edex.UI.Views;

/// <summary>Small factory helpers so the layout can be written in plain C# without XAML.</summary>
internal static class Ui
{
    public static TextBlock Text(string text, IBrush brush, double size = 13, FontWeight weight = FontWeight.Normal, string? font = null) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = size,
        FontWeight = weight,
        FontFamily = font is null ? FontFamily.Default : new FontFamily(font),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Card(Palette palette, string title, Control content, string? badge = null)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var accentBar = new Border { Width = 3, Height = 12, Background = palette.AccentBrush, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(accentBar, Dock.Left);
        header.Children.Add(accentBar);
        if (badge is not null)
        {
            var badgeText = Text(badge, palette.MutedBrush, 11);
            DockPanel.SetDock(badgeText, Dock.Right);
            header.Children.Add(badgeText);
        }
        var titleBlock = Text(title.ToUpperInvariant(), palette.AccentBrush, 11, FontWeight.SemiBold);
        titleBlock.LetterSpacing = 1.5;
        header.Children.Add(titleBlock);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(content);
        return new Border
        {
            Background = palette.SurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(0x30, palette.Accent.R, palette.Accent.G, palette.Accent.B) }),
            Child = stack,
        };
    }

    public static Grid KeyValue(Palette palette, string key, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 1) };
        grid.Children.Add(Text(key, palette.MutedBrush, 12));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    public static ProgressBar Bar(Palette palette) => new()
    {
        Minimum = 0,
        Maximum = 100,
        Height = 6,
        MinHeight = 6,
        CornerRadius = new CornerRadius(3),
        Foreground = palette.AccentBrush,
        Background = palette.GlowBrush,
        Margin = new Thickness(0, 4, 0, 2),
    };
}
