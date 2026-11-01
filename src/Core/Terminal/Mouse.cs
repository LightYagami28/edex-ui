// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Terminal;

/// <summary>Mouse reporting requested by the running program (DECSET 9/1000/1002/1003).</summary>
public enum MouseTrackingMode
{
    None,
    /// <summary>Presses only.</summary>
    X10,
    /// <summary>Presses and releases.</summary>
    Normal,
    /// <summary>Presses, releases and motion while a button is held.</summary>
    ButtonEvent,
    /// <summary>All motion.</summary>
    AnyEvent,
}

public enum MouseButton { Left = 0, Middle = 1, Right = 2, None = 3, WheelUp = 64, WheelDown = 65 }

public enum MouseEventKind { Press, Release, Motion }

[Flags]
public enum MouseModifiers { None = 0, Shift = 4, Alt = 8, Control = 16 }

/// <summary>Encodes mouse events as xterm escape sequences.</summary>
public static class MouseEncoder
{
    /// <summary>
    /// Returns the bytes to send for an event at the zero-based cell (<paramref name="column"/>, <paramref name="row"/>),
    /// or null when the current mode does not report this kind of event.
    /// </summary>
    public static string? Encode(MouseTrackingMode mode, bool sgr, MouseEventKind kind, MouseButton button, int column, int row, MouseModifiers modifiers = MouseModifiers.None)
    {
        if (mode == MouseTrackingMode.None) return null;
        bool wheel = button is MouseButton.WheelUp or MouseButton.WheelDown;
        switch (kind)
        {
            case MouseEventKind.Release when mode == MouseTrackingMode.X10 || wheel:
                return null;
            case MouseEventKind.Motion when mode == MouseTrackingMode.AnyEvent:
                break;
            case MouseEventKind.Motion when mode == MouseTrackingMode.ButtonEvent && button != MouseButton.None:
                break;
            case MouseEventKind.Motion:
                return null;
        }

        int code = (int)button;
        if (mode != MouseTrackingMode.X10) code |= (int)modifiers;
        if (kind == MouseEventKind.Motion) code |= 32;
        int x = Math.Max(0, column) + 1, y = Math.Max(0, row) + 1;

        if (sgr)
        {
            char final = kind == MouseEventKind.Release ? 'm' : 'M';
            if (kind == MouseEventKind.Release && button == MouseButton.None) code = 3 | (int)modifiers;
            return $"\x1b[<{code};{x};{y}{final}";
        }

        // Legacy encoding: releases are reported as button 3 and coordinates are limited to 223.
        if (kind == MouseEventKind.Release) code = 3 | (code & ~3 & ~64);
        if (x > 223 || y > 223) return null;
        var sb = new StringBuilder("\x1b[M");
        sb.Append((char)(32 + code)).Append((char)(32 + x)).Append((char)(32 + y));
        return sb.ToString();
    }
}
