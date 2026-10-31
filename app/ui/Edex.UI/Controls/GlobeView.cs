// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Edex.Core.Geo;

namespace Edex.UI.Controls;

/// <summary>Dotted globe (orthographic projection of an offline land mask) with the user's location.</summary>
public sealed class GlobeView : Control
{
    private static readonly Lazy<IReadOnlyList<(double Latitude, double Longitude)>> s_land = new(() => LandMask.Default.LandPoints(2.4));

    private readonly Palette _palette;
    private readonly DispatcherTimer _timer;
    private double _longitude;
    private double _phase;
    private GeoLocation? _location;

    public GlobeView(Palette palette)
    {
        _palette = palette;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) =>
        {
            if (_location is null) _longitude = (_longitude + 0.6) % 360; // spin until the location is known, then stay centred on it
            _phase = (_phase + 0.05) % 1;
            InvalidateVisual();
        });
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public GeoLocation? Location
    {
        get => _location;
        set
        {
            _location = value;
            if (value is not null) _longitude = value.Longitude;
            InvalidateVisual();
        }
    }

    /// <summary>Number of land dots drawn on the last frame (used by the smoke test).</summary>
    public int LastDotCount { get; private set; }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height) - 8;
        if (size <= 10) return;
        double radius = size / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var accent = _palette.Accent;
        var view = new Orthographic(_location is null ? 20 : Math.Clamp(_location.Latitude, -60, 60) * 0.6, _longitude);

        context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
            new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B)), 1), center, radius, radius);

        // Graticule every 30 degrees.
        var gridPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), 0.7);
        for (int lon = -180; lon < 180; lon += 30) DrawLine(context, gridPen, view, center, radius, lat => (lat, lon), -90, 90);
        for (int lat = -60; lat <= 60; lat += 30) DrawLine(context, gridPen, view, center, radius, lon => (lat, lon), -180, 180);

        var dot = new ImmutableSolidColorBrush(Color.FromArgb(0xD0, accent.R, accent.G, accent.B));
        double dotSize = Math.Max(1.2, radius / 70);
        int count = 0;
        foreach (var (lat, lon) in s_land.Value)
        {
            var (x, y, visible) = view.Project(lat, lon);
            if (!visible) continue;
            context.FillRectangle(dot, new Rect(center.X + x * radius - dotSize / 2, center.Y + y * radius - dotSize / 2, dotSize, dotSize));
            count++;
        }
        LastDotCount = count;

        if (_location is { } here)
        {
            var (x, y, visible) = view.Project(here.Latitude, here.Longitude);
            if (visible)
            {
                var p = new Point(center.X + x * radius, center.Y + y * radius);
                var danger = _palette.Danger;
                context.DrawEllipse(new ImmutableSolidColorBrush(danger), null, p, 3, 3);
                double pulse = 3 + _phase * 12;
                context.DrawEllipse(null, new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * (1 - _phase)), danger.R, danger.G, danger.B)), 1.2), p, pulse, pulse);
            }
        }
    }

    private static void DrawLine(DrawingContext context, IPen pen, Orthographic view, Point center, double radius,
        Func<double, (double Lat, double Lon)> at, double from, double to)
    {
        Point? previous = null;
        for (double t = from; t <= to; t += 4)
        {
            var (lat, lon) = at(t);
            var (x, y, visible) = view.Project(lat, lon);
            var point = new Point(center.X + x * radius, center.Y + y * radius);
            if (visible && previous is { } prev) context.DrawLine(pen, prev, point);
            previous = visible ? point : null;
        }
    }
}
