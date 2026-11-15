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
    private const double MarkerRadius = LandPointRadius * 2;
    private const double LandOpacity = 0.8;
    private const double KeyRadius = 3;
    private const double KeyMargin = 2;
    private const double KeyTextMargin = 4;
    private const double ActiveKeyOpacity = 0.9;
    private const double IdleKeyOpacity = 0.35;
    private const double KeyLabelFraction = 0.4;
    private const double MinimumKeyLabelSize = 8;
    private const double SmallestKeyLabelSize = 6;
    private const double MinimumCellSize = 1;
    private const double CursorOpacity = 0.7;
    private const double GlyphAspect = 2;
    private const double Half = 2;
    private const double DefaultTitleSize = 12;
    private const double TitleLeft = 6;
    private const double TitleTop = 3;
    private const double LineSpacing = 1.25;

    private static readonly Dictionary<UiElementKind, Action<SceneRenderer, DrawingContext, UiElement>> Painters = new()
    {
        [UiElementKind.Frame] = (r, c, e) => r.DrawFrame(c, e),
        [UiElementKind.Text] = (r, c, e) => r.DrawText(c, e),
        [UiElementKind.Bar] = (r, c, e) => r.DrawBar(c, e),
        [UiElementKind.Polyline] = (r, c, e) => r.DrawPolyline(c, e),
        [UiElementKind.Rule] = (r, c, e) => r.DrawRule(c, e),
        [UiElementKind.Cells] = (r, c, e) => r.DrawCells(c, e),
        [UiElementKind.Keys] = (r, c, e) => r.DrawKeys(c, e),
        [UiElementKind.Globe] = (r, c, e) => r.DrawGlobe(c, e),
    };

    private readonly Dictionary<(char Character, double Size, Color Color), FormattedText> _glyphs = [];
    private readonly Typeface _typeface = ThemePalette.Typeface(palette.Theme.FontFamily);

    internal void Draw(DrawingContext context, UiScene scene, double width, double height)
    {
        context.DrawRectangle(palette.Brush(UiBrush.Background), null, new Rect(0, 0, width, height));
        foreach (var element in scene.Elements)
        {
            if (Painters.TryGetValue(element.Kind, out var paint))
            {
                paint(this, context, element);
            }
        }
    }

    private static Rect ToRect(UiRect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private void DrawFrame(DrawingContext context, UiElement element)
    {
        var rect = ToRect(element.Bounds);
        context.DrawRectangle(palette.Brush(UiBrush.Surface), new Pen(palette.Brush(UiBrush.Muted), BorderThickness), rect);
        if (element.Text.Length == 0)
        {
            return;
        }

        double size = element.Size > 0 ? element.Size * Dashboard.TitleScale : DefaultTitleSize;
        var title = Formatted(element.Text, size, palette.Brush(UiBrush.Muted));
        context.DrawText(title, new Point(rect.Left + TitleLeft, rect.Top + TitleTop));
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
            UiTextAlign.Center => element.Bounds.X + Math.Max(0, (element.Bounds.Width - text.Width) / Half),
            UiTextAlign.Right => element.Bounds.X + Math.Max(0, element.Bounds.Width - text.Width),
            _ => element.Bounds.X,
        };
        double y = element.Align == UiTextAlign.Center
            ? element.Bounds.Y + Math.Max(0, (element.Bounds.Height - text.Height) / Half)
            : element.Bounds.Y;
        context.DrawText(text, new Point(x, y));
    }

    private void DrawBar(DrawingContext context, UiElement element)
    {
        var rect = ToRect(element.Bounds);
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
        if (element.Payload is not double[] { Length: > 1 } samples || element.Bounds.IsEmpty)
        {
            return;
        }

        double maximum = samples.Max();
        if (maximum <= 0)
        {
            return;
        }

        var geometry = Polyline(element.Bounds, samples, maximum);
        context.DrawGeometry(null, new Pen(palette.Brush(element.Brush), BorderThickness), geometry);
    }

    private static StreamGeometry Polyline(UiRect bounds, double[] samples, double maximum)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(SamplePoint(bounds, samples, maximum, 0), false, false);
            for (int i = 1; i < samples.Length; i++)
            {
                ctx.LineTo(SamplePoint(bounds, samples, maximum, i), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point SamplePoint(UiRect bounds, double[] samples, double maximum, int index) => new(
        bounds.X + bounds.Width * index / (samples.Length - 1),
        bounds.Y + bounds.Height * (1 - Math.Clamp(samples[index] / maximum, 0, 1)));

    private void DrawRule(DrawingContext context, UiElement element)
    {
        var rect = new Rect(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, Math.Max(MinimumCellSize, element.Bounds.Height));
        context.DrawRectangle(palette.Brush(element.Brush), null, rect);
    }

    private void DrawCells(DrawingContext context, UiElement element)
    {
        if (element.Payload is not TerminalSnapshot { Columns: > 0, Rows: > 0 } snapshot || element.Bounds.IsEmpty)
        {
            return;
        }

        var cellSize = new Size(element.Bounds.Width / snapshot.Columns, element.Bounds.Height / snapshot.Rows);
        for (int row = 0; row < snapshot.Rows; row++)
        {
            var line = snapshot.Lines[row];
            int columns = Math.Min(snapshot.Columns, line.Length);
            for (int column = 0; column < columns; column++)
            {
                var origin = new Point(element.Bounds.X + column * cellSize.Width, element.Bounds.Y + row * cellSize.Height);
                DrawCell(context, line[column], origin, cellSize);
            }
        }

        if (snapshot.CursorVisible)
        {
            DrawCursor(context, element.Bounds, snapshot, cellSize);
        }
    }

    private void DrawCell(DrawingContext context, TerminalCell cell, Point origin, Size cellSize)
    {
        var background = palette.TerminalBackground(cell.Background);
        if (background.A > 0)
        {
            context.DrawRectangle(new SolidColorBrush(background), null, new Rect(origin, cellSize));
        }

        if (cell.Glyph is '\0' or ' ')
        {
            return;
        }

        double size = Math.Min(cellSize.Height, cellSize.Width * GlyphAspect);
        var text = Glyph(cell.Glyph, size, palette.TerminalForeground(cell.Foreground));
        context.DrawText(text, new Point(origin.X, origin.Y + Math.Max(0, (cellSize.Height - text.Height) / Half)));
    }

    private void DrawCursor(DrawingContext context, UiRect bounds, TerminalSnapshot snapshot, Size cellSize)
    {
        var cursorBrush = new SolidColorBrush(palette.Resolve(UiBrush.Cursor)) { Opacity = CursorOpacity };
        var origin = new Point(bounds.X + snapshot.CursorX * cellSize.Width, bounds.Y + snapshot.CursorY * cellSize.Height);
        context.DrawRectangle(cursorBrush, null, new Rect(origin, cellSize));
    }

    private void DrawKeys(DrawingContext context, UiElement element)
    {
        if (element.Payload is not IReadOnlyList<UiKey> keys)
        {
            return;
        }

        foreach (var key in keys)
        {
            DrawKey(context, key);
        }
    }

    private void DrawKey(DrawingContext context, UiKey key)
    {
        var rect = new Rect(
            key.Bounds.X + KeyMargin,
            key.Bounds.Y + KeyMargin,
            Math.Max(0, key.Bounds.Width - KeyMargin * Half),
            Math.Max(0, key.Bounds.Height - KeyMargin * Half));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var fill = palette.Brush(key.Active ? UiBrush.Accent : UiBrush.Muted);
        var background = new SolidColorBrush(fill.Color) { Opacity = key.Active ? ActiveKeyOpacity : IdleKeyOpacity };
        context.DrawRoundedRectangle(background, new Pen(palette.Brush(UiBrush.Accent), BorderThickness), rect, KeyRadius, KeyRadius);
        var label = KeyLabel(key.Label, rect);
        context.DrawText(label, new Point(
            rect.X + Math.Max(0, (rect.Width - label.Width) / Half),
            rect.Y + Math.Max(0, (rect.Height - label.Height) / Half)));
    }

    private FormattedText KeyLabel(string text, Rect rect)
    {
        double size = Math.Max(MinimumKeyLabelSize, rect.Height * KeyLabelFraction);
        var label = Formatted(text, size, palette.Brush(UiBrush.Foreground));
        double available = rect.Width - KeyTextMargin;
        if (label.Width <= available || available <= 0)
        {
            return label;
        }

        size = Math.Max(SmallestKeyLabelSize, size * available / label.Width);
        return Formatted(text, size, palette.Brush(UiBrush.Foreground));
    }

    private void DrawGlobe(DrawingContext context, UiElement element)
    {
        if (element.Payload is not UiGlobeFrame frame)
        {
            return;
        }

        double radius = Math.Min(element.Bounds.Width, element.Bounds.Height) / Half;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(element.Bounds.X + element.Bounds.Width / Half, element.Bounds.Y + element.Bounds.Height / Half);
        context.DrawEllipse(null, new Pen(palette.Brush(UiBrush.Muted), BorderThickness), center, radius, radius);
        var land = new SolidColorBrush(palette.Resolve(UiBrush.Accent)) { Opacity = LandOpacity };
        foreach (var point in frame.Land)
        {
            context.DrawEllipse(land, null, OnGlobe(center, radius, point), LandPointRadius, LandPointRadius);
        }

        if (frame.Marker is { } marker)
        {
            var markerBrush = new SolidColorBrush(palette.Resolve(UiBrush.Danger));
            context.DrawEllipse(markerBrush, null, OnGlobe(center, radius, marker), MarkerRadius, MarkerRadius);
        }
    }

    private static Point OnGlobe(Point center, double radius, UiPoint point) =>
        new(center.X + point.X * radius, center.Y - point.Y * radius);

    private FormattedText Formatted(string text, double size, Brush brush) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        _typeface,
        size,
        brush,
        LineSpacing);

    private FormattedText Glyph(char character, double size, Color color)
    {
        var key = (character, size, color);
        if (!_glyphs.TryGetValue(key, out var formatted))
        {
            formatted = Formatted(character.ToString(CultureInfo.InvariantCulture), size, new SolidColorBrush(color));
            _glyphs[key] = formatted;
        }

        return formatted;
    }
}
