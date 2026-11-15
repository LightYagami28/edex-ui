// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;

namespace Edex.Core.Terminal;

public sealed class TerminalBuffer
{
    private enum State { Ground, Escape, EscapeIntermediate, Csi, Osc, OscEscape, Charset, Dcs, DcsEscape }

    private const int MinSize = 2;
    private const int DefaultColumns = 80;
    private const int DefaultRows = 24;
    private const int DefaultScrollback = 2000;
    private const int CharBufferInitialSize = 4096;
    private const int CharBufferSlack = 4;

    private readonly Lock _gate = new();
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
    private int _cursorX;
    private int _cursorY;
    private int _savedX;
    private int _savedY;
    private bool _wrapPending;
    private int _scrollTop;
    private int _scrollBottom;
    private uint _fg;
    private uint _bg;
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

    public TerminalBuffer() : this(DefaultColumns, DefaultRows)
    {
    }

    public TerminalBuffer(int columns, int rows) : this(columns, rows, DefaultScrollback)
    {
    }

    public TerminalBuffer(int columns, int rows, int scrollbackLimit)
    {
        Columns = Math.Max(MinSize, columns);
        Rows = Math.Max(MinSize, rows);
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
        string? responses;
        lock (_gate)
        {
            Decode(data);
            responses = TakeResponses();
        }
        if (responses is not null)
        {
            Response?.Invoke(responses);
        }
    }

    private void Decode(ReadOnlySpan<byte> data)
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
    }

    private string? TakeResponses()
    {
        if (_responses.Length == 0)
        {
            return null;
        }

        var text = _responses.ToString();
        _responses.Clear();
        return text;
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(MinSize, columns);
        rows = Math.Max(MinSize, rows);
        lock (_gate)
        {
            if (columns != Columns || rows != Rows)
            {
                ResizeLocked(columns, rows);
            }
        }
    }

    private void ResizeLocked(int columns, int rows)
    {
        bool alternate = IsAlternateScreen;
        int oldCursorY = _cursorY;
        var size = (columns, rows);
        _main = ResizeScreen(_main, size, alternate ? rows - 1 : oldCursorY, !alternate);
        _alternate = _alternate is null ? null : ResizeScreen(_alternate, size, oldCursorY, false);
        _screen = alternate ? _alternate! : _main;
        int shift = Math.Max(0, (oldCursorY + 1) - rows);
        Columns = columns;
        Rows = rows;
        _cursorY = Math.Clamp(oldCursorY - shift, 0, rows - 1);
        _cursorX = Math.Clamp(_cursorX, 0, columns - 1);
        ResetMargins();
        _wrapPending = false;
        _scrollOffset = Math.Min(_scrollOffset, _scrollback.Count);
    }

    private TerminalCell[][] ResizeScreen(TerminalCell[][] screen, (int Columns, int Rows) size, int cursorY, bool pushToScrollback)
    {
        var lines = screen.ToList();
        while (lines.Count > size.Rows)
        {
            cursorY -= DropLine(lines, cursorY, pushToScrollback);
        }
        while (lines.Count < size.Rows)
        {
            lines.Add(NewLine(size.Columns));
        }

        return lines.Select(line => line.Length == size.Columns ? line : CopyLine(line, size.Columns)).ToArray();
    }

    private int DropLine(List<TerminalCell[]> lines, int cursorY, bool pushToScrollback)
    {
        if (lines.Count - 1 > cursorY && IsBlank(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
            return 0;
        }

        if (pushToScrollback)
        {
            PushScrollback(lines[0]);
        }

        lines.RemoveAt(0);
        return 1;
    }

    private static TerminalCell[] CopyLine(TerminalCell[] line, int columns)
    {
        var copy = NewLine(columns);
        Array.Copy(line, copy, Math.Min(columns, line.Length));
        return copy;
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
            int offset = IsAlternateScreen ? 0 : _scrollOffset;
            return new TerminalSnapshot
            {
                Columns = Columns,
                Rows = Rows,
                Lines = VisibleLines(offset),
                CursorX = Math.Min(_cursorX, Columns - 1),
                CursorY = _cursorY + offset,
                CursorVisible = CursorVisible && offset == 0,
                ScrollOffset = offset,
                ScrollbackLength = _scrollback.Count,
            };
        }
    }

    private TerminalCell[][] VisibleLines(int offset)
    {
        var lines = new TerminalCell[Rows][];
        for (int row = 0; row < Rows; row++)
        {
            int source = row - offset;
            lines[row] = CopyLine(source < 0 ? _scrollback[_scrollback.Count + source] : _screen[source], Columns);
        }
        return lines;
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

    private const int MaxOscLength = 4096;
    private const int MaxParamDigits = 9;
    private const char Bell = '\a';
    private const char Cancel = '\x18';
    private const char Substitute = '\x1A';
    private const char Escape = '\x1B';
    private const char Delete = '\x7F';
    private const char CsiFinalFirst = '@';
    private const char CsiFinalLast = '~';
    private const char IntermediateFirst = '0';
    private const char IntermediateLast = '~';
    private const string IntermediateChars = " !\"$'";

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
        if (c >= CsiFinalFirst && c <= CsiFinalLast)
        {
            FinishCsi(c);
        }
        else if (c >= ' ')
        {
            _params.Append(c);
        }
        else
        {
            CsiControl(c);
        }
    }

    private void FinishCsi(char c)
    {
        Csi(c);
        _state = State.Ground;
    }

    private void CsiControl(char c)
    {
        if (c == Escape)
        {
            _state = State.Escape;
        }
        else if (c is Cancel or Substitute)
        {
            _state = State.Ground;
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

    private static readonly Dictionary<char, Action<TerminalBuffer>> EscapeActions = BuildEscapeActions();

    private static Dictionary<char, Action<TerminalBuffer>> BuildEscapeActions()
    {
        var actions = new Dictionary<char, Action<TerminalBuffer>>
        {
            ['['] = b => b.BeginCsi(),
            [']'] = b => b.BeginOsc(),
            ['7'] = b => b.SaveCursor(),
            ['8'] = b => b.RestoreCursor(),
            ['D'] = b => b.LineFeed(),
            ['E'] = b => b.NextLineFeed(),
            ['M'] = b => b.ReverseIndex(),
            ['c'] = b => b.FullReset(),
        };
        AddStateChanges(actions, "PX^_", State.Dcs);
        AddStateChanges(actions, "()*+", State.Charset);
        AddStateChanges(actions, "#% ", State.EscapeIntermediate);
        return actions;
    }

    private static void AddStateChanges(Dictionary<char, Action<TerminalBuffer>> actions, string keys, State state)
    {
        foreach (char key in keys)
        {
            actions[key] = b => b._state = state;
        }
    }

    private void BeginCsi()
    {
        _params.Clear();
        _csiPrefix = '\0';
        _state = State.Csi;
    }

    private void BeginOsc()
    {
        _osc.Clear();
        _state = State.Osc;
    }

    private void NextLineFeed()
    {
        _cursorX = 0;
        LineFeed();
    }

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        if (EscapeActions.TryGetValue(c, out var action))
        {
            action(this);
        }
    }

    private static readonly Dictionary<char, Action<TerminalBuffer>> ControlActions = new()
    {
        [Escape] = b => b._state = State.Escape,
        ['\r'] = b => b.CarriageReturn(),
        ['\n'] = b => b.LineFeed(),
        ['\v'] = b => b.LineFeed(),
        ['\f'] = b => b.LineFeed(),
        ['\b'] = b => b.Backspace(),
        ['\t'] = b => b.Tab(),
    };

    private void Control(char c)
    {
        if (ControlActions.TryGetValue(c, out var action))
        {
            action(this);
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
        var raw = StripPrefix(_params.ToString());
        return raw.Length == 0 ? [] : raw.Split(ParamSeparators).Select(ParseParam).ToArray();
    }

    private string StripPrefix(string raw)
    {
        if (raw.Length == 0 || raw[0] is not ('?' or '>' or '<' or '='))
        {
            return raw;
        }

        _csiPrefix = raw[0];
        return raw[1..];
    }

    private static int ParseParam(string part)
    {
        var digits = new string(part.TakeWhile(char.IsAsciiDigit).Take(MaxParamDigits).ToArray());
        return digits.Length == 0 ? 0 : int.Parse(digits, CultureInfo.InvariantCulture);
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
        if (_params.Length == 0 || !IntermediateChars.Contains(_params[^1], StringComparison.Ordinal))
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

    private static readonly Dictionary<char, Action<TerminalBuffer, int[]>> CsiActions = BuildCsiActions();

    private static Dictionary<char, Action<TerminalBuffer, int[]>> BuildCsiActions()
    {
        var actions = new Dictionary<char, Action<TerminalBuffer, int[]>>
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
        };
        AddEditActions(actions);
        return actions;
    }

    private static void AddEditActions(Dictionary<char, Action<TerminalBuffer, int[]>> actions)
    {
        actions['H'] = (b, p) => b.CursorPosition(p);
        actions['f'] = (b, p) => b.CursorPosition(p);
        actions['J'] = (b, p) => b.EraseDisplay(Arg(p, 0, 0));
        actions['K'] = (b, p) => b.EraseLine(Arg(p, 0, 0));
        actions['L'] = (b, p) => b.InsertLines(Arg(p, 0));
        actions['M'] = (b, p) => b.DeleteLines(Arg(p, 0));
        actions['P'] = (b, p) => b.DeleteChars(Arg(p, 0));
        actions['@'] = (b, p) => b.InsertChars(Arg(p, 0));
        actions['X'] = (b, p) => b.EraseChars(Arg(p, 0));
        actions['S'] = (b, p) => b.ScrollUp(Arg(p, 0));
        actions['T'] = (b, p) => b.ScrollDown(Arg(p, 0));
        actions['m'] = (b, p) => b.Sgr(p);
        actions['r'] = (b, p) => b.SetScrollRegion(p);
        actions['s'] = (b, _) => b.SaveCursor();
        actions['u'] = (b, _) => b.RestoreCursor();
        actions['n'] = (b, p) => b.DeviceStatus(Arg(p, 0, 0));
        actions['c'] = (b, p) => b.DeviceAttributes(p);
    }

    private void CsiCommand(char final, int[] p)
    {
        if (CsiActions.TryGetValue(final, out var action))
        {
            action(this, p);
        }
    }

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

    private const int SgrExtendedForeground = 38;
    private const int SgrExtendedBackground = 48;
    private const int SgrForegroundFirst = 30;
    private const int SgrForegroundLast = 37;
    private const int SgrBackgroundFirst = 40;
    private const int SgrBackgroundLast = 47;
    private const int SgrBrightForegroundFirst = 90;
    private const int SgrBrightForegroundLast = 97;
    private const int SgrBrightBackgroundFirst = 100;
    private const int SgrBrightBackgroundLast = 107;
    private const int BrightPaletteOffset = 8;
    private const int SgrColorIndexed = 5;
    private const int SgrColorRgb = 2;
    private const int IndexedColorArgs = 1;
    private const int RgbColorArgs = 3;
    private const int RedIndex = 0;
    private const int GreenIndex = 1;
    private const int BlueIndex = 2;

    private static readonly Dictionary<int, Action<TerminalBuffer>> SgrSimple = new()
    {
        [0] = b => b.ResetAttributes(),
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

    private static readonly (int First, int Last, int Offset, bool Foreground)[] SgrPaletteRanges =
    [
        (SgrForegroundFirst, SgrForegroundLast, 0, true),
        (SgrBackgroundFirst, SgrBackgroundLast, 0, false),
        (SgrBrightForegroundFirst, SgrBrightForegroundLast, BrightPaletteOffset, true),
        (SgrBrightBackgroundFirst, SgrBrightBackgroundLast, BrightPaletteOffset, false),
    ];

    private void ResetAttributes()
    {
        _fg = 0;
        _bg = 0;
        _attrs = CellAttributes.None;
    }

    private void Sgr(int[] p)
    {
        int[] codes = p.Length == 0 ? [0] : p;
        int i = 0;
        while (i < codes.Length)
        {
            int code = codes[i++];
            if (code is SgrExtendedForeground or SgrExtendedBackground)
            {
                i = ExtendedColor(codes, i, code == SgrExtendedForeground);
            }
            else
            {
                ApplySimpleOrPaletteSgr(code);
            }
        }
    }

    private void ApplySimpleOrPaletteSgr(int code)
    {
        foreach (var range in SgrPaletteRanges)
        {
            if (code >= range.First && code <= range.Last)
            {
                SetColor(TerminalCell.Palette(code - range.First + range.Offset), range.Foreground);
                return;
            }
        }

        if (SgrSimple.TryGetValue(code, out var action))
        {
            action(this);
        }
    }

    private void SetColor(uint color, bool foreground)
    {
        if (foreground)
        {
            _fg = color;
        }
        else
        {
            _bg = color;
        }
    }

    private int ExtendedColor(int[] p, int next, bool foreground)
    {
        if (!TryReadExtendedColor(p, next, out uint color, out int end))
        {
            return p.Length;
        }

        SetColor(color, foreground);
        return end;
    }

    private static bool TryReadExtendedColor(int[] p, int next, out uint color, out int end)
    {
        if (next + IndexedColorArgs < p.Length && p[next] == SgrColorIndexed)
        {
            color = TerminalCell.Palette(p[next + 1]);
            end = next + IndexedColorArgs + 1;
            return true;
        }

        if (next + RgbColorArgs < p.Length && p[next] == SgrColorRgb)
        {
            var rgb = p.AsSpan(next + 1, RgbColorArgs);
            color = TerminalCell.Rgb(rgb[RedIndex], rgb[GreenIndex], rgb[BlueIndex]);
            end = next + RgbColorArgs + 1;
            return true;
        }

        color = 0;
        end = p.Length;
        return false;
    }
}
