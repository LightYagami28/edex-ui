// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPty : IPtyConnection
{
    private const int QuoteEscapeMultiplier = 2;
    private const int QuoteEscapeOffset = 1;

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
        new Thread(WatchExit) { IsBackground = true, Name = "conpty-exit-watcher" }.Start();
    }

    private void WatchExit()
    {
        _ = WaitForSingleObject(_process, INFINITE);
        GetExitCodeProcess(_process, out var code);
        CloseHandle(_process);
        ExitCode = unchecked((int)code);
        Exited?.Invoke(this, ExitCode.Value);
    }

    internal static WindowsPty Spawn(PtyOptions options)
    {
        var (inputRead, inputWrite) = Pipe();
        var (outputRead, outputWrite) = Pipe();
        try
        {
            var pseudoConsole = CreateConsole(options, inputRead, outputWrite);
            return StartInConsole(options, pseudoConsole, outputRead, inputWrite);
        }
        catch
        {
            ReleaseUnownedEnds(inputRead, outputWrite, outputRead, inputWrite);
            throw;
        }
    }

    private static IntPtr CreateConsole(PtyOptions options, SafeFileHandle inputRead, SafeFileHandle outputWrite)
    {
        var size = new Coord { X = Dimension(options.Columns), Y = Dimension(options.Rows) };
        Marshal.ThrowExceptionForHR(CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pseudoConsole));
        inputRead.Dispose();
        outputWrite.Dispose();
        return pseudoConsole;
    }

    private static WindowsPty StartInConsole(PtyOptions options, IntPtr pseudoConsole, SafeFileHandle outputRead, SafeFileHandle inputWrite)
    {
        try
        {
            var info = StartProcess(options, pseudoConsole);
            return new WindowsPty(pseudoConsole, info, outputRead, inputWrite);
        }
        catch
        {
            ReleaseUnownedEnds(outputRead, inputWrite);
            ClosePseudoConsole(pseudoConsole);
            throw;
        }
    }

    private static ProcessInformation StartProcess(PtyOptions options, IntPtr pseudoConsole)
    {
        var attributeList = CreateAttributeList(pseudoConsole);
        try
        {
            return StartProcessWithAttributes(options, attributeList);
        }
        finally
        {
            FreeAttributeList(attributeList);
        }
    }

    private static IntPtr CreateAttributeList(IntPtr pseudoConsole)
    {
        IntPtr listSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
        var attributeList = Marshal.AllocHGlobal(listSize);
        try
        {
            Ensure(InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize));
            Ensure(UpdateProcThreadAttribute(attributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
            return attributeList;
        }
        catch
        {
            FreeAttributeList(attributeList);
            throw;
        }
    }

    private static void FreeAttributeList(IntPtr attributeList)
    {
        DeleteProcThreadAttributeList(attributeList);
        Marshal.FreeHGlobal(attributeList);
    }

    private static ProcessInformation StartProcessWithAttributes(PtyOptions options, IntPtr attributeList)
    {
        var startup = new StartupInfoEx { lpAttributeList = attributeList };
        startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
        startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        var commandLine = (BuildCommandLine(options.Shell, options.Args) + "\0").ToCharArray();
        string? cwd = Directory.Exists(options.WorkingDirectory) ? options.WorkingDirectory : null;
        var environmentBlock = Marshal.StringToHGlobalUni(EnvironmentBlock(options.Environment));
        try
        {
            Ensure(CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, environmentBlock, cwd, ref startup, out var info), options.Shell);
            return info;
        }
        finally
        {
            Marshal.FreeHGlobal(environmentBlock);
        }
    }

    private static string EnvironmentBlock(IReadOnlyDictionary<string, string>? extra)
    {
        var env = new StringBuilder();
        foreach (var (key, value) in PtyFactory.BuildEnvironment(extra).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            env.Append(key).Append('=').Append(value).Append('\0');
        }

        return env.Append('\0').ToString();
    }

    private static short Dimension(int value) => (short)Math.Clamp(value, MinSize, short.MaxValue);

    private static (SafeFileHandle Read, SafeFileHandle Write) Pipe()
    {
        Ensure(CreatePipe(out var read, out var write, IntPtr.Zero, 0));
        return (read, write);
    }

    [ExcludeFromCodeCoverage(Justification = "Failure cleanup that needs a failing ConPTY or pipe API call.")]
    private static void ReleaseUnownedEnds(params IDisposable[] ends)
    {
        foreach (var end in ends)
        {
            end.Dispose();
        }
    }

    private static void Ensure(bool succeeded)
    {
        if (!succeeded)
        {
            throw new Win32Exception();
        }
    }

    private static void Ensure(bool succeeded, string shell)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot start '{shell}'");
        }
    }

    internal static string BuildCommandLine(string executable, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder(Quote(executable));
        foreach (var arg in args)
        {
            sb.Append(' ').Append(Quote(arg));
        }

        return sb.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        var sb = new StringBuilder("\"");
        int backslashes = AppendEscaped(sb, value);
        sb.Append('\\', backslashes * QuoteEscapeMultiplier).Append('"');
        return sb.ToString();
    }

    private static int AppendEscaped(StringBuilder sb, string value)
    {
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            sb.Append('\\', c == '"' ? backslashes * QuoteEscapeMultiplier + QuoteEscapeOffset : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        return backslashes;
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _ = ResizePseudoConsole(_pseudoConsole, new Coord { X = Dimension(columns), Y = Dimension(rows) });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PtyStreams.DisposeQuietly(_input);
        var hpc = _pseudoConsole;
        var output = _output;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ClosePseudoConsole(hpc);
            PtyStreams.DisposeQuietly(output);
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
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
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
