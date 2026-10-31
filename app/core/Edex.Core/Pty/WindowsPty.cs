// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

/// <summary>Pseudo terminal backed by the Windows ConPTY API (Windows 10 1809+).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPty : IPtyConnection
{
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _process;
    private readonly FileStream _output;
    private readonly FileStream _input;
    private int _disposed;

    public int ProcessId { get; }
    public Stream Output => _output;
    public Stream Input => _input;
    public int? ExitCode { get; private set; }
    public event EventHandler<int>? Exited;

    private WindowsPty(IntPtr pseudoConsole, PROCESS_INFORMATION info, SafeFileHandle outputRead, SafeFileHandle inputWrite)
    {
        _pseudoConsole = pseudoConsole;
        _process = info.hProcess;
        ProcessId = info.dwProcessId;
        CloseHandle(info.hThread);
        _output = new FileStream(outputRead, FileAccess.Read, 1, false);
        _input = new FileStream(inputWrite, FileAccess.Write, 1, false);

        var waiter = new Thread(() =>
        {
            WaitForSingleObject(_process, INFINITE);
            GetExitCodeProcess(_process, out var code);
            CloseHandle(_process);
            ExitCode = unchecked((int)code);
            Exited?.Invoke(this, ExitCode.Value);
        })
        { IsBackground = true, Name = "conpty-exit-watcher" };
        waiter.Start();
    }

    public static WindowsPty Spawn(PtyOptions options)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();

        var size = new COORD { X = (short)Math.Clamp(options.Columns, 2, short.MaxValue), Y = (short)Math.Clamp(options.Rows, 2, short.MaxValue) };
        int hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pseudoConsole);
        // The pseudo console duplicated these ends; we must release ours.
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
        {
            outputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(hr, "CreatePseudoConsole failed");
        }

        IntPtr attributeList = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        try
        {
            IntPtr listSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attributeList = Marshal.AllocHGlobal(listSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, pseudoConsole,
                    (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            // Do not let the child inherit the standard handles of a GUI host.
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.lpAttributeList = attributeList;

            var env = new StringBuilder();
            foreach (var (key, value) in PtyFactory.BuildEnvironment(options.Environment).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                env.Append(key).Append('=').Append(value).Append('\0');
            env.Append('\0');
            environmentBlock = Marshal.StringToHGlobalUni(env.ToString());

            var commandLine = new StringBuilder(BuildCommandLine(options.Shell, options.Args));
            string? cwd = !string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory)
                ? options.WorkingDirectory
                : null;

            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, environmentBlock, cwd, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot start '{options.Shell}'");

            return new WindowsPty(pseudoConsole, info, outputRead, inputWrite);
        }
        catch
        {
            ClosePseudoConsole(pseudoConsole);
            outputRead.Dispose();
            inputWrite.Dispose();
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
        }
    }

    internal static string BuildCommandLine(string executable, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder(Quote(executable));
        foreach (var arg in args) sb.Append(' ').Append(Quote(arg));
        return sb.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0) return value;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') sb.Append('\\', backslashes * 2 + 1);
            else sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        ResizePseudoConsole(_pseudoConsole, new COORD { X = (short)Math.Max(2, columns), Y = (short)Math.Max(2, rows) });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _input.Dispose(); } catch (IOException) { }
        // ClosePseudoConsole can block until pending output is drained, so never run it on the UI thread.
        var hpc = _pseudoConsole;
        var output = _output;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ClosePseudoConsole(hpc);
            try { output.Dispose(); } catch (IOException) { }
        });
    }

    private const uint INFINITE = 0xFFFFFFFF;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr pseudoConsole, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
