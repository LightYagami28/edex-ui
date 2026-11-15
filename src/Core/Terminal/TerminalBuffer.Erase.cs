// SPDX-License-Identifier: GPL-3.0-only

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
{
    private static readonly Action<TerminalBuffer>[] EraseDisplayModes =
    [
        b => b.EraseBelow(),
        b => b.EraseAbove(),
        b => b.EraseAll(),
        b => b.EraseScrollback(),
    ];

    private void EraseDisplay(int mode)
    {
        if (mode >= 0 && mode < EraseDisplayModes.Length)
        {
            EraseDisplayModes[mode](this);
        }
    }

    private void EraseBelow()
    {
        EraseLine(0);
        for (int r = _cursorY + 1; r < Rows; r++)
        {
            _screen[r] = NewLine(Columns, BlankWithBackground);
        }
    }

    private void EraseAbove()
    {
        EraseLine(1);
        for (int r = 0; r < _cursorY; r++)
        {
            _screen[r] = NewLine(Columns, BlankWithBackground);
        }
    }

    private void EraseAll()
    {
        for (int r = 0; r < Rows; r++)
        {
            _screen[r] = NewLine(Columns, BlankWithBackground);
        }
    }

    private void EraseScrollback()
    {
        _scrollback.Clear();
        _scrollOffset = 0;
    }

    private void EraseLine(int mode)
    {
        var line = _screen[_cursorY];
        int from = mode == 0 ? _cursorX : 0;
        int to = mode == 1 ? _cursorX : Columns - 1;
        for (int x = from; x <= to && x < Columns; x++)
        {
            line[x] = BlankWithBackground;
        }
    }

    private void InsertLines(int count)
    {
        if (_cursorY < _scrollTop || _cursorY > _scrollBottom)
        {
            return;
        }

        count = Math.Min(count, _scrollBottom - _cursorY + 1);
        for (int n = 0; n < count; n++)
        {
            for (int row = _scrollBottom; row > _cursorY; row--)
            {
                _screen[row] = _screen[row - 1];
            }

            _screen[_cursorY] = NewLine(Columns, BlankWithBackground);
        }
        _cursorX = 0;
    }

    private void DeleteLines(int count)
    {
        if (_cursorY < _scrollTop || _cursorY > _scrollBottom)
        {
            return;
        }

        count = Math.Min(count, _scrollBottom - _cursorY + 1);
        for (int n = 0; n < count; n++)
        {
            for (int row = _cursorY; row < _scrollBottom; row++)
            {
                _screen[row] = _screen[row + 1];
            }

            _screen[_scrollBottom] = NewLine(Columns, BlankWithBackground);
        }
        _cursorX = 0;
    }

    private void DeleteChars(int count)
    {
        var line = _screen[_cursorY];
        count = Math.Min(count, Columns - _cursorX);
        Array.Copy(line, _cursorX + count, line, _cursorX, Columns - _cursorX - count);
        for (int x = Columns - count; x < Columns; x++)
        {
            line[x] = BlankWithBackground;
        }
    }

    private void InsertChars(int count)
    {
        var line = _screen[_cursorY];
        count = Math.Min(count, Columns - _cursorX);
        Array.Copy(line, _cursorX, line, _cursorX + count, Columns - _cursorX - count);
        for (int x = _cursorX; x < _cursorX + count; x++)
        {
            line[x] = BlankWithBackground;
        }
    }
}
