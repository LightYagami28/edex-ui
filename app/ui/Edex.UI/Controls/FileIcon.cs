// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Edex.Core.Files;

namespace Edex.UI.Controls;

/// <summary>Vector file icon drawn from the file category; used where the OS has no icon to offer.</summary>
public sealed class FileIcon : Control
{
    private readonly FileCategory _category;
    private readonly string _label;
    private readonly Color _accent;

    public FileIcon(FileCategory category, string label, Color accent, double size = 40)
    {
        _category = category;
        _label = label;
        _accent = accent;
        Width = Height = size;
    }

    public static Color Tint(FileCategory category, Color accent) => category switch
    {
        FileCategory.Folder or FileCategory.Link => accent,
        FileCategory.Image => Color.Parse("#4FC3F7"),
        FileCategory.Audio => Color.Parse("#BA68C8"),
        FileCategory.Video => Color.Parse("#F06292"),
        FileCategory.Archive => Color.Parse("#FFB74D"),
        FileCategory.Pdf => Color.Parse("#EF5350"),
        FileCategory.Document => Color.Parse("#64B5F6"),
        FileCategory.Spreadsheet => Color.Parse("#81C784"),
        FileCategory.Presentation => Color.Parse("#FF8A65"),
        FileCategory.Code => Color.Parse("#AED581"),
        FileCategory.Data => Color.Parse("#FFD54F"),
        FileCategory.Executable => Color.Parse("#E57373"),
        FileCategory.Font => Color.Parse("#90A4AE"),
        FileCategory.Disk => Color.Parse("#A1887F"),
        _ => Color.FromArgb(0xFF, (byte)(accent.R / 2 + 64), (byte)(accent.G / 2 + 64), (byte)(accent.B / 2 + 64)),
    };

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var tint = Tint(_category, _accent);
        var stroke = new Pen(new ImmutableSolidColorBrush(tint), 1.4);
        var fill = new ImmutableSolidColorBrush(Color.FromArgb(0x30, tint.R, tint.G, tint.B));

        if (_category is FileCategory.Folder)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(w * 0.08, h * 0.22), true);
                c.LineTo(new Point(w * 0.40, h * 0.22));
                c.LineTo(new Point(w * 0.48, h * 0.32));
                c.LineTo(new Point(w * 0.92, h * 0.32));
                c.LineTo(new Point(w * 0.92, h * 0.82));
                c.LineTo(new Point(w * 0.08, h * 0.82));
                c.EndFigure(true);
            }
            context.DrawGeometry(fill, stroke, g);
            context.DrawLine(stroke, new Point(w * 0.08, h * 0.40), new Point(w * 0.92, h * 0.40));
            return;
        }

        // Document with a folded corner.
        double left = w * 0.2, right = w * 0.8, top = h * 0.08, bottom = h * 0.92, fold = w * 0.18;
        var doc = new StreamGeometry();
        using (var c = doc.Open())
        {
            c.BeginFigure(new Point(left, top), true);
            c.LineTo(new Point(right - fold, top));
            c.LineTo(new Point(right, top + fold));
            c.LineTo(new Point(right, bottom));
            c.LineTo(new Point(left, bottom));
            c.EndFigure(true);
        }
        context.DrawGeometry(fill, stroke, doc);
        context.DrawLine(stroke, new Point(right - fold, top), new Point(right - fold, top + fold));
        context.DrawLine(stroke, new Point(right - fold, top + fold), new Point(right, top + fold));

        var center = new Point(w / 2, h * 0.5);
        switch (_category)
        {
            case FileCategory.Image:
                var mountain = new StreamGeometry();
                using (var c = mountain.Open())
                {
                    c.BeginFigure(new Point(left + 3, bottom - 6), true);
                    c.LineTo(new Point(w * 0.42, h * 0.55));
                    c.LineTo(new Point(w * 0.55, h * 0.68));
                    c.LineTo(new Point(w * 0.64, h * 0.6));
                    c.LineTo(new Point(right - 3, bottom - 6));
                    c.EndFigure(true);
                }
                context.DrawGeometry(new ImmutableSolidColorBrush(tint), null, mountain);
                context.DrawEllipse(new ImmutableSolidColorBrush(tint), null, new Point(w * 0.40, h * 0.38), w * 0.06, w * 0.06);
                return;
            case FileCategory.Audio:
                context.DrawEllipse(new ImmutableSolidColorBrush(tint), null, new Point(w * 0.42, h * 0.66), w * 0.07, h * 0.055);
                context.DrawLine(stroke, new Point(w * 0.49, h * 0.66), new Point(w * 0.49, h * 0.34));
                context.DrawLine(stroke, new Point(w * 0.49, h * 0.34), new Point(w * 0.62, h * 0.40));
                return;
            case FileCategory.Video:
                var play = new StreamGeometry();
                using (var c = play.Open())
                {
                    c.BeginFigure(new Point(w * 0.42, h * 0.38), true);
                    c.LineTo(new Point(w * 0.64, h * 0.52));
                    c.LineTo(new Point(w * 0.42, h * 0.66));
                    c.EndFigure(true);
                }
                context.DrawGeometry(new ImmutableSolidColorBrush(tint), null, play);
                return;
            case FileCategory.Archive:
                for (double y = top + 3; y < h * 0.55; y += 4)
                    context.DrawLine(stroke, new Point(center.X - 2, y), new Point(center.X + 2, y));
                context.DrawRectangle(new ImmutableSolidColorBrush(tint), null, new Rect(center.X - 3, h * 0.55, 6, 6));
                return;
            case FileCategory.Text or FileCategory.Code or FileCategory.Data or FileCategory.Document:
                for (int i = 0; i < 4; i++)
                {
                    double y = h * 0.38 + i * h * 0.1;
                    double len = _category == FileCategory.Code ? (i % 2 == 0 ? 0.35 : 0.25) : 0.4;
                    double x0 = _category == FileCategory.Code && i % 2 == 1 ? left + 8 : left + 4;
                    context.DrawLine(stroke, new Point(x0, y), new Point(x0 + w * len, y));
                }
                break;
        }

        if (_label.Length > 0)
        {
            var text = new FormattedText(_label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, Math.Max(7, w * 0.2),
                new ImmutableSolidColorBrush(tint)) { TextAlignment = TextAlignment.Center };
            var badge = new Rect(w * 0.1, bottom - h * 0.3, w * 0.8, text.Height);
            context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0)), badge, 2);
            context.DrawText(text, new Point(w / 2 - text.Width / 2, badge.Y));
        }
    }
}
