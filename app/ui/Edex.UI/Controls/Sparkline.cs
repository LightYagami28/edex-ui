// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Edex.UI.Controls;

/// <summary>Small area chart for a rolling series of values (one or two series).</summary>
public sealed class Sparkline : Control
{
    private readonly Queue<double> _primary = new();
    private readonly Queue<double> _secondary = new();
    private readonly int _capacity;

    public Sparkline(int capacity = 60)
    {
        _capacity = capacity;
        Height = 54;
    }

    public double? FixedMaximum { get; set; }
    public IBrush Stroke { get; set; } = Brushes.Cyan;
    public IBrush SecondaryStroke { get; set; } = Brushes.Orange;

    public void Push(double value, double? secondary = null)
    {
        _primary.Enqueue(value);
        if (_primary.Count > _capacity) _primary.Dequeue();
        if (secondary is double s)
        {
            _secondary.Enqueue(s);
            if (_secondary.Count > _capacity) _secondary.Dequeue();
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var grid = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(size.Height * i / 4) + 0.5;
            context.DrawLine(grid, new Point(0, y), new Point(size.Width, y));
        }
        double max = FixedMaximum ?? Math.Max(1, Math.Max(_primary.DefaultIfEmpty(0).Max(), _secondary.DefaultIfEmpty(0).Max()) * 1.15);
        DrawSeries(context, _secondary, SecondaryStroke, max, size, fill: false);
        DrawSeries(context, _primary, Stroke, max, size, fill: true);
    }

    private void DrawSeries(DrawingContext context, Queue<double> values, IBrush stroke, double max, Size size, bool fill)
    {
        if (values.Count < 2) return;
        double step = size.Width / (_capacity - 1);
        double x0 = size.Width - (values.Count - 1) * step;
        var points = values.Select((v, i) => new Point(x0 + i * step, size.Height - Math.Clamp(v / max, 0, 1) * (size.Height - 2) - 1)).ToList();

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false);
            foreach (var p in points.Skip(1)) ctx.LineTo(p);
            ctx.EndFigure(false);
        }

        if (fill && stroke is ISolidColorBrush solid)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, size.Height), true);
                foreach (var p in points) ctx.LineTo(p);
                ctx.LineTo(new Point(points[^1].X, size.Height));
                ctx.EndFigure(true);
            }
            var c = solid.Color;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x66, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
                },
            };
            context.DrawGeometry(gradient, null, area);
        }
        context.DrawGeometry(null, new Pen(stroke, 1.5), line);
    }
}
