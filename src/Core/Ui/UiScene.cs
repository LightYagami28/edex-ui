// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Ui;

public readonly record struct UiTextStyle(UiBrush Brush, double Size, UiTextAlign Align, UiPanel Panel);

public sealed class UiScene
{
    private readonly List<UiElement> _elements = [];

    public IReadOnlyList<UiElement> Elements => _elements;

    public int Count => _elements.Count;

    public void Clear() => _elements.Clear();

    public void Add(UiElement element) => _elements.Add(element);

    public void Frame(UiRect bounds, UiPanel panel, string title)
    {
        Add(new UiElement { Kind = UiElementKind.Frame, Bounds = bounds, Panel = panel, Text = title });
    }

    public void Text(UiRect bounds, string content, UiTextStyle style)
    {
        Add(new UiElement { Kind = UiElementKind.Text, Bounds = bounds, Brush = style.Brush, Size = style.Size, Align = style.Align, Text = content, Panel = style.Panel });
    }

    public void Bar(UiRect bounds, double fraction, UiBrush brush, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Bar, Bounds = bounds, Brush = brush, Value = ClampFraction(fraction), Panel = panel });
    }

    public void Polyline(UiRect bounds, IReadOnlyList<double> samples, UiBrush brush, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Polyline, Bounds = bounds, Brush = brush, Payload = samples, Panel = panel });
    }

    public void Rule(UiRect bounds, UiBrush brush, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Rule, Bounds = bounds, Brush = brush, Panel = panel });
    }

    public void Cells(UiRect bounds, object snapshot, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Cells, Bounds = bounds, Payload = snapshot, Panel = panel });
    }

    public void Keys(UiRect bounds, object arrangedKeys, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Keys, Bounds = bounds, Payload = arrangedKeys, Panel = panel });
    }

    public void Globe(UiRect bounds, object projection, UiPanel panel)
    {
        Add(new UiElement { Kind = UiElementKind.Globe, Bounds = bounds, Payload = projection, Panel = panel });
    }

    public UiElement? HitTest(double x, double y)
    {
        for (int i = _elements.Count - 1; i >= 0; i--)
        {
            UiElement element = _elements[i];
            if (IsHitTestable(element.Kind) && element.Bounds.Contains(x, y))
            {
                return element;
            }
        }

        return null;
    }

    private static bool IsHitTestable(UiElementKind kind) => kind is not UiElementKind.Rule;

    private static double ClampFraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
