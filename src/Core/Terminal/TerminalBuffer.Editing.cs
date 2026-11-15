// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
{
    private const int TabWidth = 8;
    private const int DeviceStatusReportCursorPosition = 6;
    private const int DeviceStatusReportTerminal = 5;
    private const int AlternateScreenSaveCursorMode = 1049;

    private void LineFeed()
    {
        _wrapPending = false;
        if (_cursorY == _scrollBottom)
        {
            ScrollUp(1);
        }
        else
        {
            _cursorY = Math.Min(_cursorY + 1, Rows - 1);
        }
    }

    private void ReverseIndex()
    {
        _wrapPending = false;
        if (_cursorY == _scrollTop)
        {
            ScrollDown(1);
        }
        else
        {
            _cursorY = Math.Max(_cursorY - 1, 0);
        }
    }

    private void ScrollUp(int count)
    {
        count = Math.Min(count, _scrollBottom - _scrollTop + 1);
        for (int n = 0; n < count; n++)
        {
            var top = _screen[_scrollTop];
            if (_scrollTop == 0 && !IsAlternateScreen)
            {
                PushScrollback(top);
            }

            for (int row = _scrollTop; row < _scrollBottom; row++)
            {
                _screen[row] = _screen[row + 1];
            }

            _screen[_scrollBottom] = NewLine(Columns, BlankWithBackground);
        }
    }

    private void ScrollDown(int count)
    {
        count = Math.Min(count, _scrollBottom - _scrollTop + 1);
        for (int n = 0; n < count; n++)
        {
            for (int row = _scrollBottom; row > _scrollTop; row--)
            {
                _screen[row] = _screen[row - 1];
            }

            _screen[_scrollTop] = NewLine(Columns, BlankWithBackground);
        }
    }

    private void CarriageReturn()
    {
        _cursorX = 0;
        _wrapPending = false;
    }

    private void Backspace()
    {
        if (_wrapPending)
        {
            _wrapPending = false;
        }
        else
        {
            _cursorX = Math.Max(0, _cursorX - 1);
        }
    }

    private void Tab()
    {
        _wrapPending = false;
        _cursorX = Math.Min(Columns - 1, (_cursorX / TabWidth + 1) * TabWidth);
    }

    private void SaveCursor()
    {
        _savedX = _cursorX;
        _savedY = _cursorY;
    }

    private void ResetMargins()
    {
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
    }

    private void RestoreCursor()
    {
        _cursorX = Math.Clamp(_savedX, 0, Columns - 1);
        _cursorY = Math.Clamp(_savedY, 0, Rows - 1);
        _wrapPending = false;
    }

    private void FullReset()
    {
        _main = NewScreen(Columns, Rows);
        _alternate = null;
        _screen = _main;
        _cursorX = _cursorY = 0;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        _fg = _bg = 0;
        _attrs = CellAttributes.None;
        _wrapPending = false;
        ApplicationCursorKeys = BracketedPaste = SgrMouse = false;
        MouseTracking = MouseTrackingMode.None;
        CursorVisible = true;
    }

    private (int top, int bottom) RowLimits()
    {
        if (_cursorY >= _scrollTop && _cursorY <= _scrollBottom)
        {
            return (_scrollTop, _scrollBottom);
        }

        return (0, Rows - 1);
    }

    private void CursorUp(int[] p) { _cursorY = Math.Max(RowLimits().top, _cursorY - Arg(p, 0)); }

    private void CursorDown(int[] p) { _cursorY = Math.Min(RowLimits().bottom, _cursorY + Arg(p, 0)); }

    private void CursorForward(int[] p) { _cursorX = Math.Min(Columns - 1, _cursorX + Arg(p, 0)); }

    private void CursorBack(int[] p) { _cursorX = Math.Max(0, _cursorX - Arg(p, 0)); }

    private void NextLine(int[] p)
    {
        _cursorX = 0;
        CursorDown(p);
    }

    private void PreviousLine(int[] p)
    {
        _cursorX = 0;
        CursorUp(p);
    }

    private void CursorColumn(int[] p) { _cursorX = Math.Clamp(Arg(p, 0) - 1, 0, Columns - 1); }

    private void CursorRow(int[] p) { _cursorY = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1); }

    private void CursorPosition(int[] p)
    {
        _cursorY = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1);
        _cursorX = Math.Clamp(Arg(p, 1) - 1, 0, Columns - 1);
    }

    private void DeviceAttributes(int[] p)
    {
        if (Arg(p, 0, 0) == 0)
        {
            _responses.Append("\x1b[?62;22c");
        }
    }

    private void EraseChars(int count)
    {
        for (int i = 0; i < count && _cursorX + i < Columns; i++)
        {
            _screen[_cursorY][_cursorX + i] = BlankWithBackground;
        }
    }

    private void SetScrollRegion(int[] p)
    {
        int top = Arg(p, 0) - 1;
        int bottom = Arg(p, 1, Rows) - 1;
        bool valid = top < bottom && bottom < Rows;
        _scrollTop = valid ? top : 0;
        _scrollBottom = valid ? bottom : Rows - 1;
        _cursorX = 0;
        _cursorY = 0;
    }

    private void DeviceStatus(int request)
    {
        if (request == DeviceStatusReportCursorPosition)
        {
            _responses.Append(CultureInfo.InvariantCulture, $"\x1b[{_cursorY + 1};{_cursorX + 1}R");
            return;
        }

        if (request == DeviceStatusReportTerminal)
        {
            _responses.Append("\x1b[0n");
        }
    }

    private void PrivateMode(char final, int[] p)
    {
        if (final != 'h' && final != 'l')
        {
            return;
        }

        bool on = final == 'h';
        foreach (var mode in p)
        {
            SetPrivateMode(mode, on);
        }
    }

    private static readonly Dictionary<int, Action<TerminalBuffer, bool>> PrivateModes = new()
    {
        [1] = (b, on) => b.ApplicationCursorKeys = on,
        [25] = (b, on) => b.CursorVisible = on,
        [2004] = (b, on) => b.BracketedPaste = on,
        [9] = (b, on) => b.MouseTracking = Tracking(on, MouseTrackingMode.X10),
        [1000] = (b, on) => b.MouseTracking = Tracking(on, MouseTrackingMode.Normal),
        [1002] = (b, on) => b.MouseTracking = Tracking(on, MouseTrackingMode.ButtonEvent),
        [1003] = (b, on) => b.MouseTracking = Tracking(on, MouseTrackingMode.AnyEvent),
        [1006] = (b, on) => b.SgrMouse = on,
        [47] = (b, on) => b.SwitchScreen(47, on),
        [1047] = (b, on) => b.SwitchScreen(1047, on),
        [1049] = (b, on) => b.SwitchScreen(1049, on),
    };

    private void SetPrivateMode(int mode, bool on)
    {
        if (PrivateModes.TryGetValue(mode, out var action))
        {
            action(this, on);
        }
    }

    private static MouseTrackingMode Tracking(bool on, MouseTrackingMode mode) => on ? mode : MouseTrackingMode.None;

    private void SwitchScreen(int mode, bool on)
    {
        if (on != IsAlternateScreen)
        {
            ToggleAlternateScreen(on, mode == AlternateScreenSaveCursorMode);
        }

        ResetMargins();
    }

    private void ToggleAlternateScreen(bool on, bool keepCursor)
    {
        if (on)
        {
            EnterAlternateScreen(keepCursor);
        }
        else
        {
            LeaveAlternateScreen(keepCursor);
        }
    }

    private void EnterAlternateScreen(bool saveCursor)
    {
        if (saveCursor)
        {
            SaveCursor();
        }

        _alternate = NewScreen(Columns, Rows);
        _screen = _alternate;
        _cursorX = _cursorY = 0;
    }

    private void LeaveAlternateScreen(bool restoreCursor)
    {
        _screen = _main;
        _alternate = null;
        if (restoreCursor)
        {
            RestoreCursor();
        }
    }
}
