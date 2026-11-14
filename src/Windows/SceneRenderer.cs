// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Edex.Core.Terminal;
using Edex.Core.Ui;

namespace Edex.Windows;

internal sealed class SceneRenderer(ThemePalette palette)
{
    private const double BorderThickness = 1;
    private const double BarTrackOpacity = 0.25;
    private const double LandPointRadius = 1.2;
    private const double KeyRadius = 3;
    private const double KeyMargin = 2;
    private const double KeyTextMargin = 4;
    private const double MinimumCellSize = 1;
    private const double CursorOpacity = 0.7;

    private readonly Dictionary<(char Character, double Size, Color Color), FormattedText> _glyphs = [];
    private readonly Typeface _typeface = ThemePalette.Typeface(palette.Theme.FontFamily);

    public void Draw(DrawingContext context, UiScene scene, double width, double height)
    {
        context.DrawRectangle(palette.Brush(UiBrush.Background), null, new Rect(0, 0, width, height));
        foreach (var element in scene.Elements)
        {
            switch (element.Kind)
            {
                case UiElementKind.Frame:
                    DrawFrame(context, element);
                    break;
                case UiElementKind.Text:
                    DrawText(context, element);
                    break;
                case UiElementKind.Bar:
                    DrawBar(context, element);
                    break;
                case UiElementKind.Polyline:
                    DrawPolyline(context, element);
                    break;
                case UiElementKind.Rule:
                    DrawRule(context, element);
                    break;
                case UiElementKind.Cells:
                    DrawCells(context, element);
                    break;
                case UiElementKind.Keys:
                    DrawKeys(context, element);
                    break;
                case UiElementKind.Globe:
                    DrawGlobe(context, element);
                    break;
            }
        }
    }

    private void DrawFrame(DrawingContext context, UiElement element)
    {
        var rect = new Rect(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height);
        context.DrawRectangle(palette.Brush(UiBrush.Surface), new Pen(palette.Brush(UiBrush.Muted), BorderThickness), rect);
        if (element.Text.Length == 0)
        {
            return;
        }

        var title = Formatted(element.Text, element.Size > 0 ? element.Size * Dashboard.TitleScale : 12, palette.Brush(UiBrush.Muted));
        context.DrawText(title, new Point(rect.Left + 6, rect.Top + 3));
    }

    private void DrawText(DrawingContext context, UiElement element)
    {
        if (element.Text.Length == 0)
        {
            return;
        }

        var text = Formatted(element.Text, element.Size, palette.Brush(element.Brush));
        double x = element.Align switch
        {
            UiTextAlign.Center => element.Bounds.X + Math.Max(0, (element.Bounds.Width - text.Width) / 2),
            UiTextAlign.Right => element.Bounds.X + Math.Max(0, element.Bounds.Width - text.Width),
            _ => element.Bounds.X,
        };
        double y = element.Align == UiTextAlign.Center
            ? element.Bounds.Y + Math.Max(0, (element.Bounds.Height - text.Height) / 2)
            : element.Bounds.Y;
        context.DrawText(text, new Point(x, y));
    }

    private void DrawBar(DrawingContext context, UiElement element)
    {
        var rect = new Rect(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height);
        var track = new SolidColorBrush(palette.Brush(UiBrush.Muted).Color) { Opacity = BarTrackOpacity };
        context.DrawRectangle(track, null, rect);
        double width = rect.Width * Math.Clamp(element.Value, 0, 1);
        if (width <= 0)
        {
            return;
        }

        context.DrawRectangle(palette.Brush(element.Brush), null, new Rect(rect.X, rect.Y, width, rect.Height));
    }

    private void DrawPolyline(DrawingContext context, UiElement element)
    {
        if (element.Payload is not double[] samples || samples.Length < 2 || element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
        {
            return;
        }

        double maximum = samples.Max();
        if (maximum <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < samples.Length; i++)
            {
                double x = element.Bounds.X + element.Bounds.Width * i / (samples.Length - 1);
                double y = element.Bounds.Y + element.Bounds.Height * (1 - Math.Clamp(samples[i] / maximum, 0, 1));
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(palette.Brush(element.Brush), BorderThickness), geometry);
    }

    private void DrawRule(DrawingContext context, UiElement element)
    {
        var rect = new Rect(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, Math.Max(MinimumCellSize, element.Bounds.Height));
        context.DrawRectangle(palette.Brush(element.Brush), null, rect);
    }

    private void DrawCells(DrawingContext context, UiElement element)
    {
        if (element.Payload is not TerminalSnapshot snapshot || snapshot.Columns <= 0 || snapshot.Rows <= 0)
        {
            return;
        }

        double cellWidth = element.Bounds.Width / snapshot.Columns;
        double cellHeight = element.Bounds.Height / snapshot.Rows;
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return;
        }

        double size = Math.Min(cellHeight, cellWidth * 2);
        for (int row = 0; row < snapshot.Rows; row++)
        {
            var line = snapshot.Lines[row];
            for (int column = 0; column < snapshot.Columns && column < line.Length; column++)
            {
                var cell = line[column];
                double x = element.Bounds.X + column * cellWidth;
                double y = element.Bounds.Y + row * cellHeight;
                var background = palette.TerminalBackground(cell.Background);
                if (background.A > 0)
                {
                    context.DrawRectangle(new SolidColorBrush(background), null, new Rect(x, y, cellWidth, cellHeight));
                }

                char glyph = cell.Glyph == '\0' ? ' ' : cell.Glyph;
                if (glyph == ' ')
                {
                    continue;
                }

                var text = Glyph(glyph, size, palette.TerminalForeground(cell.Foreground));
                context.DrawText(text, new Point(x, y + Math.Max(0, (cellHeight - text.Height) / 2)));
            }
        }

        if (snapshot.CursorVisible)
        {
            var cursorBrush = new SolidColorBrush(palette.Resolve(UiBrush.Cursor)) { Opacity = CursorOpacity };
            context.DrawRectangle(cursorBrush, null, new Rect(
                element.Bounds.X + snapshot.CursorX * cellWidth,
                element.Bounds.Y + snapshot.CursorY * cellHeight,
                cellWidth,
                cellHeight));
        }
    }

    private void DrawKeys(DrawingContext context, UiElement element)
    {
        if (element.Payload is not IReadOnlyList<UiKey> keys)
        {
            return;
        }

        foreach (var key in keys)
        {
            var rect = new Rect(
                key.Bounds.X + KeyMargin,
                key.Bounds.Y + KeyMargin,
                Math.Max(0, key.Bounds.Width - KeyMargin * 2),
                Math.Max(0, key.Bounds.Height - KeyMargin * 2));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            var fill = key.Active ? palette.Brush(UiBrush.Accent) : palette.Brush(UiBrush.Muted);
            context.DrawRoundedRectangle(new SolidColorBrush(fill.Color) { Opacity = key.Active ? 0.9 : 0.35 }, new Pen(palette.Brush(UiBrush.Accent), BorderThickness), rect, KeyRadius, KeyRadius);
            double size = Math.Max(8, rect.Height * 0.4);
            var label = Formatted(key.Label, size, palette.Brush(UiBrush.Foreground));
            double available = rect.Width - KeyTextMargin;
            if (label.Width > available && available > 0)
            {
                size = Math.Max(6, size * available / label.Width);
                label = Formatted(key.Label, size, palette.Brush(UiBrush.Foreground));
            }

            context.DrawText(label, new Point(
                rect.X + Math.Max(0, (rect.Width - label.Width) / 2),
                rect.Y + Math.Max(0, (rect.Height - label.Height) / 2)));
        }
    }

    private void DrawGlobe(DrawingContext context, UiElement element)
    {
        if (element.Payload is not UiGlobeFrame frame)
        {
            return;
        }

        double radius = Math.Min(element.Bounds.Width, element.Bounds.Height) / 2;
        if (radius <= 0)
        {
            return;
        }

        double centerX = element.Bounds.X + element.Bounds.Width / 2;
        double centerY = element.Bounds.Y + element.Bounds.Height / 2;
        context.DrawEllipse(null, new Pen(palette.Brush(UiBrush.Muted), BorderThickness), new Point(centerX, centerY), radius, radius);
        var land = new SolidColorBrush(palette.Resolve(UiBrush.Accent)) { Opacity = 0.8 };
        foreach (var point in frame.Land)
        {
            context.DrawEllipse(land, null, new Point(centerX + point.X * radius, centerY - point.Y * radius), LandPointRadius, LandPointRadius);
        }

        if (frame.Marker is { } marker)
        {
            context.DrawEllipse(new SolidColorBrush(palette.Resolve(UiBrush.Danger)), null, new Point(centerX + marker.X * radius, centerY - marker.Y * radius), LandPointRadius * 2, LandPointRadius * 2);
        }
    }

    private FormattedText Formatted(string text, double size, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            size,
            brush,
            1.25);
        return formatted;
    }

    private FormattedText Glyph(char character, double size, Color color)
    {
        var key = (character, size, color);
        if (_glyphs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            character.ToString(),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            size,
            new SolidColorBrush(color),
            1.25);
        _glyphs[key] = formatted;
        return formatted;
    }
}
