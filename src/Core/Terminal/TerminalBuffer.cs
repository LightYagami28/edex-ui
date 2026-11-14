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

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground:
                ProcessGround(c);
                break;
            case State.Escape:
                EscapeChar(c);
                break;
            case State.EscapeIntermediate:
                if (c >= IntermediateFirst && c <= IntermediateLast)
                {
                    _state = State.Ground;
                }

                break;
            case State.Charset:
                _state = State.Ground;
                break;
            case State.Csi:
                ProcessCsi(c);
                break;
            case State.Osc:
                ProcessOsc(c);
                break;
            case State.OscEscape:
                if (c == '\\')
                {
                    Osc();
                }

                _state = State.Ground;
                break;
            case State.Dcs:
                ProcessDcs(c);
                break;
            case State.DcsEscape:
                _state = c == '\\' ? State.Ground : State.Dcs;
                break;
        }
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

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[':
                _params.Clear();
                _csiPrefix = '\0';
                _state = State.Csi;
                break;
            case ']':
                _osc.Clear();
                _state = State.Osc;
                break;
            case 'P': case 'X': case '^': case '_':
                _state = State.Dcs;
                break;
            case '(': case ')': case '*': case '+':
                _state = State.Charset;
                break;
            case '#': case '%': case ' ':
                _state = State.EscapeIntermediate;
                break;
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break;
            case 'E': _cursorX = 0; LineFeed(); break;
            case 'M': ReverseIndex(); break;
            case 'c': FullReset(); break;
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
        bool intermediate = _params.Length > 0 && _params[^1] is ' ' or '!' or '"' or '$' or '\'';
        if (intermediate)
        {
            if (final == 'p' && _params.ToString() == "!")
            {
                FullReset();
            }

            return;
        }
        if (_csiPrefix == '?') { PrivateMode(final, p); return; }
        if (_csiPrefix is '>' or '<' or '=')
        {
            if (final == 'c' && _csiPrefix == '>')
            {
                _responses.Append("\x1b[>0;10;0c");
            }

            return;
        }

        CsiCommand(final, p);
        if (final != 'm' && final != 'n' && final != 'c')
        {
            _wrapPending = false;
        }
    }

    private void CsiCommand(char final, int[] p)
    {
        int rowLimitTop = 0, rowLimitBottom = Rows - 1;
        if (_cursorY >= _scrollTop && _cursorY <= _scrollBottom) { rowLimitTop = _scrollTop; rowLimitBottom = _scrollBottom; }

        switch (final)
        {
            case 'A': _cursorY = Math.Max(rowLimitTop, _cursorY - Arg(p, 0)); break;
            case 'B': case 'e': _cursorY = Math.Min(rowLimitBottom, _cursorY + Arg(p, 0)); break;
            case 'C': case 'a': _cursorX = Math.Min(Columns - 1, _cursorX + Arg(p, 0)); break;
            case 'D': _cursorX = Math.Max(0, _cursorX - Arg(p, 0)); break;
            case 'E': _cursorX = 0; _cursorY = Math.Min(rowLimitBottom, _cursorY + Arg(p, 0)); break;
            case 'F': _cursorX = 0; _cursorY = Math.Max(rowLimitTop, _cursorY - Arg(p, 0)); break;
            case 'G': case '`': _cursorX = Math.Clamp(Arg(p, 0) - 1, 0, Columns - 1); break;
            case 'd': _cursorY = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1); break;
            case 'H': case 'f':
                _cursorY = Math.Clamp(Arg(p, 0) - 1, 0, Rows - 1);
                _cursorX = Math.Clamp(Arg(p, 1) - 1, 0, Columns - 1);
                break;
            case 'J': EraseDisplay(Arg(p, 0, 0)); break;
            case 'K': EraseLine(Arg(p, 0, 0)); break;
            case 'L': InsertLines(Arg(p, 0)); break;
            case 'M': DeleteLines(Arg(p, 0)); break;
            case 'P': DeleteChars(Arg(p, 0)); break;
            case '@': InsertChars(Arg(p, 0)); break;
            case 'X': EraseChars(Arg(p, 0)); break;
            case 'S': ScrollUp(Arg(p, 0)); break;
            case 'T': ScrollDown(Arg(p, 0)); break;
            case 'm': Sgr(p); break;
            case 'r': SetScrollRegion(p); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'n': DeviceStatus(Arg(p, 0, 0)); break;
            case 'c':
                if (Arg(p, 0, 0) == 0)
                {
                    _responses.Append("\x1b[?62;22c");
                }

                break;
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

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: ApplicationCursorKeys = on; break;
            case 25: CursorVisible = on; break;
            case 2004: BracketedPaste = on; break;
            case 9: MouseTracking = Tracking(on, MouseTrackingMode.X10); break;
            case 1000: MouseTracking = Tracking(on, MouseTrackingMode.Normal); break;
            case 1002: MouseTracking = Tracking(on, MouseTrackingMode.ButtonEvent); break;
            case 1003: MouseTracking = Tracking(on, MouseTrackingMode.AnyEvent); break;
            case 1006: SgrMouse = on; break;
            case 47: case 1047: case 1049: SwitchScreen(mode, on); break;
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
            case 0:
                EraseLine(0);
                for (int r = _cursorY + 1; r < Rows; r++)
                {
                    _screen[r] = NewLine(Columns, BlankWithBackground);
                }

                break;
            case 1:
                EraseLine(1);
                for (int r = 0; r < _cursorY; r++)
                {
                    _screen[r] = NewLine(Columns, BlankWithBackground);
                }

                break;
            case 2:
                for (int r = 0; r < Rows; r++)
                {
                    _screen[r] = NewLine(Columns, BlankWithBackground);
                }

                break;
            case 3:
                _scrollback.Clear();
                _scrollOffset = 0;
                break;
        }
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

    private void Sgr(int[] p)
    {
        if (p.Length == 0) { _fg = _bg = 0; _attrs = CellAttributes.None; return; }
        int i = 0;
        while (i < p.Length)
        {
            int code = p[i++];
            switch (code)
            {
                case 0: _fg = _bg = 0; _attrs = CellAttributes.None; break;
                case 1: _attrs |= CellAttributes.Bold; break;
                case 2: _attrs |= CellAttributes.Dim; break;
                case 3: _attrs |= CellAttributes.Italic; break;
                case 4: _attrs |= CellAttributes.Underline; break;
                case 7: _attrs |= CellAttributes.Inverse; break;
                case 8: _attrs |= CellAttributes.Hidden; break;
                case 9: _attrs |= CellAttributes.Strikethrough; break;
                case 21: case 22: _attrs &= ~(CellAttributes.Bold | CellAttributes.Dim); break;
                case 23: _attrs &= ~CellAttributes.Italic; break;
                case 24: _attrs &= ~CellAttributes.Underline; break;
                case 27: _attrs &= ~CellAttributes.Inverse; break;
                case 28: _attrs &= ~CellAttributes.Hidden; break;
                case 29: _attrs &= ~CellAttributes.Strikethrough; break;
                case >= 30 and <= 37: _fg = TerminalCell.Palette(code - 30); break;
                case 39: _fg = 0; break;
                case >= 40 and <= 47: _bg = TerminalCell.Palette(code - 40); break;
                case 49: _bg = 0; break;
                case >= 90 and <= 97: _fg = TerminalCell.Palette(code - 90 + 8); break;
                case >= 100 and <= 107: _bg = TerminalCell.Palette(code - 100 + 8); break;
                case 38: case 48:
                    i = ExtendedColor(p, i, code == 38);
                    break;
            }
        }
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
