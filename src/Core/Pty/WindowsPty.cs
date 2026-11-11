// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

/// <summary>Pseudo terminal backed by the Windows ConPTY API (Windows 10 1809+).</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPty : IPtyConnection
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

    private WindowsPty(IntPtr pseudoConsole, ProcessInformation info, SafeFileHandle outputRead, SafeFileHandle inputWrite)
    {
        _pseudoConsole = pseudoConsole;
        _process = info.hProcess;
        ProcessId = info.dwProcessId;
        CloseHandle(info.hThread);
        _output = new FileStream(outputRead, FileAccess.Read, 1, false);
        _input = new FileStream(inputWrite, FileAccess.Write, 1, false);

        var waiter = new Thread(() =>
        {
            _ = WaitForSingleObject(_process, INFINITE);
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
        var (inputRead, inputWrite) = Pipe();
        var (outputRead, outputWrite) = Pipe();
        bool consoleOwnsEnds = false;
        IntPtr pseudoConsole = IntPtr.Zero, attributeList = IntPtr.Zero, environmentBlock = IntPtr.Zero;
        try
        {
            var size = new Coord { X = (short)Math.Clamp(options.Columns, MinSize, short.MaxValue), Y = (short)Math.Clamp(options.Rows, MinSize, short.MaxValue) };
            Marshal.ThrowExceptionForHR(CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole));
            // The pseudo console duplicated these ends; we must release ours.
            inputRead.Dispose();
            outputWrite.Dispose();
            consoleOwnsEnds = true;

            IntPtr listSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attributeList = Marshal.AllocHGlobal(listSize);
            Ensure(InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize));
            Ensure(UpdateProcThreadAttribute(attributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));

            var startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            // Do not let the child inherit the standard handles of a GUI host.
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.lpAttributeList = attributeList;

            var env = new StringBuilder();
            foreach (var (key, value) in PtyFactory.BuildEnvironment(options.Environment).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                env.Append(key).Append('=').Append(value).Append('\0');
            env.Append('\0');
            environmentBlock = Marshal.StringToHGlobalUni(env.ToString());

            // CreateProcessW may modify the command line, so it gets its own writable, null-terminated buffer.
            var commandLine = (BuildCommandLine(options.Shell, options.Args) + "\0").ToCharArray();
            string? cwd = Directory.Exists(options.WorkingDirectory) ? options.WorkingDirectory : null;

            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, environmentBlock, cwd, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot start '{options.Shell}'");

            return new WindowsPty(pseudoConsole, info, outputRead, inputWrite);
        }
        catch
        {
            if (!consoleOwnsEnds)
            {
                ReleaseUnownedEnds(inputRead, outputWrite);
            }
            outputRead.Dispose();
            inputWrite.Dispose();
            if (pseudoConsole != IntPtr.Zero) ClosePseudoConsole(pseudoConsole);
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

    private static (SafeFileHandle Read, SafeFileHandle Write) Pipe()
    {
        Ensure(CreatePipe(out var read, out var write, IntPtr.Zero, 0));
        return (read, write);
    }

    // Runs only after a Windows API call failed before the pseudo console took over the ends, which tests cannot provoke. It is
    // kept out of Spawn so the analyzer does not see a second disposal.
    [ExcludeFromCodeCoverage(Justification = "Failure cleanup that needs a failing ConPTY or pipe API call.")]
    private static void ReleaseUnownedEnds(params IDisposable[] ends)
    {
        foreach (var end in ends)
        {
            end.Dispose();
        }
    }

    private static void Ensure(bool succeeded) { if (!succeeded) throw new Win32Exception(); }

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
        _ = ResizePseudoConsole(_pseudoConsole, new Coord { X = (short)Math.Max(MinSize, columns), Y = (short)Math.Max(MinSize, rows) });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _input.Dispose(); } catch (IOException) { /* already closed */ }
        // ClosePseudoConsole can block until pending output is drained, so never run it on the UI thread.
        var hpc = _pseudoConsole;
        var output = _output;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ClosePseudoConsole(hpc);
            try { output.Dispose(); } catch (IOException) { /* already closed */ }
        });
    }

    private const int MinSize = 2;
    private const uint INFINITE = 0xFFFFFFFF;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private record struct Coord { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private record struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private record struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [LibraryImport("kernel32.dll")]
    private static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll")]
    private static partial int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [LibraryImport("kernel32.dll")]
    private static partial void ClosePseudoConsole(IntPtr pseudoConsole);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(IntPtr list);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string? applicationName, [In, Out] char[] commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
