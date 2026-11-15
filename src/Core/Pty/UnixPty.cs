// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

internal sealed partial class UnixPty : IPtyConnection
{
    private readonly int _pid;
    private readonly int _master;
    private readonly FileStream _output;
    private readonly FileStream _input;
    private int _disposed;

    public int ProcessId => _pid;
    public Stream Output => _output;
    public Stream Input => _input;
    public int? ExitCode { get; private set; }
    public event EventHandler<int>? Exited;

    private UnixPty(int pid, int master)
    {
        _pid = pid;
        _master = master;
        _output = new FileStream(new SafeFileHandle((IntPtr)master, ownsHandle: true), FileAccess.Read, 1, false);
        _input = new FileStream(new SafeFileHandle((IntPtr)Dup(master), ownsHandle: true), FileAccess.Write, 1, false);
        new Thread(WatchExit) { IsBackground = true, Name = "pty-exit-watcher" }.Start();
    }

    private void WatchExit()
    {
        int code = ExitCodeFromStatus(WaitForExit(_pid));
        ExitCode = code;
        Exited?.Invoke(this, code);
    }

    private static int WaitForExit(int pid)
    {
        int status;
        bool interrupted;
        do
        {
            interrupted = WaitPid(pid, out status, 0) == -1 && Marshal.GetLastPInvokeError() == EINTR;
        }
        while (interrupted);
        return status;
    }

    private static int ExitCodeFromStatus(int status) =>
        (status & SignalMask) == 0 ? (status >> ExitStatusShift) & ExitStatusMask : SignalExitBase + (status & SignalMask);

    internal static UnixPty Spawn(PtyOptions options)
    {
        string executable = ResolveExecutable(options.Shell);
        var env = PtyFactory.BuildEnvironment(options.Environment);
        var size = WindowSize(options.Columns, options.Rows);
        using var native = new NativeAllocations();
        var launch = new Launch(
            native.Text(executable),
            native.Array(new[] { executable }.Concat(options.Args)),
            native.Array(env.Select(e => $"{e.Key}={e.Value}")),
            native.Text(WorkingDirectory(options)));
        int pid = Fork(launch, ref size, out int master);
        int error = Marshal.GetLastPInvokeError();
        if (pid < 0)
        {
            throw new IOException($"forkpty failed (errno {error})");
        }

        return new UnixPty(pid, master);
    }

    private static int Fork(Launch launch, ref WinSize size, out int master)
    {
        Marshal.PrelinkAll(typeof(UnixPty));
        int pid = ForkPty(out master, IntPtr.Zero, IntPtr.Zero, ref size);
        if (pid == 0)
        {
            Exit(ChDir(launch.Directory) == 0 && ExecVe(launch.Path, launch.Argv, launch.Envp) == 0 ? 0 : ChildFailed);
        }

        return pid;
    }

    private static string WorkingDirectory(PtyOptions options) =>
        !string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory)
            ? options.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static WinSize WindowSize(int columns, int rows) =>
        new() { Rows = (ushort)Math.Max(MinSize, rows), Columns = (ushort)Math.Max(MinSize, columns) };

    internal static string ResolveExecutable(string shell)
    {
        if (shell.Contains('/'))
        {
            return shell;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, shell);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return shell;
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var size = WindowSize(columns, rows);
        _ = Ioctl(_master, OperatingSystem.IsMacOS() ? TiocswinszMac : TiocswinszLinux, ref size);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = Kill(_pid, SIGHUP);
        PtyStreams.DisposeQuietly(_input);
        PtyStreams.DisposeQuietly(_output);
    }

    private const int MinSize = 2;
    private const int SignalMask = 0x7f;
    private const int ExitStatusShift = 8;
    private const int ExitStatusMask = 0xff;
    private const int SignalExitBase = 128;
    private const int SIGHUP = 1;
    private const int EINTR = 4;
    private const int ChildFailed = 127;
    private const nuint TiocswinszLinux = 0x5414;
    private const nuint TiocswinszMac = 0x80087467;

    [StructLayout(LayoutKind.Sequential)]
    private record struct WinSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort XPixels;
        public ushort YPixels;
    }

    private readonly record struct Launch(IntPtr Path, IntPtr Argv, IntPtr Envp, IntPtr Directory);

    private sealed class NativeAllocations : IDisposable
    {
        private readonly List<IntPtr> _pointers = [];

        public IntPtr Text(string value) => Track(Marshal.StringToHGlobalAnsi(value));

        public IntPtr Array(IEnumerable<string> items)
        {
            var pointers = items.Select(Text).Append(IntPtr.Zero).ToArray();
            var block = Track(Marshal.AllocHGlobal(IntPtr.Size * pointers.Length));
            for (int i = 0; i < pointers.Length; i++)
            {
                Marshal.WriteIntPtr(block, i * IntPtr.Size, pointers[i]);
            }

            return block;
        }

        public void Dispose() => _pointers.ForEach(Marshal.FreeHGlobal);

        private IntPtr Track(IntPtr pointer)
        {
            _pointers.Add(pointer);
            return pointer;
        }
    }

    [LibraryImport("libc", EntryPoint = "forkpty", SetLastError = true)]
    private static partial int ForkPty(out int master, IntPtr name, IntPtr termios, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "chdir")]
    private static partial int ChDir(IntPtr path);

    [LibraryImport("libc", EntryPoint = "execve")]
    private static partial int ExecVe(IntPtr path, IntPtr argv, IntPtr envp);

    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void Exit(int status);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, UIntPtr request, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, out int status, int options);

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static partial int Dup(int fd);

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int pid, int signal);
}
