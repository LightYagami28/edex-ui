// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Text;
using Edex.Core.Pty;

namespace Edex.Core.Terminal;

public sealed class TerminalSession : IDisposable
{
    private const int ReadChunkSize = 16 * 1024;
    private const int DefaultScrollback = 2000;

    private readonly IPtyConnection _pty;
    private readonly Lock _writeGate = new();
    private int _disposed;
    private int _exitReported;

    public TerminalBuffer Buffer { get; }
    public string Shell { get; }
    public int ProcessId => _pty.ProcessId;
    public bool HasExited { get; private set; }
    public int? ExitCode { get; private set; }

    public event EventHandler? Updated;
    public event EventHandler<int>? Exited;

    private TerminalSession(IPtyConnection pty, TerminalBuffer buffer, string shell)
    {
        _pty = pty;
        Buffer = buffer;
        Shell = shell;
        Buffer.Response += Send;
        _pty.Exited += (_, code) => OnExited(code);
        if (_pty.ExitCode is int early)
        {
            OnExited(early);
        }

        var reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        reader.Start();
    }

    public static TerminalSession Start(PtyOptions options) => Start(options, DefaultScrollback);

    public static TerminalSession Start(PtyOptions options, int scrollback)
    {
        return Attach(PtyFactory.Spawn(options), options.Shell, new TerminalBuffer(options.Columns, options.Rows, scrollback));
    }

    internal static TerminalSession Attach(IPtyConnection pty, string shell, TerminalBuffer buffer) => new(pty, buffer, shell);

    private void OnExited(int code)
    {
        if (Interlocked.Exchange(ref _exitReported, 1) != 0)
        {
            return;
        }

        ExitCode = code;
        HasExited = true;
        Exited?.Invoke(this, code);
    }

    private void ReadLoop()
    {
        var chunk = new byte[ReadChunkSize];
        try
        {
            bool running = true;
            while (running && Volatile.Read(ref _disposed) == 0)
            {
                running = ReadChunk(chunk);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Trace.TraceInformation($"TerminalSession: lettura terminata, pty chiuso: {e.GetType().Name}: {e.Message}");
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private bool ReadChunk(byte[] chunk)
    {
        int read = _pty.Output.Read(chunk, 0, chunk.Length);
        if (read <= 0)
        {
            return false;
        }

        Buffer.Feed(chunk.AsSpan(0, read));
        Updated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Send(string text)
    {
        if (string.IsNullOrEmpty(text) || Volatile.Read(ref _disposed) != 0 || HasExited)
        {
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (_writeGate)
            {
                _pty.Input.Write(bytes, 0, bytes.Length);
                _pty.Input.Flush();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Trace.TraceWarning($"TerminalSession: scrittura sul pty fallita, sessione chiusa: {e.GetType().Name}: {e.Message}");
        }
    }

    public void Paste(string text)
    {
        text = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        Send(Buffer.BracketedPaste ? $"\x1b[200~{text}\x1b[201~" : text);
    }

    public void Resize(int columns, int rows)
    {
        if (columns == Buffer.Columns && rows == Buffer.Rows)
        {
            return;
        }

        Buffer.Resize(columns, rows);
        _pty.Resize(columns, rows);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Buffer.Response -= Send;
        _pty.Dispose();
    }
}
