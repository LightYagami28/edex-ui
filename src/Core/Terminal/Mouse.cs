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

public readonly record struct MouseEvent(MouseEventKind Kind, MouseButton Button, CellPosition At, MouseModifiers Modifiers)
{
    public MouseEvent(MouseEventKind kind, MouseButton button, CellPosition at) : this(kind, button, at, MouseModifiers.None)
    {
    }
}

public static class MouseEncoder
{
    private const int MotionFlag = 32;
    private const int ReleaseButton = 3;
    private const int ButtonMask = 3;
    private const int WheelMask = 64;
    private const int LegacyMaxCoordinate = 223;
    private const int LegacyOffset = 32;

    public static string? Encode(MouseTrackingMode mode, bool sgr, MouseEvent mouse)
    {
        if (mode == MouseTrackingMode.None || !IsReported(mode, mouse.Kind, mouse.Button))
        {
            return null;
        }

        int code = Code(mode, mouse);
        int x = Math.Max(0, mouse.At.Column) + 1;
        int y = Math.Max(0, mouse.At.Row) + 1;
        return sgr ? SgrEncode(mouse.Kind, SgrCode(mouse, code), x, y) : LegacyEncode(mouse.Kind, code, x, y);
    }

    private static int Code(MouseTrackingMode mode, MouseEvent mouse)
    {
        int code = (int)mouse.Button;
        if (mode != MouseTrackingMode.X10)
        {
            code |= (int)mouse.Modifiers;
        }

        return mouse.Kind == MouseEventKind.Motion ? code | MotionFlag : code;
    }

    private static int SgrCode(MouseEvent mouse, int code) =>
        mouse.Kind == MouseEventKind.Release && mouse.Button == MouseButton.None ? ReleaseButton | (int)mouse.Modifiers : code;

    private static string SgrEncode(MouseEventKind kind, int code, int x, int y)
    {
        char final = kind == MouseEventKind.Release ? 'm' : 'M';
        return $"\x1b[<{code};{x};{y}{final}";
    }

    private static string? LegacyEncode(MouseEventKind kind, int code, int x, int y)
    {
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
