// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace Edex.Core.Terminal;

public sealed partial class TerminalBuffer
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
}
