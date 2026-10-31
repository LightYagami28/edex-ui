// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Edex.Core.Pty;

namespace Edex.Core.Terminal;

/// <summary>Couples a pseudo terminal with a <see cref="TerminalBuffer"/> and pumps output on a background thread.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly IPtyConnection _pty;
    private readonly Thread _reader;
    private readonly object _writeGate = new();
    private int _disposed;
    private int _exitReported;

    public TerminalBuffer Buffer { get; }
    public string Shell { get; }
    public int ProcessId => _pty.ProcessId;
    public bool HasExited { get; private set; }
    public int? ExitCode { get; private set; }

    /// <summary>Raised on the reader thread whenever new output has been parsed.</summary>
    public event EventHandler? Updated;
    public event EventHandler<int>? Exited;

    private TerminalSession(IPtyConnection pty, TerminalBuffer buffer, string shell)
    {
        _pty = pty;
        Buffer = buffer;
        Shell = shell;
        Buffer.Response += Send;
        _pty.Exited += (_, code) => OnExited(code);
        // The child may already be gone before we subscribed.
        if (_pty.ExitCode is int early) OnExited(early);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
    }

    public static TerminalSession Start(PtyOptions options, int scrollback = 2000)
    {
        var buffer = new TerminalBuffer(options.Columns, options.Rows, scrollback);
        var pty = PtyFactory.Spawn(options);
        return new TerminalSession(pty, buffer, options.Shell);
    }

    private void OnExited(int code)
    {
        if (Interlocked.Exchange(ref _exitReported, 1) != 0) return;
        ExitCode = code;
        HasExited = true;
        Exited?.Invoke(this, code);
    }

    private void ReadLoop()
    {
        var chunk = new byte[16 * 1024];
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                int read = _pty.Output.Read(chunk, 0, chunk.Length);
                if (read <= 0) break;
                Buffer.Feed(chunk.AsSpan(0, read));
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (IOException) { /* EIO on Unix when the child closes the tty */ }
        catch (ObjectDisposedException) { }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Send(string text)
    {
        if (string.IsNullOrEmpty(text) || Volatile.Read(ref _disposed) != 0 || HasExited) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (_writeGate)
            {
                _pty.Input.Write(bytes, 0, bytes.Length);
                _pty.Input.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Paste(string text)
    {
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        Send(Buffer.BracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text);
    }

    public void Resize(int columns, int rows)
    {
        if (columns == Buffer.Columns && rows == Buffer.Rows) return;
        Buffer.Resize(columns, rows);
        _pty.Resize(columns, rows);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Buffer.Response -= Send;
        _pty.Dispose();
    }
}
