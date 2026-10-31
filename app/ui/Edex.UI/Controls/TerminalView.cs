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
        const double padX = 6, padY = 4;

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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_session is null) return;
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
        e.Handled = true;
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
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                _session.Buffer.ResetViewport();
                _session.Paste(text);
            }
            return;
        }
        if (appShortcut && e.Key == Key.C)
        {
            e.Handled = true;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(_session.Buffer.Snapshot().Text);
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

    private void SendUserInput(string text)
    {
        _session!.Buffer.ResetViewport();
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
