// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

/// <summary>Pseudo terminal for Linux and macOS based on forkpty(3).</summary>
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

        var waiter = new Thread(() =>
        {
            int status;
            while (WaitPid(_pid, out status, 0) == -1 && Marshal.GetLastPInvokeError() == EINTR)
            {
                // Interrupted by a signal: retry until the child state is available.
            }
            int code = (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
            ExitCode = code;
            Exited?.Invoke(this, code);
        })
        { IsBackground = true, Name = "pty-exit-watcher" };
        waiter.Start();
    }

    // Full optimisation from the first call: the child branch must not need the JIT after fork.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe UnixPty Spawn(PtyOptions options)
    {
        string executable = ResolveExecutable(options.Shell);
        var env = PtyFactory.BuildEnvironment(options.Environment);
        string cwd = !string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory)
            ? options.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Everything the child needs is marshalled before fork: after fork only async-signal-safe calls are made.
        var allocations = new List<IntPtr>();
        static IntPtr Str(List<IntPtr> allocations, string s)
        {
            var p = Marshal.StringToHGlobalAnsi(s);
            allocations.Add(p);
            return p;
        }
        static IntPtr Array(List<IntPtr> allocations, IEnumerable<string> items)
        {
            var pointers = items.Select(item => Str(allocations, item)).Append(IntPtr.Zero).ToArray();
            var block = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
            for (int i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(block, i * IntPtr.Size, pointers[i]);
            allocations.Add(block);
            return block;
        }

        IntPtr path = Str(allocations, executable);
        IntPtr argv = Array(allocations, new[] { executable }.Concat(options.Args));
        IntPtr envp = Array(allocations, env.Select(e => $"{e.Key}={e.Value}"));
        IntPtr dir = Str(allocations, cwd);
        var size = new WinSize { Rows = (ushort)Math.Max(2, options.Rows), Columns = (ushort)Math.Max(2, options.Columns) };

        // The child calls libc through raw function pointers resolved here, so it runs no marshalling stub.
        var libc = NativeLibrary.Load("libc", typeof(UnixPty).Assembly, null);
        var chdir = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(libc, "chdir");
        var execve = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)NativeLibrary.GetExport(libc, "execve");
        var exit = (delegate* unmanaged<int, void>)NativeLibrary.GetExport(libc, "_exit");

        int pid = ForkPty(out int master, IntPtr.Zero, IntPtr.Zero, ref size);
        // Child: only async-signal-safe calls. Exit 127 unless execve replaced the process.
        if (pid == 0)
        {
            exit(chdir(dir) == 0 && execve(path, argv, envp) == 0 ? 0 : ChildFailed);
        }

        int error = Marshal.GetLastPInvokeError();
        foreach (var p in allocations) Marshal.FreeHGlobal(p);
        if (pid < 0) throw new IOException($"forkpty failed (errno {error})");
        return new UnixPty(pid, master);
    }

    internal static string ResolveExecutable(string shell)
    {
        if (shell.Contains('/')) return shell;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, shell);
            if (File.Exists(candidate)) return candidate;
        }
        return shell;
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var size = new WinSize { Rows = (ushort)Math.Max(2, rows), Columns = (ushort)Math.Max(2, columns) };
        _ = Ioctl(_master, OperatingSystem.IsMacOS() ? (UIntPtr)0x80087467u : (UIntPtr)0x5414u, ref size);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ = Kill(_pid, SIGHUP);
        try { _input.Dispose(); } catch (IOException) { /* already closed */ }
        try { _output.Dispose(); } catch (IOException) { /* already closed */ }
    }

    private const int SIGHUP = 1;
    private const int EINTR = 4;
    private const int ChildFailed = 127;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize { public ushort Rows, Columns, XPixels, YPixels; }

    [LibraryImport("libc", EntryPoint = "forkpty", SetLastError = true)]
    private static partial int ForkPty(out int master, IntPtr name, IntPtr termios, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, UIntPtr request, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, out int status, int options);

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static partial int Dup(int fd);

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int pid, int signal);
}
