// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;

namespace Edex.Core.Terminal;

public sealed class TerminalBuffer
{
    private enum State { Ground, Escape, EscapeIntermediate, Csi, Osc, OscEscape, Charset, Dcs, DcsEscape }

    private const int CharBufferInitialSize = 4096;
    private const int CharBufferSlack = 4;
    private const int MaxOscLength = 4096;
    private const int TabWidth = 8;
    private const int DeviceStatusReportCursorPosition = 6;
    private const int DeviceStatusReportTerminal = 5;
    private const int AlternateScreenSaveCursorMode = 1049;
    private const char Bell = '\a';
    private const char Cancel = '\x18';
    private const char Substitute = '\x1A';
    private const char Escape = '\x1B';
    private const char Delete = '\x7F';
    private const char CsiFinalFirst = '@';
    private const char CsiFinalLast = '~';
    private const char IntermediateFirst = '0';
    private const char IntermediateLast = '~';

    private readonly object _gate = new();
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly List<TerminalCell[]> _scrollback = [];
    private readonly StringBuilder _params = new();
    private readonly StringBuilder _osc = new();
    private readonly StringBuilder _responses = new();
    private char[] _charBuffer = new char[CharBufferInitialSize];

    private TerminalCell[][] _main;
    private TerminalCell[][]? _alternate;
    private TerminalCell[][] _screen;
    private State _state = State.Ground;
    private char _csiPrefix;
    private int _cursorX, _cursorY, _savedX, _savedY;
    private bool _wrapPending;
    private int _scrollTop, _scrollBottom;
    private uint _fg, _bg;
    private CellAttributes _attrs;
    private int _scrollOffset;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int ScrollbackLimit { get; }
    public string Title { get; private set; } = "";
    public bool ApplicationCursorKeys { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public MouseTrackingMode MouseTracking { get; private set; }
    public bool SgrMouse { get; private set; }
    public bool IsAlternateScreen => _alternate is not null && ReferenceEquals(_screen, _alternate);

    public event Action<string>? Response;

    public TerminalBuffer(int columns = 80, int rows = 24, int scrollbackLimit = 2000)
    {
        Columns = Math.Max(2, columns);
        Rows = Math.Max(2, rows);
        ScrollbackLimit = scrollbackLimit;
        _main = NewScreen(Columns, Rows);
        _screen = _main;
        _scrollBottom = Rows - 1;
    }

    private static TerminalCell[][] NewScreen(int columns, int rows)
    {
        var screen = new TerminalCell[rows][];
        for (int i = 0; i < rows; i++)
        {
            screen[i] = NewLine(columns);
        }

        return screen;
    }

    private static TerminalCell[] NewLine(int columns, TerminalCell? fill = null)
    {
        var line = new TerminalCell[columns];
        Array.Fill(line, fill ?? TerminalCell.Blank);
        return line;
    }

    private TerminalCell BlankWithBackground => new(' ', 0, _bg, CellAttributes.None);

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    public void Feed(ReadOnlySpan<byte> data)
    {
        string? responses = null;
        lock (_gate)
        {
            if (_charBuffer.Length < data.Length + CharBufferSlack)
            {
                _charBuffer = new char[data.Length + CharBufferSlack];
            }

            int count = _decoder.GetChars(data, _charBuffer, flush: false);
            for (int i = 0; i < count; i++)
            {
                Process(_charBuffer[i]);
            }

            if (_responses.Length > 0)
            {
                responses = _responses.ToString();
                _responses.Clear();
            }
        }
        if (responses is not null)
        {
            Response?.Invoke(responses);
        }
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(2, columns);
        rows = Math.Max(2, rows);
        lock (_gate)
        {
            if (columns == Columns && rows == Rows)
            {
                return;
            }

            bool alternate = IsAlternateScreen;
            int oldCursorY = _cursorY;
            _main = ResizeScreen(_main, columns, rows, pushToScrollback: !alternate, alternate ? rows - 1 : oldCursorY);
            if (_alternate is not null)
            {
                _alternate = ResizeScreen(_alternate, columns, rows, pushToScrollback: false, oldCursorY);
            }

            _screen = alternate ? _alternate! : _main;
            int shift = Math.Max(0, (oldCursorY + 1) - rows);
            Columns = columns;
            Rows = rows;
            _cursorY = Math.Clamp(oldCursorY - shift, 0, rows - 1);
            _cursorX = Math.Clamp(_cursorX, 0, columns - 1);
            _scrollTop = 0;
            _scrollBottom = rows - 1;
            _wrapPending = false;
            _scrollOffset = Math.Min(_scrollOffset, _scrollback.Count);
        }
    }

    private TerminalCell[][] ResizeScreen(TerminalCell[][] screen, int columns, int rows, bool pushToScrollback, int cursorY)
    {
        var lines = screen.ToList();
        while (lines.Count > rows)
        {
            if (lines.Count - 1 > cursorY && IsBlank(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
            else
            {
                if (pushToScrollback)
                {
                    PushScrollback(lines[0]);
                }

                lines.RemoveAt(0);
                cursorY--;
            }
        }
        while (lines.Count < rows)
        {
            lines.Add(NewLine(columns));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == columns)
            {
                continue;
            }

            var line = NewLine(columns);
            Array.Copy(lines[i], line, Math.Min(columns, lines[i].Length));
            lines[i] = line;
        }
        return lines.ToArray();
    }

    private static bool IsBlank(TerminalCell[] line) => line.All(c => c.Glyph == ' ' && c.Background == 0);

    private void PushScrollback(TerminalCell[] line)
    {
        _scrollback.Add(line);
        if (_scrollback.Count > ScrollbackLimit)
        {
            _scrollback.RemoveAt(0);
            return;
        }

        if (_scrollOffset > 0)
        {
            _scrollOffset++;
        }
    }

    public void ScrollViewport(int delta)
    {
        lock (_gate)
        {
            _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, _scrollback.Count);
        }
    }

    public void ResetViewport()
    {
        lock (_gate)
        {
            _scrollOffset = 0;
        }
    }

    public TerminalSnapshot Snapshot()
    {
        lock (_gate)
        {
            var lines = new TerminalCell[Rows][];
            int offset = IsAlternateScreen ? 0 : _scrollOffset;
            for (int row = 0; row < Rows; row++)
            {
                int source = row - offset;
                TerminalCell[] line = source < 0 ? _scrollback[_scrollback.Count + source] : _screen[source];
                var copy = NewLine(Columns);
                Array.Copy(line, copy, Math.Min(Columns, line.Length));
                lines[row] = copy;
            }
            return new TerminalSnapshot
            {
                Columns = Columns,
                Rows = Rows,
                Lines = lines,
                CursorX = Math.Min(_cursorX, Columns - 1),
                CursorY = _cursorY + offset,
                CursorVisible = CursorVisible && offset == 0,
                ScrollOffset = offset,
                ScrollbackLength = _scrollback.Count,
            };
        }
    }

    public string ScrollbackText()
    {
        lock (_gate)
        {
            var sb = new StringBuilder();
            foreach (var line in _scrollback.Concat(_main))
            {
                sb.Append(new string(line.Select(c => c.Glyph).ToArray()).TrimEnd()).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }
    }

    private static readonly Dictionary<State, Action<TerminalBuffer, char>> StateActions = new()
    {
        [State.Ground] = (b, c) => b.ProcessGround(c),
        [State.Escape] = (b, c) => b.EscapeChar(c),
        [State.EscapeIntermediate] = (b, c) => b.ProcessEscapeIntermediate(c),
        [State.Charset] = (b, _) => b._state = State.Ground,
        [State.Csi] = (b, c) => b.ProcessCsi(c),
        [State.Osc] = (b, c) => b.ProcessOsc(c),
        [State.OscEscape] = (b, c) => b.ProcessOscEscape(c),
        [State.Dcs] = (b, c) => b.ProcessDcs(c),
        [State.DcsEscape] = (b, c) => b.ProcessDcsEscape(c),
    };

    private void Process(char c)
    {
        if (StateActions.TryGetValue(_state, out var action))
        {
            action(this, c);
        }
    }

    private void ProcessEscapeIntermediate(char c)
    {
        if (c >= IntermediateFirst && c <= IntermediateLast)
        {
            _state = State.Ground;
        }
    }

    private void ProcessOscEscape(char c)
    {
        if (c == '\\')
        {
            Osc();
        }

        _state = State.Ground;
    }

    private void ProcessDcsEscape(char c)
    {
        _state = c == '\\' ? State.Ground : State.Dcs;
    }

    private void ProcessGround(char c)
    {
        if (c < ' ' || c == Delete)
        {
            Control(c);
        }
        else
        {
            Print(c);
        }
    }

    private void ProcessCsi(char c)
    {
        if (c >= CsiFinalFirst && c <= CsiFinalLast) { Csi(c); _state = State.Ground; }
        else if (c == Escape)
        {
            _state = State.Escape;
        }
        else if (c == Cancel || c == Substitute)
        {
            _state = State.Ground;
        }
        else if (c >= ' ')
        {
            _params.Append(c);
        }
        else
        {
            Control(c);
        }
    }

    private void ProcessOsc(char c)
    {
        if (c == Bell)
        {
            Osc();
            _state = State.Ground;
            return;
        }

        if (c == Escape)
        {
            _state = State.OscEscape;
            return;
        }

        if (_osc.Length < MaxOscLength)
        {
            _osc.Append(c);
        }
    }

    private void ProcessDcs(char c)
    {
        if (c == Escape)
        {
            _state = State.DcsEscape;
            return;
        }

        if (c == Bell)
        {
            _state = State.Ground;
        }
    }

    private static readonly Dictionary<char, Action<TerminalBuffer>> EscapeActions = new()
    {
        ['['] = b => { b._params.Clear(); b._csiPrefix = '\0'; b._state = State.Csi; },
        [']'] = b => { b._osc.Clear(); b._state = State.Osc; },
        ['P'] = b => b._state = State.Dcs,
        ['X'] = b => b._state = State.Dcs,
        ['^'] = b => b._state = State.Dcs,
        ['_'] = b => b._state = State.Dcs,
        ['('] = b => b._state = State.Charset,
        [')'] = b => b._state = State.Charset,
        ['*'] = b => b._state = State.Charset,
        ['+'] = b => b._state = State.Charset,
        ['#'] = b => b._state = State.EscapeIntermediate,
        ['%'] = b => b._state = State.EscapeIntermediate,
        [' '] = b => b._state = State.EscapeIntermediate,
        ['7'] = b => b.SaveCursor(),
        ['8'] = b => b.RestoreCursor(),
        ['D'] = b => b.LineFeed(),
        ['E'] = b => { b._cursorX = 0; b.LineFeed(); },
        ['M'] = b => b.ReverseIndex(),
        ['c'] = b => b.FullReset(),
    };

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        if (EscapeActions.TryGetValue(c, out var action))
        {
            action(this);
        }
    }

    private void Control(char c)
    {
        switch (c)
        {
            case '\x1B': _state = State.Escape; break;
            case '\r': _cursorX = 0; _wrapPending = false; break;
            case '\n': case '\v': case '\f': LineFeed(); break;
            case '\b':
                if (_wrapPending)
                {
                    _wrapPending = false;
                }
                else
                {
                    _cursorX = Math.Max(0, _cursorX - 1);
                }

                break;
            case '\t':
                _wrapPending = false;
                _cursorX = Math.Min(Columns - 1, (_cursorX / TabWidth + 1) * TabWidth);
                break;
        }
    }

    private void Print(char c)
    {
        if (_wrapPending)
        {
            _cursorX = 0;
            LineFeed();
            _wrapPending = false;
        }
        _screen[_cursorY][_cursorX] = new TerminalCell(c, _fg, _bg, _attrs);
        if (_cursorX == Columns - 1)
        {
            _wrapPending = true;
        }
        else
        {
            _cursorX++;
        }
    }

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

    private void SaveCursor() { _savedX = _cursorX; _savedY = _cursorY; }

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

    private void Osc()
    {
        var text = _osc.ToString();
        int sep = text.IndexOf(';');
        if (sep <= 0)
        {
            return;
        }

        var kind = text[..sep];
        if (kind is "0" or "2")
        {
            Title = text[(sep + 1)..];
        }
    }

    private int[] ParseParams()
    {
        var raw = _params.ToString();
        if (raw.Length > 0 && raw[0] is '?' or '>' or '<' or '=')
        {
            _csiPrefix = raw[0];
            raw = raw[1..];
        }
        if (raw.Length == 0)
        {
            return [];
        }

        var parts = raw.Split(ParamSeparators);
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var digits = new string(parts[i].TakeWhile(char.IsAsciiDigit).Take(9).ToArray());
            values[i] = digits.Length == 0 ? 0 : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static readonly char[] ParamSeparators = [';', ':'];

    private static int Arg(int[] p, int index, int fallback = 1) => index < p.Length && p[index] > 0 ? p[index] : fallback;

    private void Csi(char final)
    {
        var p = ParseParams();
        if (HasIntermediate(final) || HasPrefix(final, p))
        {
            return;
        }

        CsiCommand(final, p);
        if (final is not 'm' and not 'n' and not 'c')
        {
            _wrapPending = false;
        }
    }

    private bool HasIntermediate(char final)
    {
        if (_params.Length == 0 || _params[^1] is not (' ' or '!' or '"' or '$' or '\''))
        {
            return false;
        }

        if (final == 'p' && _params.ToString() == "!")
        {
            FullReset();
        }

        return true;
    }

    private bool HasPrefix(char final, int[] p)
    {
        if (_csiPrefix == '?')
        {
            PrivateMode(final, p);
            return true;
        }

        if (_csiPrefix is not ('>' or '<' or '='))
        {
            return false;
        }

        if (final == 'c' && _csiPrefix == '>')
        {
            _responses.Append("\x1b[>0;10;0c");
        }

        return true;
    }

    private static readonly Dictionary<char, Action<TerminalBuffer, int[]>> CsiActions = new()
    {
        ['A'] = (b, p) => b.CursorUp(p),
        ['B'] = (b, p) => b.CursorDown(p),
        ['e'] = (b, p) => b.CursorDown(p),
        ['C'] = (b, p) => b.CursorForward(p),
        ['a'] = (b, p) => b.CursorForward(p),
        ['D'] = (b, p) => b.CursorBack(p),
        ['E'] = (b, p) => b.NextLine(p),
        ['F'] = (b, p) => b.PreviousLine(p),
        ['G'] = (b, p) => b.CursorColumn(p),
        ['`'] = (b, p) => b.CursorColumn(p),
        ['d'] = (b, p) => b.CursorRow(p),
        ['H'] = (b, p) => b.CursorPosition(p),
        ['f'] = (b, p) => b.CursorPosition(p),
        ['J'] = (b, p) => b.EraseDisplay(Arg(p, 0, 0)),
        ['K'] = (b, p) => b.EraseLine(Arg(p, 0, 0)),
        ['L'] = (b, p) => b.InsertLines(Arg(p, 0)),
        ['M'] = (b, p) => b.DeleteLines(Arg(p, 0)),
        ['P'] = (b, p) => b.DeleteChars(Arg(p, 0)),
        ['@'] = (b, p) => b.InsertChars(Arg(p, 0)),
        ['X'] = (b, p) => b.EraseChars(Arg(p, 0)),
        ['S'] = (b, p) => b.ScrollUp(Arg(p, 0)),
        ['T'] = (b, p) => b.ScrollDown(Arg(p, 0)),
        ['m'] = (b, p) => b.Sgr(p),
        ['r'] = (b, p) => b.SetScrollRegion(p),
        ['s'] = (b, p) => b.SaveCursor(),
        ['u'] = (b, p) => b.RestoreCursor(),
        ['n'] = (b, p) => b.DeviceStatus(Arg(p, 0, 0)),
        ['c'] = (b, p) => b.DeviceAttributes(p),
    };

    private void CsiCommand(char final, int[] p)
    {
        if (CsiActions.TryGetValue(final, out var action))
        {
            action(this, p);
        }
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

    private void NextLine(int[] p) { _cursorX = 0; _cursorY = Math.Min(RowLimits().bottom, _cursorY + Arg(p, 0)); }

    private void PreviousLine(int[] p) { _cursorX = 0; _cursorY = Math.Max(RowLimits().top, _cursorY - Arg(p, 0)); }

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
        int top = Arg(p, 0) - 1, bottom = Arg(p, 1, Rows) - 1;
        if (top < bottom && bottom < Rows) { _scrollTop = top; _scrollBottom = bottom; }
        else { _scrollTop = 0; _scrollBottom = Rows - 1; }
        _cursorX = 0;
        _cursorY = 0;
    }

    private void DeviceStatus(int request)
    {
        if (request == DeviceStatusReportCursorPosition)
        {
            _responses.Append(CultureInfo.InvariantCulture, $"\x1b[{_cursorY + 1};{_cursorX + 1}R");
        }
        else if (request == DeviceStatusReportTerminal)
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
        if (on && !IsAlternateScreen)
        {
            if (mode == AlternateScreenSaveCursorMode)
            {
                SaveCursor();
            }

            _alternate = NewScreen(Columns, Rows);
            _screen = _alternate;
            _cursorX = _cursorY = 0;
        }
        else if (!on && IsAlternateScreen)
        {
            _screen = _main;
            _alternate = null;
            if (mode == AlternateScreenSaveCursorMode)
            {
                RestoreCursor();
            }
        }
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: EraseBelow(); break;
            case 1: EraseAbove(); break;
            case 2: EraseAll(); break;
            case 3: EraseScrollback(); break;
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

    private static readonly Dictionary<int, Action<TerminalBuffer>> SgrSimple = new()
    {
        [0] = b => { b._fg = b._bg = 0; b._attrs = CellAttributes.None; },
        [1] = b => b._attrs |= CellAttributes.Bold,
        [2] = b => b._attrs |= CellAttributes.Dim,
        [3] = b => b._attrs |= CellAttributes.Italic,
        [4] = b => b._attrs |= CellAttributes.Underline,
        [7] = b => b._attrs |= CellAttributes.Inverse,
        [8] = b => b._attrs |= CellAttributes.Hidden,
        [9] = b => b._attrs |= CellAttributes.Strikethrough,
        [21] = b => b._attrs &= ~(CellAttributes.Bold | CellAttributes.Dim),
        [22] = b => b._attrs &= ~(CellAttributes.Bold | CellAttributes.Dim),
        [23] = b => b._attrs &= ~CellAttributes.Italic,
        [24] = b => b._attrs &= ~CellAttributes.Underline,
        [27] = b => b._attrs &= ~CellAttributes.Inverse,
        [28] = b => b._attrs &= ~CellAttributes.Hidden,
        [29] = b => b._attrs &= ~CellAttributes.Strikethrough,
        [39] = b => b._fg = 0,
        [49] = b => b._bg = 0,
    };

    private void Sgr(int[] p)
    {
        if (p.Length == 0)
        {
            _fg = _bg = 0;
            _attrs = CellAttributes.None;
            return;
        }

        int i = 0;
        while (i < p.Length)
        {
            int code = p[i++];
            if (code is 38 or 48)
            {
                i = ExtendedColor(p, i, code == 38);
            }
            else
            {
                ApplySimpleOrPaletteSgr(code);
            }
        }
    }

    private void ApplySimpleOrPaletteSgr(int code)
    {
        if (SetStandardForeground(code))
        {
            return;
        }

        if (SetStandardBackground(code))
        {
            return;
        }

        if (SetBrightForeground(code))
        {
            return;
        }

        if (SetBrightBackground(code))
        {
            return;
        }

        TryApplySimpleSgr(code);
    }

    private bool SetStandardForeground(int code)
    {
        if (code < 30 || code > 37)
        {
            return false;
        }

        _fg = TerminalCell.Palette(code - 30);
        return true;
    }

    private bool SetStandardBackground(int code)
    {
        if (code < 40 || code > 47)
        {
            return false;
        }

        _bg = TerminalCell.Palette(code - 40);
        return true;
    }

    private bool SetBrightForeground(int code)
    {
        if (code < 90 || code > 97)
        {
            return false;
        }

        _fg = TerminalCell.Palette(code - 90 + 8);
        return true;
    }

    private bool SetBrightBackground(int code)
    {
        if (code < 100 || code > 107)
        {
            return false;
        }

        _bg = TerminalCell.Palette(code - 100 + 8);
        return true;
    }

    private bool TryApplySimpleSgr(int code)
    {
        if (!SgrSimple.TryGetValue(code, out var action))
        {
            return false;
        }

        action(this);
        return true;
    }

    private int ExtendedColor(int[] p, int next, bool foreground)
    {
        uint color;
        int end;
        if (next + 1 < p.Length && p[next] == 5) { color = TerminalCell.Palette(p[next + 1]); end = next + 2; }
        else if (next + 3 < p.Length && p[next] == 2) { color = TerminalCell.Rgb(p[next + 1], p[next + 2], p[next + 3]); end = next + 4; }
        else
        {
            return p.Length;
        }

        if (foreground)
        {
            _fg = color;
        }
        else
        {
            _bg = color;
        }

        return end;
    }
}
