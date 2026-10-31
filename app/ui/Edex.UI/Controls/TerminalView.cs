// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Edex.Core.Terminal;
using MouseButton = Edex.Core.Terminal.MouseButton;

namespace Edex.UI.Controls;

/// <summary>Renders a <see cref="TerminalSession"/> and forwards keyboard input to it.</summary>
public sealed class TerminalView : Control
{
    private readonly DispatcherTimer _blink;
    private TerminalSession? _session;
    private Palette _palette;
    private Typeface _typeface;
    private Typeface _boldTypeface;
    private double _fontSize = 14;
    private double _cellWidth = 8, _cellHeight = 16;
    private bool _cursorOn = true;
    private int _renderQueued;
    private TextSelection? _selection;
    private bool _selecting;
    private MouseButton _reportButton = MouseButton.None;
    private CellPosition _lastMotionCell = new(-1, -1);
    private const double PadX = 6, PadY = 4;

    public TerminalView(Palette palette)
    {
        _palette = palette;
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        SetFont(palette.Theme.FontFamily, _fontSize);
        _blink = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) =>
        {
            _cursorOn = !_cursorOn;
            InvalidateVisual();
        });
    }

    public TerminalSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null) _session.Updated -= OnSessionUpdated;
            _session = value;
            if (_session is not null)
            {
                _session.Updated += OnSessionUpdated;
                ResizeSession();
            }
            InvalidateVisual();
        }
    }

    public double CellWidth => _cellWidth;
    public double CellHeight => _cellHeight;

    public void ApplyPalette(Palette palette)
    {
        _palette = palette;
        SetFont(palette.Theme.FontFamily, _fontSize);
        InvalidateVisual();
    }

    public void SetFont(string family, double size)
    {
        _fontSize = size;
        var fontFamily = new FontFamily(family);
        _typeface = new Typeface(fontFamily);
        _boldTypeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Bold);
        var probe = new FormattedText("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.White);
        _cellWidth = Math.Max(1, probe.WidthIncludingTrailingWhitespace / 10.0);
        _cellHeight = Math.Max(1, Math.Ceiling(probe.Height));
        ResizeSession();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _blink.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blink.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSessionUpdated(object? sender, EventArgs e)
    {
        // Coalesce bursts of output into one repaint per frame.
        if (Interlocked.Exchange(ref _renderQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeSession();
    }

    private void ResizeSession()
    {
        if (_session is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        int columns = Math.Max(10, (int)((Bounds.Width - 12) / _cellWidth));
        int rows = Math.Max(3, (int)((Bounds.Height - 8) / _cellHeight));
        _session.Resize(columns, rows);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_palette.Brush(Color.FromArgb(0xE6, _palette.TerminalBackground.R, _palette.TerminalBackground.G, _palette.TerminalBackground.B)), new Rect(Bounds.Size));
        if (_session is null) return;
        var snapshot = _session.Buffer.Snapshot();
        const double padX = PadX, padY = PadY;

        for (int row = 0; row < snapshot.Rows; row++)
        {
            var line = snapshot.Lines[row];
            double y = padY + row * _cellHeight;
            int col = 0;
            while (col < line.Length)
            {
                var cell = line[col];
                var (fg, bg) = Colors(cell);
                int start = col;
                var text = new StringBuilder();
                while (col < line.Length)
                {
                    var next = line[col];
                    if (next.Attributes != cell.Attributes || Colors(next) != (fg, bg)) break;
                    text.Append(next.Char == '\0' ? ' ' : next.Char);
                    col++;
                }
                var rect = new Rect(padX + start * _cellWidth, y, (col - start) * _cellWidth, _cellHeight);
                if (bg != _palette.TerminalBackground) context.FillRectangle(_palette.Brush(bg), rect);
                var str = text.ToString();
                if (cell.Attributes.HasFlag(CellAttributes.Hidden) || string.IsNullOrWhiteSpace(str)) continue;
                var brush = _palette.Brush(cell.Attributes.HasFlag(CellAttributes.Dim) ? Color.FromArgb(0xAA, fg.R, fg.G, fg.B) : fg);
                var formatted = new FormattedText(str, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    cell.Attributes.HasFlag(CellAttributes.Bold) ? _boldTypeface : _typeface, _fontSize, brush);
                if (cell.Attributes.HasFlag(CellAttributes.Underline))
                    formatted.SetTextDecorations(TextDecorations.Underline);
                if (cell.Attributes.HasFlag(CellAttributes.Strikethrough))
                    formatted.SetTextDecorations(TextDecorations.Strikethrough);
                context.DrawText(formatted, rect.TopLeft);
            }
        }

        if (snapshot.CursorVisible && snapshot.CursorY < snapshot.Rows)
        {
            var cursorRect = new Rect(padX + snapshot.CursorX * _cellWidth, padY + snapshot.CursorY * _cellHeight, _cellWidth, _cellHeight);
            if (!IsFocused)
                context.DrawRectangle(new Pen(_palette.Brush(_palette.Cursor), 1), cursorRect.Deflate(0.5));
            else if (_cursorOn)
            {
                context.FillRectangle(_palette.Brush(_palette.Cursor), cursorRect);
                var ch = snapshot.Lines[snapshot.CursorY][snapshot.CursorX].Char;
                if (ch != ' ' && ch != '\0')
                {
                    var under = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize,
                        _palette.Brush(_palette.TerminalBackground));
                    context.DrawText(under, cursorRect.TopLeft);
                }
            }
        }

        if (_selection is { IsEmpty: false } selection)
        {
            var brush = _palette.Brush(Color.FromArgb(0x60, _palette.Accent.R, _palette.Accent.G, _palette.Accent.B));
            for (int row = Math.Max(0, selection.Start.Row); row <= Math.Min(snapshot.Rows - 1, selection.End.Row); row++)
            {
                int from = row == selection.Start.Row ? selection.Start.Column : 0;
                int to = row == selection.End.Row ? selection.End.Column : snapshot.Columns - 1;
                if (to < from) continue;
                context.FillRectangle(brush, new Rect(padX + from * _cellWidth, padY + row * _cellHeight, (to - from + 1) * _cellWidth, _cellHeight));
            }
        }

        if (snapshot.ScrollOffset > 0)
        {
            var label = new FormattedText($"↑ {snapshot.ScrollOffset}/{snapshot.ScrollbackLength}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize * 0.8, _palette.AccentBrush);
            context.DrawText(label, new Point(Bounds.Width - label.Width - 10, 4));
        }
    }

    private (Color Fg, Color Bg) Colors(TerminalCell cell)
    {
        bool bold = cell.Attributes.HasFlag(CellAttributes.Bold);
        var fg = _palette.Resolve(cell.Foreground, true, bold);
        var bg = _palette.Resolve(cell.Background, false, false);
        return cell.Attributes.HasFlag(CellAttributes.Inverse) ? (bg, fg) : (fg, bg);
    }

    /// <summary>Text currently selected with the mouse, or null.</summary>
    public string? SelectedText => _session is not null && _selection is { IsEmpty: false } sel ? sel.GetText(_session.Buffer.Snapshot()) : null;

    public void ClearSelection()
    {
        if (_selection is null) return;
        _selection = null;
        InvalidateVisual();
    }

    private CellPosition CellAt(Point point)
    {
        int columns = _session?.Buffer.Columns ?? 1, rows = _session?.Buffer.Rows ?? 1;
        int col = (int)Math.Floor((point.X - PadX) / _cellWidth);
        int row = (int)Math.Floor((point.Y - PadY) / _cellHeight);
        return new CellPosition(Math.Clamp(col, 0, columns - 1), Math.Clamp(row, 0, rows - 1));
    }

    private bool ReportsMouse(KeyModifiers mods) =>
        _session is not null && _session.Buffer.MouseTracking != MouseTrackingMode.None && !mods.HasFlag(KeyModifiers.Shift);

    private static MouseModifiers ToMouseModifiers(KeyModifiers mods) =>
        (mods.HasFlag(KeyModifiers.Shift) ? MouseModifiers.Shift : 0) |
        (mods.HasFlag(KeyModifiers.Alt) ? MouseModifiers.Alt : 0) |
        (mods.HasFlag(KeyModifiers.Control) ? MouseModifiers.Control : 0);

    private void Report(MouseEventKind kind, MouseButton button, CellPosition cell, KeyModifiers mods)
    {
        var buffer = _session!.Buffer;
        var sequence = MouseEncoder.Encode(buffer.MouseTracking, buffer.SgrMouse, kind, button, cell.Column, cell.Row, ToMouseModifiers(mods));
        if (sequence is not null) _session.Send(sequence);
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
        if (_session is null) return;
        var point = e.GetCurrentPoint(this);
        var cell = CellAt(point.Position);
        var button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
            PointerUpdateKind.RightButtonPressed => MouseButton.Right,
            _ => MouseButton.None,
        };

        if (ReportsMouse(e.KeyModifiers) && button != MouseButton.None)
        {
            _reportButton = button;
            _lastMotionCell = cell;
            e.Pointer.Capture(this);
            Report(MouseEventKind.Press, button, cell, e.KeyModifiers);
            return;
        }

        switch (button)
        {
            case MouseButton.Left:
                var snapshot = _session.Buffer.Snapshot();
                _selection = e.ClickCount switch
                {
                    2 => TextSelection.Word(snapshot, cell),
                    >= 3 => TextSelection.Line(snapshot, cell.Row),
                    _ => new TextSelection(cell, cell),
                };
                _selecting = e.ClickCount == 1;
                e.Pointer.Capture(this);
                InvalidateVisual();
                break;
            case MouseButton.Right:
                // Windows Terminal convention: right click copies a selection, otherwise pastes.
                if (SelectedText is { Length: > 0 } text)
                {
                    await CopyAsync(text);
                    ClearSelection();
                }
                else await PasteAsync();
                break;
            case MouseButton.Middle:
                await PasteAsync();
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session is null) return;
        var cell = CellAt(e.GetPosition(this));
        if (_selecting && _selection is { } selection)
        {
            if (selection.Active != cell)
            {
                _selection = selection with { Active = cell };
                InvalidateVisual();
            }
            return;
        }
        if (ReportsMouse(e.KeyModifiers) && cell != _lastMotionCell)
        {
            _lastMotionCell = cell;
            Report(MouseEventKind.Motion, _reportButton, cell, e.KeyModifiers);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_session is null) return;
        e.Pointer.Capture(null);
        if (_reportButton != MouseButton.None)
        {
            Report(MouseEventKind.Release, _reportButton, CellAt(e.GetPosition(this)), e.KeyModifiers);
            _reportButton = MouseButton.None;
            return;
        }
        if (_selecting)
        {
            _selecting = false;
            if (_selection is { IsEmpty: true }) ClearSelection();
        }
    }

    private async Task CopyAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && text.Length > 0) await clipboard.SetTextAsync(text);
    }

    private async Task PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || _session is null) return;
        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        _session.Buffer.ResetViewport();
        _session.Paste(text);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_session is null) return;
        e.Handled = true;
        if (ReportsMouse(e.KeyModifiers))
        {
            Report(MouseEventKind.Press, e.Delta.Y > 0 ? MouseButton.WheelUp : MouseButton.WheelDown, CellAt(e.GetPosition(this)), e.KeyModifiers);
            return;
        }
        ClearSelection();
        if (_session.Buffer.IsAlternateScreen)
        {
            // Full-screen programs (less, vim, htop...) get arrow keys instead of scrollback.
            var key = e.Delta.Y > 0 ? "A" : "B";
            for (int i = 0; i < 3; i++) _session.Send(_session.Buffer.ApplicationCursorKeys ? $"\x1bO{key}" : $"\x1b[{key}");
        }
        else
        {
            _session.Buffer.ScrollViewport((int)Math.Round(e.Delta.Y * 3));
        }
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_session is null || string.IsNullOrEmpty(e.Text)) return;
        var text = new string(e.Text.Where(c => c >= ' ' && c != '\x7f').ToArray());
        if (text.Length == 0) return;
        SendUserInput(text);
        e.Handled = true;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null) return;
        var mods = e.KeyModifiers;
        bool appShortcut = EdexHost.Profile.UseCommandKey
            ? mods.HasFlag(KeyModifiers.Meta)
            : mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Shift);

        if (appShortcut && e.Key == Key.V)
        {
            e.Handled = true;
            await PasteAsync();
            return;
        }
        if (appShortcut && e.Key == Key.C)
        {
            e.Handled = true;
            await CopyAsync(SelectedText ?? _session.Buffer.Snapshot().Text);
            return;
        }
        if (appShortcut && e.Key == Key.A)
        {
            e.Handled = true;
            var snapshot = _session.Buffer.Snapshot();
            _selection = new TextSelection(new CellPosition(0, 0), new CellPosition(snapshot.Columns - 1, snapshot.Rows - 1));
            InvalidateVisual();
            return;
        }
        if (mods.HasFlag(KeyModifiers.Shift) && e.Key is Key.PageUp or Key.PageDown)
        {
            int page = Math.Max(1, _session.Buffer.Rows - 1);
            _session.Buffer.ScrollViewport(e.Key == Key.PageUp ? page : -page);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var sequence = TranslateKey(e.Key, mods, _session.Buffer.ApplicationCursorKeys);
        if (sequence is not null)
        {
            SendUserInput(sequence);
            e.Handled = true;
        }
    }

    /// <summary>Sends text as if typed (used by the on-screen keyboard too).</summary>
    public void SendUserInput(string text)
    {
        if (_session is null) return;
        _selection = null;
        _session.Buffer.ResetViewport();
        _cursorOn = true;
        _session.Send(text);
    }

    internal static string? TranslateKey(Key key, KeyModifiers mods, bool applicationCursor)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        int modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

        string Cursor(char final) => modifier > 1 ? $"\x1b[1;{modifier}{final}" : applicationCursor ? $"\x1bO{final}" : $"\x1b[{final}";
        string Tilde(int code) => modifier > 1 ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";

        if (ctrl && !alt && key >= Key.A && key <= Key.Z)
            return ((char)(key - Key.A + 1)).ToString();
        if (ctrl && key == Key.Space) return "\0";
        if (ctrl && key == Key.OemOpenBrackets) return "\x1b";
        if (ctrl && key == Key.OemPipe) return "\x1c";
        if (ctrl && key == Key.OemCloseBrackets) return "\x1d";
        if (alt && !ctrl && key >= Key.A && key <= Key.Z)
            return "\x1b" + (char)((shift ? 'A' : 'a') + (key - Key.A));

        return key switch
        {
            Key.Enter => "\r",
            Key.Back => ctrl ? "\x08" : "\x7f",
            Key.Tab => shift ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => Cursor('A'),
            Key.Down => Cursor('B'),
            Key.Right => Cursor('C'),
            Key.Left => Cursor('D'),
            Key.Home => Cursor('H'),
            Key.End => Cursor('F'),
            Key.Insert => Tilde(2),
            Key.Delete => Tilde(3),
            Key.PageUp => Tilde(5),
            Key.PageDown => Tilde(6),
            Key.F1 => modifier > 1 ? $"\x1b[1;{modifier}P" : "\x1bOP",
            Key.F2 => modifier > 1 ? $"\x1b[1;{modifier}Q" : "\x1bOQ",
            Key.F3 => modifier > 1 ? $"\x1b[1;{modifier}R" : "\x1bOR",
            Key.F4 => modifier > 1 ? $"\x1b[1;{modifier}S" : "\x1bOS",
            Key.F5 => Tilde(15),
            Key.F6 => Tilde(17),
            Key.F7 => Tilde(18),
            Key.F8 => Tilde(19),
            Key.F9 => Tilde(20),
            Key.F10 => Tilde(21),
            Key.F11 => Tilde(23),
            Key.F12 => Tilde(24),
            _ => null,
        };
    }
}
