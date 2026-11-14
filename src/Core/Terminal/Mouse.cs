// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Terminal;

public enum MouseTrackingMode
{
    None,
    X10,
    Normal,
    ButtonEvent,
    AnyEvent,
}

public enum MouseButton { Left = 0, Middle = 1, Right = 2, None = 3, WheelUp = 64, WheelDown = 65 }

public enum MouseEventKind { Press, Release, Motion }

[Flags]
public enum MouseModifiers { None = 0, Shift = 4, Alt = 8, Control = 16 }

public static class MouseEncoder
{
    private const int MotionFlag = 32;
    private const int ReleaseButton = 3;
    private const int ButtonMask = 3;
    private const int WheelMask = 64;
    private const int LegacyMaxCoordinate = 223;
    private const int LegacyOffset = 32;

    public static string? Encode(MouseTrackingMode mode, bool sgr, MouseEventKind kind, MouseButton button, int column, int row, MouseModifiers modifiers = MouseModifiers.None)
    {
        if (mode == MouseTrackingMode.None || !IsReported(mode, kind, button))
        {
            return null;
        }

        int code = (int)button;
        if (mode != MouseTrackingMode.X10)
        {
            code |= (int)modifiers;
        }

        if (kind == MouseEventKind.Motion)
        {
            code |= MotionFlag;
        }

        int x = Math.Max(0, column) + 1, y = Math.Max(0, row) + 1;

        if (sgr)
        {
            char final = kind == MouseEventKind.Release ? 'm' : 'M';
            if (kind == MouseEventKind.Release && button == MouseButton.None)
            {
                code = ReleaseButton | (int)modifiers;
            }

            return $"\x1b[<{code};{x};{y}{final}";
        }

        if (kind == MouseEventKind.Release)
        {
            code = ReleaseButton | (code & ~ButtonMask & ~WheelMask);
        }

        if (x > LegacyMaxCoordinate || y > LegacyMaxCoordinate)
        {
            return null;
        }

        var sb = new StringBuilder("\x1b[M");
        sb.Append((char)(LegacyOffset + code)).Append((char)(LegacyOffset + x)).Append((char)(LegacyOffset + y));
        return sb.ToString();
    }

    private static bool IsReported(MouseTrackingMode mode, MouseEventKind kind, MouseButton button) => kind switch
    {
        MouseEventKind.Release => mode != MouseTrackingMode.X10 && button is not (MouseButton.WheelUp or MouseButton.WheelDown),
        MouseEventKind.Motion => mode == MouseTrackingMode.AnyEvent || (mode == MouseTrackingMode.ButtonEvent && button != MouseButton.None),
        _ => true,
    };
}
