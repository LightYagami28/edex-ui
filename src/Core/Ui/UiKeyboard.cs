// SPDX-License-Identifier: GPL-3.0-only
using Edex.Core.Input;

namespace Edex.Core.Ui;

public readonly record struct UiPoint(double X, double Y);

public readonly record struct UiKey(UiRect Bounds, KeyDefinition Key, string Label, bool Active);

public sealed record UiGlobeFrame(IReadOnlyList<UiPoint> Land, UiPoint? Marker);

public sealed record UiHit(UiPanel Panel, UiElement Element, int Index, object? Value);

public static class UiKeyboard
{
    public const double KeyGap = 3;

    public static IReadOnlyList<UiKey> Arrange(KeyboardLayout layout, UiRect bounds, KeyboardState state)
    {
        var keys = new List<UiKey>();
        if (bounds.IsEmpty || layout.Rows.Count == 0)
        {
            return keys;
        }

        double rowHeight = (bounds.Height + KeyGap) / layout.Rows.Count - KeyGap;
        for (int row = 0; row < layout.Rows.Count; row++)
        {
            var definitions = layout.Rows[row];
            if (definitions.Count == 0)
            {
                continue;
            }

            double units = definitions.Sum(d => d.Width);
            double slot = (bounds.Width + KeyGap) / units;
            double x = bounds.X;
            double y = bounds.Y + (rowHeight + KeyGap) * row;
            foreach (var definition in definitions)
            {
                double width = Math.Max(0, slot * definition.Width - KeyGap);
                keys.Add(new UiKey(new UiRect(x, y, width, rowHeight), definition, state.Display(definition), IsActive(state, definition)));
                x += slot * definition.Width;
            }
        }

        return keys;
    }

    public static int HitTest(IReadOnlyList<UiKey> keys, double x, double y)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Bounds.Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsActive(KeyboardState state, KeyDefinition key) => key.Action switch
    {
        KeyAction.Shift => state.Shift,
        KeyAction.Control => state.Control,
        KeyAction.Alt => state.Alt,
        KeyAction.CapsLock => state.CapsLock,
        _ => false,
    };
}
